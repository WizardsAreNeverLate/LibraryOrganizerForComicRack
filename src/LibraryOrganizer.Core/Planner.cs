using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibraryOrganizer;

/// <summary>
/// One planning run. First every book is admitted and rendered under every profile, in selection order, which fixes the
/// counter ordinals. Then each book's targets are checked against the disk and the targets planned so far.
/// </summary>
/// <remarks>
/// A book is resolved as a unit, and its operations are emitted when it is done. A book whose target is held by another
/// selected book's file resolves that book first, so by the time an operation is emitted, everything it waits on already
/// is. That makes emission order the execution order. A book that is met again while it is being resolved is a cycle.
/// </remarks>
internal sealed class Planner
{
	private static readonly Regex NumberSuffix = new(@" \(\d+\)$", RegexOptions.CultureInvariant);

	private readonly OrganizeInput input;
	private readonly Decisions decisions;
	private readonly Dictionary<ProfileId, int> rendered = [];
	private readonly Dictionary<QuestionKey, int> questionIndex = [];
	private readonly List<Question> questions = [];
	private readonly Dictionary<AbsPath, BookPlan> fileOwners = [];
	private readonly Dictionary<AbsPath, EntryKey> claims = [];
	private readonly List<Conflict> conflicts = [];
	private readonly List<Operation> operations = [];

	public Planner(OrganizeInput input, Decisions decisions)
	{
		this.input = input;
		this.decisions = decisions;
	}

	private enum Progress { Pending, Resolving, Done }

	private sealed class Slot(BookRecord book, Profile profile)
	{
		public BookRecord Book { get; } = book;
		public Profile Profile { get; } = profile;
		public EntryKey Key { get; } = new(book.Id, profile.Id);

		/// <summary>Set when the entry ends with no operation.</summary>
		public EntryOutcome? Outcome { get; set; }

		/// <summary>The rendered path, before conflicts.</summary>
		public AbsPath? Target { get; set; }

		public string? FailReason { get; set; }
		public List<Operation> Operations { get; } = [];

		public EntryOutcome FinalOutcome
			=> Outcome ?? (FailReason is null ? new EntryOutcome.Planned(Operations) : new EntryOutcome.PlannedAsFailed(Operations, FailReason));
	}

	private sealed class BookPlan(BookRecord book, List<Slot> slots)
	{
		public BookRecord Book { get; } = book;
		public List<Slot> Slots { get; } = slots;
		public Progress Progress { get; set; }

		public bool MovesAwayFrom(AbsPath path)
			=> Slots.Any(slot => slot.Operations.OfType<Operation.MoveFile>().Any(move => move.From.Equals(path)));
	}

	public Plan Run()
	{
		List<BookPlan> books = input.Selection
			.GroupBy(book => book.Id)
			.Select(group => Render(group.First()))
			.ToList();
		foreach (BookPlan book in books)
		{
			if (book.Book.Location is BookLocation.File file && !fileOwners.ContainsKey(file.Path))
			{
				fileOwners[file.Path] = book;
			}
		}
		foreach (BookPlan book in books)
		{
			if (book.Progress == Progress.Pending)
			{
				Resolve(book);
			}
		}
		AddPruning();
		return new Plan(
			new PlanKind.Organize(),
			books.SelectMany(book => book.Slots).Select(slot => new PlanEntry(slot.Key, slot.Book.Caption, slot.Profile.Name, slot.FinalOutcome)).ToList(),
			questions,
			conflicts,
			operations,
			decisions.Answers);
	}

	private BookPlan Render(BookRecord book)
	{
		bool fileExists = book.Location is BookLocation.File file && input.Disk.FileExists(file.Path);
		List<Slot> slots = input.RunOrder.Select(profile => new Slot(book, profile)).ToList();
		var accepted = new List<(Slot Slot, Admission.Accept Admission)>();
		foreach (Slot slot in slots)
		{
			switch (BookPath.Admit(slot.Profile, book, input.Series, fileExists))
			{
				case Admission.Skip skip:
					slot.Outcome = new EntryOutcome.Skipped(skip.Reason);
					break;
				case Admission.Fail fail:
					slot.Outcome = new EntryOutcome.Failed(fail.Reason);
					break;
				case Admission.Accept accept:
					accepted.Add((slot, accept));
					break;
			}
		}

		Slot? mover = null;
		foreach ((Slot slot, Admission.Accept admission) in accepted.Where(entry => entry.Slot.Profile.Action is ProfileAction.Move).Reverse())
		{
			if (mover is not null)
			{
				slot.Outcome = new EntryOutcome.Overridden(mover.Profile.Name);
			}
			else if (Render(slot, admission))
			{
				mover = slot;
			}
		}
		foreach ((Slot slot, Admission.Accept admission) in accepted.Where(entry => entry.Slot.Profile.Action is ProfileAction.Copy))
		{
			Render(slot, admission);
		}
		return new BookPlan(book, slots);
	}

