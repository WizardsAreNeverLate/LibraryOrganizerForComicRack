using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryOrganizer;

// ───────────────────────────── Ports the host implements ─────────────────────────────

/// <summary>Read-only disk view. The planner and undo planner read through this; tests pass an in-memory tree.</summary>
public interface IFileProbe
{
	bool FileExists(AbsPath path);
	bool FolderExists(AbsPath path);
	/// <summary>Exact on-disk spelling of the last segment, for case-only renames. Null when missing.</summary>
	string? ActualName(AbsPath path);
	long? FileSize(AbsPath path);
	/// <summary>Direct children (files and folders), for prune prediction.</summary>
	IReadOnlyList<AbsPath> Children(AbsPath folder);
}

/// <summary>Disk writes. Only the executor calls these, always off the UI thread.</summary>
public interface IFileSystem : IFileProbe
{
	void CreateFolder(AbsPath folder);
	/// <summary>Same-volume rename or cross-volume copy-then-delete. Never overwrites.</summary>
	void Move(AbsPath from, AbsPath to);
	/// <summary>Never overwrites. Writes to a temp name in the target folder, then renames into place.</summary>
	void Copy(AbsPath from, AbsPath to);
	void WriteNew(AbsPath to, byte[] content);
	void Recycle(AbsPath file);
	void DeleteEmptyFolder(AbsPath folder);
}

/// <summary>
/// The library effects of operations. The plugin implements it over ComicBook; every call happens on the thread that awaits
/// RunAsync, which in the plugin is the UI thread. Tests use an in-memory map.
/// </summary>
public interface ILibraryGateway
{
	void FileMoved(BookId book, AbsPath newPath);
	/// <summary>Adds a library entry for a copy: AddNewBook(false), copy metadata (not Id, not FilePath), set FilePath. Returns its id.</summary>
	BookId AddCopy(BookId source, AbsPath copyPath);
	void Remove(BookId book);
	void TransferReadProgress(BookId from, BookId to);
	/// <summary>The custom cover encoded in the format, read on the calling (UI) thread. Null when the book has none.</summary>
	byte[]? Cover(BookId book, CoverFormat format);
}

// ───────────────────────────── Executor ─────────────────────────────

public sealed record RunProgress(int Done, int Total, string Current);

public abstract record OpResult
{
	private OpResult() { }
	public sealed record Done(OpId Op) : OpResult;
	/// <summary>The end state already held (resume after a crash, or a re-run).</summary>
	public sealed record AlreadyDone(OpId Op) : OpResult;
	/// <summary>The disk changed after planning (target appeared, source vanished) or the OS refused. Nothing was overwritten.</summary>
	public sealed record Failed(OpId Op, string Reason) : OpResult;
	public sealed record NotRun(OpId Op) : OpResult;
}

/// <summary>The plan's entries plus the result of each operation. The review grid shows it in place of the plan; Save writes TSV.</summary>
public sealed record RunReport(RunId Run, IReadOnlyList<PlanEntry> Entries, IReadOnlyDictionary<OpId, OpResult> Results, bool Cancelled)
{
	public IReadOnlyList<ProfileTally> Tally() => throw new NotImplementedException();
}

/// <summary>
/// Runs an approved plan. The only code that changes files or the library.
/// </summary>
public sealed class Executor
{
	public Executor(IFileSystem disk, ILibraryGateway library, JournalStore journals) => throw new NotImplementedException();

