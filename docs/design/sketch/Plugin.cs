// Plugin project (LibraryOrganizer.Plugin): references ComicRack.Engine, ComicRack.Plugins, WinForms and LibraryOrganizer.Core.
// Thin shell: snapshot ComicBooks, show forms, call Core, apply library effects. No organizing logic here.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using LibraryOrganizer;

namespace LibraryOrganizer.Plugin;

// ───────────────────────────── Commands ─────────────────────────────
// ComicRack makes one instance per class and calls hooks on the UI thread. One class per menu entry; all share one Session.

/// <summary>Book list > Automation > "Library Organizer". Plans, shows the review grid, runs on approval. Also the Configure action.</summary>
public sealed class OrganizeCommand : DotNetPlugin, IBooks, IConfig
{
	public override string Key { get; set; } = "library-organizer-net";
	public override string Name { get; set; } = "Library Organizer";
	public override string Image { get; set; } = "libraryorganizer.png";

	void IBooks.Execute(ComicBook[] books) => Session.Get(ComicRack, ScriptPath).Organize(books, RunStyle.Review);
	void IConfig.Execute() => Session.Get(ComicRack, ScriptPath).Configure(previewBooks: null);
}

/// <summary>"Library Organizer (now)": executes at once unless RunPreferences.ReviewWhen says the plan needs a look.</summary>
public sealed class OrganizeNowCommand : DotNetPlugin, IBooks
{
	public override string Key { get; set; } = "library-organizer-net-now";
	void IBooks.Execute(ComicBook[] books) => Session.Get(ComicRack, ScriptPath).Organize(books, RunStyle.Quick);
}

/// <summary>File > Automation > "Library Organizer history": past runs, undo, finish an interrupted run.</summary>
public sealed class HistoryCommand : DotNetPlugin, ILibrary
{
	public override string Key { get; set; } = "library-organizer-net-history";
	void ILibrary.Execute(ComicBook[] books) => Session.Get(ComicRack, ScriptPath).History();
}

/// <summary>Organizes the whole library at start-up with Quick style. Disabled by default, as 2.x.</summary>
public sealed class StartupCommand : DotNetPlugin, IStartup
{
	public override string Key { get; set; } = "library-organizer-net-startup";
	public override bool Enabled { get; set; } = false;
	void IStartup.Execute() => Session.Get(ComicRack, ScriptPath).OrganizeLibraryAtStartup();
}

public enum RunStyle { Review, Quick }

// ───────────────────────────── Session ─────────────────────────────

/// <summary>
/// Process-wide services, created on the first hook call (never in a constructor; ComicRack builds disabled plugins too).
/// Owns the three stores so no two commands write one file. UI thread only, so its "a run is open" flag needs no lock.
/// Data folder: %APPDATA%\LibraryOrganizer.NET (not ScriptPath: a reinstall or a read-only install folder must not lose settings).
/// </summary>
internal sealed class Session
{
	private static Session? instance;

	public static Session Get(IPluginEnvironment env, string scriptPath) => instance ??= new Session(env, scriptPath);

	private Session(IPluginEnvironment env, string scriptPath) => throw new NotImplementedException();

	public SettingsStore Settings { get; }
	public AnswerStore Answers { get; }
	public JournalStore Journals { get; }

	/// <summary>
	/// TODO:
	///   if running: return (a second hook while the modal run window is up)
	///   offer Interrupted() runs first (finish / undo / ignore)
	///   load settings; first run with no settings.xml and a 2.x losettingsx.dat nearby -> offer LegacyImport
	///   compile profiles; any error -> open Configure on the failing profile and stop
	///   order = ProfilePicker (only when more than one profile; saves LastRunOrder as ids)
	///   snapshot = library.Snapshot(books, Organizer.Demand(order))                 // UI thread
	///   RunWindow.Run(owner, async (ui, ct) => {
	///     plan = await Task.Run(() => Organizer.Plan(new OrganizeInput(order, snapshot.Selection, snapshot.Series, Answers.Load(), disk)))
	///     plan = plan.WithDefaultResolution(prefs.DefaultResolution)
	///     plan = await ui.Ask(plan)            // MultiValueDialog per question; re-plan on the pool after each answer
	///     if style == Review or prefs.ReviewWhen says so: plan = await ui.Review(plan)   // null = closed = nothing happens
	///     approved = plan.Approve() ?? return
	///     Answers.Save(plan.Answers)
	///     report = await new Executor(disk, snapshot.Gateway, Journals).RunAsync(approved, ui.Progress, ct)
	///     ui.ShowReport(report); Journals.Trim()
	///   })
	/// </summary>
	public void Organize(ComicBook[] books, RunStyle style) => throw new NotImplementedException();