	private bool Render(Slot slot, Admission.Accept admission)
	{
		rendered.TryGetValue(slot.Profile.Id, out int ordinal);
		var context = new RenderContext(slot.Profile.Style, input.Series, decisions.Answers, slot.Profile.DontAskWhenOneValue, ordinal, Preview: false);
		switch (BookPath.Render(slot.Profile, slot.Book, admission.FailedRoot, context))
		{
			case PathRender.Path path:
				slot.Target = path.Levels.Aggregate(path.Root, (folder, level) => folder.Combine(level)).Combine(path.FileName);
				slot.FailReason = admission.FailReason;
				break;
			case PathRender.Asks asks:
				slot.Outcome = new EntryOutcome.WaitingForAnswer(asks.Questions.Select(question => question.Key).ToList());
				foreach (Question question in asks.Questions)
				{
					Ask(question);
				}
				break;
			case PathRender.Fails fails:
				slot.Outcome = new EntryOutcome.Failed(fails.Reason);
				return false;
		}
		rendered[slot.Profile.Id] = ordinal + 1;
		return true;
	}

	private void Ask(Question question)
	{
		if (questionIndex.TryGetValue(question.Key, out int index))
		{
			questions[index] = questions[index] with { Waiting = questions[index].Waiting + 1 };
		}
		else
		{
			questionIndex[question.Key] = questions.Count;
			questions.Add(question with { Waiting = 1 });
		}
	}

	private void Resolve(BookPlan book)
	{
		book.Progress = Progress.Resolving;
		foreach (Slot slot in book.Slots.Where(slot => slot.Target is not null))
		{
			Resolve(book, slot);
		}
		book.Progress = Progress.Done;
		foreach (Slot slot in book.Slots.OrderBy(slot => slot.Profile.Action is ProfileAction.Move ? 1 : 0))
		{
			for (int i = 0; i < slot.Operations.Count; i++)
			{
				slot.Operations[i] = slot.Operations[i] with { Id = NextId() };
				operations.Add(slot.Operations[i]);
			}
		}
	}

