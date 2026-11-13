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

	/// <summary>False when the profile cannot produce a path for the book. Only a render that produces one, or waits for an answer, takes a counter ordinal.</summary>
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
		// Copies read the file where it is now, so they run before the move takes it away.
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

			(ConflictKind? kind, bool canReplace) = FindConflict(book, target);
			if (kind is null)
			{
				slot.Operations.Add(Operation(slot, target));
				Claim(target, slot);
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
			AbsPath? keepBothPath = allowed.Contains(ResolutionChoice.KeepBoth) ? FreeName(target) : null;

			bool Usable(Resolution? resolution)
				=> resolution is not null
					&& allowed.Contains(resolution.Choice)
					// A typed path that leads back to a path already refused would never end.
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
					BookId? existing = (kind as ConflictKind.TargetExists)?.ExistingBook;
					bool transfer = existing is not null && slot.Profile.KeepReadProgressOnReplace && slot.Profile.Action is ProfileAction.Move;
					slot.Operations.Add(new Operation.RecycleFile(default, slot.Key, target, existing, transfer ? slot.Key.Book : null));
					slot.Operations.Add(Operation(slot, target));
					Claim(target, slot);
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

	/// <summary>The first entry to want a path keeps it, whether it is planned or still blocked there.</summary>
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
		// In a cycle the file at the target belongs to a book that is still being planned, so it cannot be recycled.
		return (new ConflictKind.TargetExists(existing), owner?.Progress != Progress.Resolving);
	}

	private Operation Operation(Slot slot, AbsPath target) => slot.Book.Location switch
	{
		BookLocation.File file when slot.Profile.Action is ProfileAction.Copy copy => new Operation.CopyFile(default, slot.Key, file.Path, target, copy.AddToLibrary),
		BookLocation.File file => new Operation.MoveFile(default, slot.Key, file.Path, target),
		_ => new Operation.WriteCover(default, slot.Key, target, slot.Profile.FilelessCovers ?? CoverFormat.Jpg),
	};

	/// <summary>"name (n).ext" in the target's folder, replacing a number the name already ends with.</summary>
	private AbsPath FreeName(AbsPath target)
	{
		string extension = target.Extension;
		string stem = NumberSuffix.Replace(target.Name.Substring(0, target.Name.Length - extension.Length), "");
		for (int n = 1; ; n++)
		{
			AbsPath candidate = target.Parent.Combine($"{stem} ({n}){extension}");
			if (!claims.ContainsKey(candidate) && !input.Disk.FileExists(candidate))
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
