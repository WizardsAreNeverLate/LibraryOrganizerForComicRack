using System;
using System.Collections.Generic;

namespace LibraryOrganizer;

// ───────────────────────────── Entry point ─────────────────────────────

/// <summary>The planning module's public face. Pure: no UI, no writes. Disk is read through IFileProbe only.</summary>
public static class Organizer
{
	/// <summary>
	/// What the host must snapshot for these profiles: fields their templates, rules and required lists read,
	/// plus the fields series lookups need on other books of the same series.
	/// </summary>
	public static FieldDemand Demand(IReadOnlyList<Profile> runOrder) => throw new NotImplementedException();

	/// <summary>
	/// TODO, per book in selection order (see design.md "Planning rules"):
	///   verdicts = runOrder.Select(p => (p, file-exists check, p.Filter.Evaluate, required check, fileless policy))
	///   move: walk accepting Move profiles from last to first; first successful render wins; earlier -> Overridden(winner).
	///   copy: each accepting Copy profile renders -> CopyFile.
	///   target == source -> AlreadyInPlace; case-only -> RenameCase.
	///   NeedsAnswer -> entry waits; question added (deduplicated by key).
	///   counters: thread CounterState; keep the advanced state only for renders the plan uses.
	/// Then globally: ConflictFinder (collisions, occupied targets not vacated by the plan, too long), apply ResolutionBook,
	/// order operations (vacate-before-fill; a cycle goes through a temp name), predict PruneFolders.
	/// </summary>
	public static Plan Plan(OrganizeInput input) => throw new NotImplementedException();

	/// <summary>
	/// Undo as a plan. TODO: for each journal operation in reverse, if its end state holds on disk (IsSatisfied), emit its
	/// Inverse(); if the inverse is null (Recycle), add an entry "cannot be undone" so the review shows it.
	/// Journal status lines are ignored: the disk is the truth, so a crashed run undoes exactly what happened.
	/// </summary>
	public static Plan PlanUndo(RunJournal journal, IFileProbe disk) => throw new NotImplementedException();

	/// <summary>
	/// One profile, one book, for the configure form. Ignores other profiles and conflicts. Unanswered multi-value tokens
	/// render a placeholder instead of asking. Filter rejection still renders the path and says why it would be skipped.
	/// </summary>
	public static PreviewResult Preview(Profile profile, BookRecord book, SeriesIndex series, AnswerBook answers) => throw new NotImplementedException();
}

/// <param name="RunOrder">Profiles in the order the user chose. Order matters for "last move profile wins".</param>
/// <param name="Selection">The books to organize, in selection order (drives counters and conflict precedence).</param>
public sealed record OrganizeInput(
	IReadOnlyList<Profile> RunOrder,
	IReadOnlyList<BookRecord> Selection,
	SeriesIndex Series,
	AnswerBook Answers,
	IFileProbe Disk,
	int MaxPathLength = 259);

public sealed record PreviewResult(string? Path, string? Problem, FilterVerdictText? Filter, IReadOnlyList<Field> Unanswered);

public sealed record FilterVerdictText(bool Accepted, string Reason);

// ───────────────────────────── The plan value ─────────────────────────────

/// <summary>
/// The result of planning: what would happen to every book under every profile. A value: every change returns a new Plan
/// derived from scratch from the same input plus the new answer or resolution, so there is no partial state to keep in sync.
/// </summary>
public sealed class Plan
{
	private Plan() => throw new NotImplementedException();

	public PlanKind Kind { get; }

	/// <summary>One row per (book, profile) that the review grid shows, in selection order then run order.</summary>
	public IReadOnlyList<PlanEntry> Entries { get; }

	/// <summary>Distinct open multi-value questions, in the order they first block an entry.</summary>
	public IReadOnlyList<Question> Questions { get; }

	/// <summary>Every conflict, open or resolved. Open ones block Approve.</summary>
	public IReadOnlyList<Conflict> Conflicts { get; }

	/// <summary>The answers this plan used, including new ones. The host persists the remembered part after approval.</summary>
	public AnswerBook Answers { get; }

	public Plan WithAnswer(QuestionKey key, Answer answer) => throw new NotImplementedException();

	public Plan WithResolution(ConflictKey key, Resolution resolution) => throw new NotImplementedException();

	/// <summary>"Do this for all": applies to every conflict that has no explicit resolution and allows it.</summary>
	public Plan WithDefaultResolution(Resolution? resolution) => throw new NotImplementedException();