	private void Resolve(BookPlan book, Slot slot)
	{
		AbsPath? source = (slot.Book.Location as BookLocation.File)?.Path;
		AbsPath target = slot.Target!.Value;
		IReadOnlyList<Sidecar> sidecars = SidecarsOf(slot);
		var tried = new HashSet<AbsPath>();
		while (true)
		{
			if (source is { } from && target.Equals(from))
			{
				if (slot.Profile.Action is ProfileAction.Move && target.DiffersOnlyByCase(from))
				{
					slot.Operations.Add(new Operation.RenameCase(default, slot.Key, from, target));
					Claim(target, slot);
				}
				else
				{
					slot.Outcome = new EntryOutcome.AlreadyInPlace();
				}
				return;
			}

			AbsPath[] sidecarTargets = [.. sidecars.Select(sidecar => sidecar.TargetFor(target))];
			(ConflictKind? kind, bool canReplace) = FindConflict(book, target);
			for (int i = 0; kind is null && i < sidecarTargets.Length; i++)
			{
				(ConflictKind? xmlKind, _) = FindConflict(book, sidecarTargets[i]);
				(kind, canReplace) = xmlKind switch
				{
					null => (null, false),
					ConflictKind.PathTooLong => (xmlKind, false),
					_ => (new ConflictKind.SidecarTaken(sidecarTargets[i]), xmlKind is ConflictKind.TargetExists),
				};
			}
			if (kind is null)
			{
				AddMove(slot, target, sidecars);
				return;
			}

			List<ResolutionChoice> allowed = [ResolutionChoice.Skip];
			if (canReplace)
			{
				allowed.Add(ResolutionChoice.Replace);
			}
			if (kind is not ConflictKind.PathTooLong)
			{
				allowed.Add(ResolutionChoice.KeepBoth);
			}
			allowed.Add(ResolutionChoice.UsePath);
			bool Taken(AbsPath candidate) => claims.ContainsKey(candidate) || input.Disk.FileExists(candidate);
			AbsPath? keepBothPath = allowed.Contains(ResolutionChoice.KeepBoth)
				? FreeName(target, candidate => Taken(candidate) || sidecars.Any(sidecar => Taken(sidecar.TargetFor(candidate))))
				: null;

			bool Usable(Resolution? resolution)
				=> resolution is not null
					&& allowed.Contains(resolution.Choice)
					&& !(resolution is Resolution.UsePath typed && (typed.Path.Equals(target) || tried.Contains(typed.Path)));

			var key = new ConflictKey(slot.Key, target);
			Resolution? chosen = decisions.Resolutions.TryGetValue(key, out Resolution? choice) && Usable(choice) ? choice
				: Usable(decisions.Default) ? decisions.Default
				: null;
			conflicts.Add(new Conflict(key, kind, allowed, keepBothPath, chosen));

			switch (chosen)
			{
				case null:
					slot.Outcome = new EntryOutcome.Blocked(key, Operation(slot, target));
					Claim(target, slot);
					return;
				case Resolution.Skip:
					slot.Outcome = new EntryOutcome.Skipped($"{kind.Describe()}: {target}");
					return;
				case Resolution.Replace:
					if (kind is ConflictKind.TargetExists exists)
					{
						BookId? existing = exists.ExistingBook;
						bool transfer = existing is not null && slot.Profile.KeepReadProgressOnReplace && slot.Profile.Action is ProfileAction.Move;
						slot.Operations.Add(new Operation.RecycleFile(default, slot.Key, target, existing, transfer ? slot.Key.Book : null));
					}
					foreach (AbsPath oldXml in sidecarTargets.Where(path => input.Disk.FileExists(path) && !claims.ContainsKey(path)))
					{
						slot.Operations.Add(new Operation.RecycleFile(default, slot.Key, oldXml, null, null));
					}
					AddMove(slot, target, sidecars);
					return;
				case Resolution.KeepBoth:
					tried.Add(target);
					target = keepBothPath!.Value;
					break;
				case Resolution.UsePath typed:
					tried.Add(target);
					target = typed.Path;
					break;
			}
		}
	}

	private void AddMove(Slot slot, AbsPath target, IReadOnlyList<Sidecar> sidecars)
	{
		slot.Operations.Add(Operation(slot, target));
		Claim(target, slot);
		foreach (Sidecar sidecar in sidecars)
		{
			AbsPath sidecarTarget = sidecar.TargetFor(target);
			if (!sidecarTarget.Equals(sidecar.From))
			{
				slot.Operations.Add(new Operation.MoveFile(default, slot.Key, sidecar.From, sidecarTarget, Sidecar: true));
				Claim(sidecarTarget, slot);
			}
		}
	}

	/// <param name="FullName">Named after the whole file name, "x.pdf.xml", rather than "x.xml".</param>
	private sealed record Sidecar(AbsPath From, bool FullName)
	{
		/// <summary>The sidecar's name for a book moved to <paramref name="bookTarget"/>, in the same form as now.</summary>
		public AbsPath TargetFor(AbsPath bookTarget)
			=> bookTarget.Parent.Combine((FullName ? bookTarget.Name : Stem(bookTarget)) + From.Extension);
	}

	/// <summary>
	/// The xml files next to a book that a Move profile moves: "x.pdf.xml", and "x.xml" unless another file shares the
	/// name "x", as "x.cbz" and "x.pdf" beside one "x.xml", where it could be either one's and so stays where it is.
	/// </summary>
	private IReadOnlyList<Sidecar> SidecarsOf(Slot slot)
	{
		if (slot.Profile.Action is not ProfileAction.Move || slot.Book.Location is not BookLocation.File { Path: var book }
			|| IsXml(book))
		{
			return [];
		}
		IReadOnlyList<AbsPath> beside = input.Disk.Children(book.Parent);
		var sidecars = new List<Sidecar>();
		if (beside.Where(path => IsXml(path) && Stem(path).Equals(book.Name, StringComparison.OrdinalIgnoreCase)).Select(path => (AbsPath?)path).FirstOrDefault() is AbsPath full)
		{
			sidecars.Add(new Sidecar(full, FullName: true));
		}
		string stem = Stem(book);
		AbsPath[] sameStem = [.. beside.Where(path => !path.Equals(book) && Stem(path).Equals(stem, StringComparison.OrdinalIgnoreCase))];
		if (sameStem.Length == 1 && IsXml(sameStem[0]))
		{
			sidecars.Add(new Sidecar(sameStem[0], FullName: false));
		}
		return sidecars;
	}