	/// <summary>
	/// TODO:
	///   journal = journals.Begin(plan)                      // writes header + every operation, flushed, before any change
	///   foreach op in plan.Operations:
	///     if ct cancelled: mark rest NotRun; break           // cancel between operations, never mid-operation
	///     if op.IsSatisfied(disk): journal.Done(op); AlreadyDone; continue
	///     pre = gather library-side inputs here (Cover bytes) // on the awaiting thread = UI thread in the plugin
	///     try { await Task.Run(() => Apply(op, pre)) }       // file work on the pool; ConfigureAwait(true)
	///     catch IOException/UnauthorizedAccess: journal.Failed(op, reason); continue
	///     apply library effect via gateway                    // back on the awaiting thread
	///     journal.Done(op, createdBook?)                      // CopyFile records the new BookId for undo
	///     progress.Report(...)
	///   journal.Close(cancelled)
	/// Crash safety: a crash between the file op and Done leaves "begun" in the journal; resume finds it IsSatisfied,
	/// and undo derives from disk state. A crash after the file op but before the library effect: resume re-applies the
	/// effect for AlreadyDone moves whose library path is stale (gateway.FileMoved is idempotent).
	/// </summary>
	public Task<RunReport> RunAsync(ApprovedPlan plan, IProgress<RunProgress> progress, CancellationToken ct) => throw new NotImplementedException();
}

// ───────────────────────────── Journal ─────────────────────────────

public sealed record RunSummary(RunId Id, DateTime Started, PlanKind Kind, int Operations, int Done, int Failed, bool Finished, IReadOnlyList<string> Profiles);

/// <summary>A parsed journal: the approved operations in order and what is known about each.</summary>
public sealed record RunJournal(RunSummary Summary, IReadOnlyList<Operation> Operations, IReadOnlyDictionary<OpId, OpResult> Results, IReadOnlyDictionary<OpId, BookId> CreatedBooks);

/// <summary>
/// The runs folder. One file per run: runs\yyyyMMdd-HHmmss-{runId}.journal. Line-oriented: each line is one self-contained
/// XML element (&lt;run/&gt;, &lt;op/&gt;, &lt;done/&gt;, &lt;failed/&gt;, &lt;end/&gt;), appended and flushed with WriteThrough.
/// A torn last line (crash mid-write) is ignored on read. No other process or command writes a journal: one writer per file.
/// </summary>
public sealed class JournalStore
{
	public JournalStore(string dataFolder, int keepRuns) => throw new NotImplementedException();

	public IReadOnlyList<RunSummary> List() => throw new NotImplementedException();

	public RunJournal Read(RunId run) => throw new NotImplementedException();

	/// <summary>Runs that have no &lt;end/&gt; line: offered as "finish" or "undo" the next time a command starts.</summary>
	public IReadOnlyList<RunSummary> Interrupted() => throw new NotImplementedException();

	internal JournalWriter Begin(ApprovedPlan plan) => throw new NotImplementedException();

	/// <summary>Deletes the oldest finished journals beyond keepRuns. Never deletes an interrupted one.</summary>
	public void Trim() => throw new NotImplementedException();
}

internal sealed class JournalWriter : IDisposable
{
	public void Done(OpId op, BookId? createdBook = null) => throw new NotImplementedException();
	public void Failed(OpId op, string reason) => throw new NotImplementedException();
	public void Close(bool cancelled) => throw new NotImplementedException();
	public void Dispose() => throw new NotImplementedException();
}

/// <summary>Real disk. Lives in Core because it needs only System.IO plus Microsoft.VisualBasic.FileIO for the Recycle Bin (both in net48).</summary>
public sealed class WindowsFileSystem : IFileSystem
{
	public bool FileExists(AbsPath path) => throw new NotImplementedException();
	public bool FolderExists(AbsPath path) => throw new NotImplementedException();
	public string? ActualName(AbsPath path) => throw new NotImplementedException();
	public long? FileSize(AbsPath path) => throw new NotImplementedException();
	public IReadOnlyList<AbsPath> Children(AbsPath folder) => throw new NotImplementedException();
	public void CreateFolder(AbsPath folder) => throw new NotImplementedException();
	public void Move(AbsPath from, AbsPath to) => throw new NotImplementedException();
	public void Copy(AbsPath from, AbsPath to) => throw new NotImplementedException();
	public void WriteNew(AbsPath to, byte[] content) => throw new NotImplementedException();
	public void Recycle(AbsPath file) => throw new NotImplementedException();
	public void DeleteEmptyFolder(AbsPath folder) => throw new NotImplementedException();
}
