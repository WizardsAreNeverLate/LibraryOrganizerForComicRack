using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>
/// The result of planning: what would happen to every book under every profile. A value: an answer or a conflict choice
/// goes into <see cref="Decisions"/> and the host plans again.
/// </summary>
public sealed class Plan
{
	internal Plan(
		PlanKind kind, IReadOnlyList<PlanEntry> entries, IReadOnlyList<Question> questions, IReadOnlyList<Conflict> conflicts,
		IReadOnlyList<Operation> operations, AnswerBook answers)
	{
		Kind = kind;
		Entries = entries;
		Questions = questions;
		Conflicts = conflicts;
		Operations = operations;
		Answers = answers;
	}

	public PlanKind Kind { get; }

	/// <summary>One row per book and profile, in selection order then run order.</summary>
	public IReadOnlyList<PlanEntry> Entries { get; }

	/// <summary>Distinct open multi-value questions, in the order they first block an entry.</summary>
	public IReadOnlyList<Question> Questions { get; }

	/// <summary>Every conflict, open or resolved. Open ones block <see cref="Approve"/>.</summary>
	public IReadOnlyList<Conflict> Conflicts { get; }

	/// <summary>The operations planned so far, in execution order. Entries that wait or are blocked contribute none.</summary>
	public IReadOnlyList<Operation> Operations { get; }

	/// <summary>The answers this plan used.</summary>
	public AnswerBook Answers { get; }

	/// <summary>What stops <see cref="Approve"/>, one line per open question and conflict.</summary>
	public IReadOnlyList<string> Blockers =>
	[
		.. Questions.Select(question => $"{question.FieldName} needs a choice for {question.ExampleCaption}"),
		.. Conflicts.Where(conflict => conflict.IsOpen).Select(conflict => $"{conflict.Kind.Describe()}: {conflict.Key.Target}"),
	];

	/// <summary>An approved plan when no question and no conflict is open; otherwise null.</summary>
	public ApprovedPlan? Approve()
		=> Questions.Count == 0 && Conflicts.All(conflict => !conflict.IsOpen)
			? new ApprovedPlan(new RunId(Guid.NewGuid()), Kind, Operations, Entries)
			: null;

	/// <summary>Counts per profile, derived from <see cref="Entries"/>, in the order profiles first appear.</summary>
	public IReadOnlyList<ProfileTally> Tally()
		=> Entries
			.GroupBy(entry => (entry.Key.Profile, entry.ProfileName))
			.Select(group => new ProfileTally(
				group.Key.ProfileName ?? "",
				Planned: group.Count(entry => entry.Outcome is EntryOutcome.Planned),
				InPlace: group.Count(entry => entry.Outcome is EntryOutcome.AlreadyInPlace),
				Skipped: group.Count(entry => entry.Outcome is EntryOutcome.Skipped or EntryOutcome.Overridden),
				Failed: group.Count(entry => entry.Outcome is EntryOutcome.Failed or EntryOutcome.PlannedAsFailed),
				Waiting: group.Count(entry => entry.Outcome is EntryOutcome.WaitingForAnswer),
				Blocked: group.Count(entry => entry.Outcome is EntryOutcome.Blocked)))
			.ToList();
}

public abstract record PlanKind
{
	private PlanKind() { }

	public sealed record Organize : PlanKind;
	public sealed record Undo(RunId Of) : PlanKind;
}

/// <summary>Identifies an entry across re-plans: the same book under the same profile.</summary>
public readonly record struct EntryKey(BookId Book, ProfileId? Profile);

public sealed record PlanEntry(EntryKey Key, string BookCaption, string? ProfileName, EntryOutcome Outcome);

/// <summary>What happens to one book under one profile. Closed.</summary>
public abstract record EntryOutcome
{
	private EntryOutcome() { }

	public sealed record Planned(IReadOnlyList<Operation> Operations) : EntryOutcome;

	public sealed record AlreadyInPlace : EntryOutcome;

	/// <summary>Filtered out, fileless with no cover policy, or a conflict resolved with Skip.</summary>
	public sealed record Skipped(string Reason) : EntryOutcome;

	/// <summary>A later Move profile in the run order moves the book.</summary>
	public sealed record Overridden(string ByProfile) : EntryOutcome;

	/// <summary>Missing file, blank file name, a required field empty under the Skip route, fileless without a cover.</summary>
	public sealed record Failed(string Reason) : EntryOutcome;

	/// <summary>A required field is empty under the MoveUnder route: the book is planned, and reported as failed.</summary>
	public sealed record PlannedAsFailed(IReadOnlyList<Operation> Operations, string Reason) : EntryOutcome;

	/// <summary>Every multi-value question the entry's templates still need.</summary>
	public sealed record WaitingForAnswer(IReadOnlyList<QuestionKey> Questions) : EntryOutcome;

	/// <param name="Pending">The operation the entry gets once the conflict is resolved in its favour. Its id is not assigned.</param>
	public sealed record Blocked(ConflictKey Conflict, Operation Pending) : EntryOutcome;
}

/// <param name="Skipped">Includes entries a later Move profile overrides.</param>
/// <param name="Failed">Includes books planned into the failed-books folder.</param>
public sealed record ProfileTally(string Profile, int Planned, int InPlace, int Skipped, int Failed, int Waiting, int Blocked);

public readonly record struct OpId(int Value);

/// <summary>A file-system change plus its library effect. Closed, plain data: the executor gives each case its behavior.</summary>
public abstract record Operation
{
	private Operation() { }

	/// <summary>Unique within a plan, and assigned in execution order starting at 1.</summary>
	public abstract OpId Id { get; init; }

	/// <summary>The plan entry this operation belongs to; default for the run-level <see cref="PruneFolders"/>.</summary>
	public abstract EntryKey Entry { get; init; }