	private static bool IsXml(AbsPath path) => path.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);

	private static string Stem(AbsPath path) => path.Name.Substring(0, path.Name.Length - path.Extension.Length);

	private void Claim(AbsPath target, Slot slot)
	{
		if (!claims.ContainsKey(target))
		{
			claims[target] = slot.Key;
		}
	}

	private (ConflictKind? Kind, bool CanReplace) FindConflict(BookPlan book, AbsPath target)
	{
		if (target.Value.Length > input.MaxPathLength)
		{
			return (new ConflictKind.PathTooLong(target.Value.Length, input.MaxPathLength), false);
		}
		BookPlan? owner = fileOwners.TryGetValue(target, out BookPlan? found) && found != book ? found : null;
		if (owner?.Progress == Progress.Pending)
		{
			Resolve(owner);
		}
		if (claims.TryGetValue(target, out EntryKey earlier))
		{
			return (new ConflictKind.SameTargetAs(earlier), false);
		}
		if (!input.Disk.FileExists(target) || owner?.Progress == Progress.Done && owner.MovesAwayFrom(target))
		{
			return (null, false);
		}
		BookId? existing = input.LibraryFiles.TryGetValue(target, out BookId id) ? id : null;
		return (new ConflictKind.TargetExists(existing), owner?.Progress != Progress.Resolving);
	}

	private Operation Operation(Slot slot, AbsPath target) => slot.Book.Location switch
	{
		BookLocation.File file when slot.Profile.Action is ProfileAction.Copy copy => new Operation.CopyFile(default, slot.Key, file.Path, target, copy.AddToLibrary),
		BookLocation.File file => new Operation.MoveFile(default, slot.Key, file.Path, target),
		_ => new Operation.WriteCover(default, slot.Key, target, slot.Profile.FilelessCovers ?? CoverFormat.Jpg),
	};

	/// <summary>"name (n).ext" in the target's folder, replacing a number the name already ends with.</summary>
	internal static AbsPath FreeName(AbsPath target, Func<AbsPath, bool> taken)
	{
		string extension = target.Extension;
		string stem = NumberSuffix.Replace(target.Name.Substring(0, target.Name.Length - extension.Length), "");
		for (int n = 1; ; n++)
		{
			AbsPath candidate = target.Parent.Combine($"{stem} ({n}){extension}");
			if (!taken(candidate))
			{
				return candidate;
			}
		}
	}

	private void AddPruning()
	{
		Dictionary<ProfileId, Profile> profiles = input.RunOrder.GroupBy(profile => profile.Id).ToDictionary(group => group.Key, group => group.First());
		List<AbsPath> candidates = operations
			.OfType<Operation.MoveFile>()
			.Where(move => move.Entry.Profile is { } id && profiles[id].Pruning is not null)
			.Select(move => move.From.Parent)
			.Distinct()
			.ToList();
		if (candidates.Count == 0)
		{
			return;
		}
		IEnumerable<AbsPath> profileFolders = input.RunOrder.SelectMany(profile => new[]
		{
			profile.Destination switch
			{
				Destination.FolderAndName both => both.BaseFolder,
				Destination.FolderOnly folderOnly => folderOnly.BaseFolder,
				_ => (AbsPath?)null,
			},
			(profile.Required?.Route as RequiredFields.FailRoute.MoveUnder)?.Folder,
		}).OfType<AbsPath>();
		operations.Add(new Operation.PruneFolders(
			NextId(),
			candidates,
			profileFolders.Concat(candidates.Select(Root)).Distinct().ToList(),
			input.RunOrder.SelectMany(profile => profile.Pruning?.Keep ?? []).Distinct().ToList()));
	}

	private OpId NextId() => new(operations.Count + 1);

	private static AbsPath Root(AbsPath path)
	{
		while (!path.IsRoot)
		{
			path = path.Parent;
		}
		return path;
	}
}