	/// <summary>Profiles editor with live preview over previewBooks (or a sample of the library). Preview never opens a dialog.</summary>
	public void Configure(ComicBook[]? previewBooks) => throw new NotImplementedException();

	/// <summary>Lists RunSummary rows; Undo builds Organizer.PlanUndo and goes through the same review and executor.</summary>
	public void History() => throw new NotImplementedException();

	public void OrganizeLibraryAtStartup() => throw new NotImplementedException();
}

// ───────────────────────────── ComicRack adapter ─────────────────────────────

/// <summary>A snapshot of the selection and its series, plus the live books it came from for the gateway.</summary>
internal sealed record LibrarySnapshot(IReadOnlyList<BookRecord> Selection, SeriesIndex Series, ILibraryGateway Gateway);

/// <summary>
/// The only code that reads or writes ComicBook. Snapshot and every gateway call run on the UI thread.
/// </summary>
internal sealed class ComicRackLibrary
{
	public ComicRackLibrary(IPluginEnvironment env) => throw new NotImplementedException();

	/// <summary>
	/// TODO:
	///   readers = HostReaders for demand.SelectionFields  (compiled once per Field from Field.HostProperty + Field.Kind:
	///             int -1 -> Unset, "" -> Unset, YesNo/MangaYesNo.Unknown -> Unset, DateTime.MinValue -> Unset, CSV -> Items)
	///   selection = books.Select(b => Record(b, readers))
	///   if demand needs series: key the whole library by SeriesKey (3 reads per book), keep members of selected series,
	///             snapshot them with demand.SeriesMemberFields, SeriesIndex.Build
	///   gateway = new Gateway(byId: selection + series members + lazy App.GetLibraryBooks() index for undo)
	/// </summary>
	public LibrarySnapshot Snapshot(ComicBook[] books, FieldDemand demand) => throw new NotImplementedException();

	/// <summary>For undo in a later session: resolves BookIds against the current library.</summary>
	public ILibraryGateway GatewayForLibrary() => throw new NotImplementedException();

	/// <summary>
	/// Binds Field.HostProperty to a ComicBook getter by reflection, once. A plugin test asserts every registry field with a
	/// HostProperty binds, so the registry stays the one field list.
	/// </summary>
	internal static Func<ComicBook, FieldValue> HostReader(Field field) => throw new NotImplementedException();
}

/// <summary>
/// ILibraryGateway over live ComicBooks.
/// FileMoved: book.FilePath = path (fires ComicRack's change events on the UI thread, where they belong).
/// AddCopy: App.AddNewBook(false), then copy metadata with SetInfo plus rating, custom values and catalog fields;
///          NOT ComicBook.CopyFrom, which copies Id and FilePath too.
/// Remove: App.RemoveBook. Cover: App.GetComicThumbnail(book, 0) encoded to the format.
/// </summary>
internal sealed class ComicRackGateway : ILibraryGateway
{
	public void FileMoved(BookId book, AbsPath newPath) => throw new NotImplementedException();
	public BookId AddCopy(BookId source, AbsPath copyPath) => throw new NotImplementedException();
	public void Remove(BookId book) => throw new NotImplementedException();
	public void TransferReadProgress(BookId from, BookId to) => throw new NotImplementedException();
	public byte[]? Cover(BookId book, CoverFormat format) => throw new NotImplementedException();
}

// ───────────────────────────── Run window ─────────────────────────────

/// <summary>What the async run body can ask the window to do. Implemented by RunWindow (WinForms, code-only, themed).</summary>
internal interface IRunUi
{
	/// <summary>Asks open questions one at a time; after each answer re-plans on the pool. Cancel = null (abandon the run).</summary>
	Task<Plan?> Ask(Plan plan);

	/// <summary>
	/// The review grid: one row per entry (book, profile, action, from, to, outcome), filter by outcome, a conflict column
	/// with a per-row resolution and a "for all" default, covers side by side for TargetExists. Run = return the plan.
	/// </summary>
	Task<Plan?> Review(Plan plan);

	IProgress<RunProgress> Progress { get; }
	void ShowReport(RunReport report);
}

/// <summary>
/// Modal window that hosts an async body on the UI SynchronizationContext (the sibling plugin's ScrapeHost pattern):
/// ShowDialog(owner), start the body from Shown, close when it ends, closing early cancels and waits for the body.
/// </summary>
internal static class RunWindow
{
	public static void Run(IWin32Window owner, Func<IRunUi, CancellationToken, Task> body) => throw new NotImplementedException();
}