	/// <summary>The file moves and the library book's path follows. The target folder is created.</summary>
	public sealed record MoveFile(OpId Id, EntryKey Entry, AbsPath From, AbsPath To) : Operation;

	/// <summary>A change of spelling only, done through a temporary name: CE's ComicBook.RenameFile ignores it.</summary>
	public sealed record RenameCase(OpId Id, EntryKey Entry, AbsPath From, AbsPath To) : Operation;

	/// <summary>Copies the file. With AddToLibrary the library gets an entry with the source's metadata and a new id.</summary>
	public sealed record CopyFile(OpId Id, EntryKey Entry, AbsPath From, AbsPath To, bool AddToLibrary) : Operation;

	/// <summary>Writes the custom cover of the fileless book <c>Entry.Book</c>. The library entry does not change.</summary>
	public sealed record WriteCover(OpId Id, EntryKey Entry, AbsPath To, CoverFormat Format) : Operation;

	/// <summary>
	/// Sends a file to the Recycle Bin, from a Replace resolution. ExistingBook leaves the library, and when
	/// TransferReadProgressTo is set, its read progress goes to that book first.
	/// </summary>
	public sealed record RecycleFile(OpId Id, EntryKey Entry, AbsPath Path, BookId? ExistingBook, BookId? TransferReadProgressTo) : Operation;

	/// <summary>
	/// Last operation of a run. Removes each folder in Candidates that is empty at execution time, then its parents,
	/// never a folder in StopAt or above one, and never one in Keep.
	/// </summary>
	public sealed record PruneFolders(OpId Id, IReadOnlyList<AbsPath> Candidates, IReadOnlyList<AbsPath> StopAt, IReadOnlyList<AbsPath> Keep) : Operation
	{
		public override EntryKey Entry { get; init; }
	}
}

public readonly record struct ConflictKey(EntryKey Entry, AbsPath Target);

public abstract record ConflictKind
{
	private ConflictKind() { }

	/// <summary>A file the plan does not move away is at the target. ExistingBook is set when it is in the library.</summary>
	public sealed record TargetExists(BookId? ExistingBook) : ConflictKind;

	/// <summary>An entry planned earlier already targets this path.</summary>
	public sealed record SameTargetAs(EntryKey Earlier) : ConflictKind;

	public sealed record PathTooLong(int Length, int Max) : ConflictKind;

	public string Describe() => this switch
	{
		TargetExists => "A file already exists",
		SameTargetAs => "Another book in this run goes to the same path",
		PathTooLong tooLong => $"The path has {tooLong.Length} characters, and the limit is {tooLong.Max}",
		_ => "",
	};
}

/// <param name="Allowed">The resolutions this conflict accepts.</param>
/// <param name="KeepBothPath">Where Keep both puts the file: "name (n).ext", free on disk and in the plan. Null when Keep both is not allowed.</param>
/// <param name="Chosen">The resolution the plan applied, explicit or default. Null while the conflict is open.</param>
public sealed record Conflict(ConflictKey Key, ConflictKind Kind, IReadOnlyList<ResolutionChoice> Allowed, AbsPath? KeepBothPath, Resolution? Chosen)
{
	public bool IsOpen => Chosen is null;
}

public enum ResolutionChoice { Skip, Replace, KeepBoth, UsePath }

public abstract record Resolution
{
	private Resolution() { }

	public sealed record Skip : Resolution;
	public sealed record Replace : Resolution;
	public sealed record KeepBoth : Resolution;

	/// <summary>A path the user typed. It is checked like any target, so it can conflict again under its own key.</summary>
	public sealed record UsePath(AbsPath Path) : Resolution;

	public ResolutionChoice Choice => this switch
	{
		Skip => ResolutionChoice.Skip,
		Replace => ResolutionChoice.Replace,
		KeepBoth => ResolutionChoice.KeepBoth,
		_ => ResolutionChoice.UsePath,
	};
}

/// <summary>Everything the user has decided so far. Planning again with more decisions is how a plan changes.</summary>
/// <param name="Resolutions">Explicit choices. One the conflict does not allow is ignored.</param>
/// <param name="Default">"Do this for all": applies to every conflict that allows it and has no usable explicit choice.</param>
public sealed record Decisions(AnswerBook Answers, IReadOnlyDictionary<ConflictKey, Resolution> Resolutions, Resolution? Default)
{
	public static Decisions None { get; } = new(AnswerBook.Empty, new Dictionary<ConflictKey, Resolution>(), null);

	public Decisions WithAnswer(QuestionKey key, Answer answer) => this with { Answers = Answers.With(key, answer) };

	public Decisions WithResolution(ConflictKey key, Resolution resolution)
	{
		Dictionary<ConflictKey, Resolution> resolutions = Resolutions.ToDictionary(pair => pair.Key, pair => pair.Value);
		resolutions[key] = resolution;
		return this with { Resolutions = resolutions };
	}

	public Decisions WithDefault(Resolution? resolution) => this with { Default = resolution };
}

public readonly record struct RunId(Guid Value);

/// <summary>
/// The only input the executor accepts. Exists only when nothing is open. Operations are in execution order: a move that
/// vacates a path before the move that fills it, a recycle before the operation it makes room for, PruneFolders last.
/// </summary>
public sealed class ApprovedPlan
{
	internal ApprovedPlan(RunId id, PlanKind kind, IReadOnlyList<Operation> operations, IReadOnlyList<PlanEntry> entries)
	{
		Id = id;
		Kind = kind;
		Operations = operations;
		Entries = entries;
	}

	public RunId Id { get; }
	public PlanKind Kind { get; }
	public IReadOnlyList<Operation> Operations { get; }
	public IReadOnlyList<PlanEntry> Entries { get; }
}