	/// <summary>An ApprovedPlan when no question and no conflict is open; otherwise null. Blockers lists what is open.</summary>
	public ApprovedPlan? Approve() => throw new NotImplementedException();

	public IReadOnlyList<string> Blockers => throw new NotImplementedException();

	/// <summary>Counts per profile and outcome, derived from Entries (2.x kept counters that double-counted).</summary>
	public IReadOnlyList<ProfileTally> Tally() => throw new NotImplementedException();
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
	public sealed record Skipped(string Reason) : EntryOutcome;
	public sealed record Overridden(string ByProfile) : EntryOutcome;
	/// <summary>Missing file, blank file name, required fields under Skip route, fileless without a cover, cannot undo.</summary>
	public sealed record Failed(string Reason) : EntryOutcome;
	/// <summary>Required field empty under the MoveUnder route: it is planned, and reported as failed.</summary>
	public sealed record PlannedAsFailed(IReadOnlyList<Operation> Operations, string Reason) : EntryOutcome;
	public sealed record WaitingForAnswer(QuestionKey Question) : EntryOutcome;
	public sealed record Blocked(ConflictKey Conflict, Operation Pending) : EntryOutcome;
}

public sealed record ProfileTally(string Profile, int Planned, int InPlace, int Skipped, int Failed, int Waiting);

// ───────────────────────────── Operations ─────────────────────────────

public readonly record struct OpId(int Value);

/// <summary>
/// A file-system change plus its library effect. Closed. Every operation can tell whether its end state already holds,
/// so the executor and undo are idempotent.
/// </summary>
public abstract record Operation
{
	private Operation() { }

	public abstract OpId Id { get; init; }

	/// <summary>The plan entry this operation belongs to; default for run-level operations (PruneFolders).</summary>
	public abstract EntryKey Entry { get; init; }

	/// <summary>File moves; library book's FilePath follows. Creates the target folder.</summary>
	public sealed record MoveFile(OpId Id, EntryKey Entry, AbsPath From, AbsPath To) : Operation;

	/// <summary>Case-only rename via a temp name (CE's ComicBook.RenameFile ignores case-only changes).</summary>
	public sealed record RenameCase(OpId Id, EntryKey Entry, AbsPath From, AbsPath To) : Operation;

	/// <summary>Copies; when AddToLibrary, the gateway adds a library entry with the source's metadata and a NEW id.</summary>
	public sealed record CopyFile(OpId Id, EntryKey Entry, AbsPath From, AbsPath To, bool AddToLibrary) : Operation;

	/// <summary>Writes a fileless book's custom cover. The library entry does not change.</summary>
	public sealed record WriteCover(OpId Id, EntryKey Entry, AbsPath To, CoverFormat Format) : Operation;

	/// <summary>
	/// Sends a file to the Recycle Bin. From a Replace resolution: ExistingBook is removed from the library and, when
	/// TransferReadProgressTo is set, its LastPageRead goes to the incoming book first. From undo of a copy: removes the copy.
	/// </summary>
	public sealed record RecycleFile(OpId Id, EntryKey Entry, AbsPath Path, BookId? ExistingBook, BookId? TransferReadProgressTo) : Operation;

	/// <summary>
	/// Last operation of a run. Removes each folder in Candidates that is empty at execution time, then its parents,
	/// never at or above StopAt (library roots and profile base folders), never one in Keep.
	/// </summary>
	public sealed record PruneFolders(OpId Id, IReadOnlyList<AbsPath> Candidates, IReadOnlyList<AbsPath> StopAt, IReadOnlyList<AbsPath> Keep, bool DeleteJunkFiles)
		: Operation
	{
		public override EntryKey Entry { get; init; }
	}

	/// <summary>The end state already holds: Move = source gone and target present; Copy = target present with source size; ...</summary>
	public bool IsSatisfied(IFileProbe disk) => throw new NotImplementedException();

	/// <summary>The undo of this operation, or null when it cannot be undone (RecycleFile, PruneFolders).</summary>
	public Operation? Inverse(OpId id, IReadOnlyDictionary<OpId, BookId> createdBooks) => throw new NotImplementedException();
}

// ───────────────────────────── Conflicts ─────────────────────────────

public readonly record struct ConflictKey(EntryKey Entry, AbsPath Target);

public abstract record ConflictKind
{
	private ConflictKind() { }

	/// <summary>A file the plan does not move away is at the target. ExistingBook is set when it is in the library (the review shows both covers).</summary>
	public sealed record TargetExists(BookId? ExistingBook) : ConflictKind;

	/// <summary>An earlier entry in selection order already targets this path.</summary>
	public sealed record SameTargetAs(EntryKey Earlier) : ConflictKind;

	public sealed record PathTooLong(int Length, int Max) : ConflictKind;
}

/// <param name="Allowed">The resolutions this kind accepts. Replace only for TargetExists.</param>
/// <param name="KeepBothPath">The rename candidate "name (n).ext": strips any " (digits)" suffix, n unbounded, free on disk and in the plan.</param>
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
	/// <summary>A path the user typed (the long-path editor). Re-checked by the next plan; it can conflict again.</summary>
	public sealed record UsePath(AbsPath Path) : Resolution;
}

// ───────────────────────────── Questions and answers ─────────────────────────────

public abstract record AnswerScope
{
	private AnswerScope() { }
	public sealed record Issue(BookId Book) : AnswerScope;
	public sealed record Series(SeriesKey Key) : AnswerScope;
}

public readonly record struct QuestionKey(Field Field, AnswerScope Scope);

/// <param name="Choices">Issue scope: the book's items. Series scope: distinct items across the series (SeriesIndex).</param>
/// <param name="Preselected">From the largest matching preset, in preset order.</param>
/// <param name="Waiting">How many entries this answer unblocks, for the dialog title.</param>
public sealed record Question(QuestionKey Key, string FieldName, IReadOnlyList<string> Choices, IReadOnlyList<string> Preselected, string ExampleCaption, int Waiting);

/// <param name="Selected">Chosen items in the user's order. Empty = write nothing.</param>
/// <param name="AsFolders">One folder level per item.</param>
/// <param name="EveryIssue">Series scope: write every selected item even on issues that lack it.</param>
/// <param name="SaveAsPreset">Issue scope: add a preset for this item set ("use for every issue that has these").</param>
/// <param name="PresetAskWhenMore">With SaveAsPreset: still ask when a book has items beyond the preset.</param>
public sealed record Answer(IReadOnlyList<string> Selected, bool AsFolders, bool EveryIssue, bool SaveAsPreset, bool PresetAskWhenMore);

/// <summary>A remembered item set for a field (2.x "always use").</summary>
public sealed record Preset(Field Field, IReadOnlyList<string> Items, bool AskWhenMore, bool AsFolders);

/// <summary>
/// Immutable answers: per issue (by BookId), per series (by SeriesKey), and presets per field. Persisted by AnswerStore,
/// so a series is not asked about again next run.
/// </summary>
public sealed class AnswerBook
{
	private AnswerBook() => throw new NotImplementedException();

	public static AnswerBook Empty => throw new NotImplementedException();

	public AnswerBook With(QuestionKey key, Answer answer) => throw new NotImplementedException();
	public AnswerBook Forget(QuestionKey key) => throw new NotImplementedException();
	public AnswerBook ForgetPreset(Preset preset) => throw new NotImplementedException();

	/// <summary>
	/// TODO: an issue answer is valid only while all its selected items are still on the book; otherwise it is ignored
	/// (and the question comes back) instead of silently writing items the book no longer has.
	/// </summary>
	internal Answer? Find(QuestionKey key, IReadOnlyList<string> currentItems) => throw new NotImplementedException();

	internal Preset? BestPreset(Field field, IReadOnlyList<string> items) => throw new NotImplementedException();

	public IReadOnlyList<(QuestionKey Key, Answer Answer)> Remembered => throw new NotImplementedException();
	public IReadOnlyList<Preset> Presets => throw new NotImplementedException();
}

/// <summary>answers.xml in the data folder. Single owner. Atomic save.</summary>
public sealed class AnswerStore
{
	public AnswerStore(string dataFolder) => throw new NotImplementedException();
	public AnswerBook Load() => throw new NotImplementedException();
	public void Save(AnswerBook answers) => throw new NotImplementedException();
}

// ───────────────────────────── Approved ─────────────────────────────

public readonly record struct RunId(Guid Value);

/// <summary>
/// The only input the executor accepts. Exists only when nothing is open. Operations are in execution order:
/// vacating moves before the moves that fill their old paths, Recycle before the Move it makes room for, PruneFolders last.
/// </summary>
public sealed class ApprovedPlan
{
	private ApprovedPlan() => throw new NotImplementedException();

	public RunId Id { get; }
	public PlanKind Kind { get; }
	public IReadOnlyList<Operation> Operations { get; }
	public IReadOnlyList<PlanEntry> Entries { get; }
}
