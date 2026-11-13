using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace LibraryOrganizer.Plugin;

// ComicRack makes one command per hook interface per class, so each menu entry is its own class. All share one Session.
// Every class answers Configure, so the settings open from whichever entry the user right-clicks.

/// <summary>Books menu: plan the selected books, review the plan, run it on approval.</summary>
public sealed class OrganizeCommand : DotNetPlugin, IBooks, IConfig
{
	public override string Key { get; set; } = "library-organizer-net";
	public override string Name { get; set; } = "Library Organizer";
	public override string Description { get; set; } = "Moves and renames the selected books into folders built from your templates and rules. Shows the plan before anything moves.";

	void IBooks.Execute(ComicBook[] books) => Session.Get(ComicRack, ScriptPath).Organize(books, quick: false);
	void IConfig.Execute() => Session.Get(ComicRack, ScriptPath).Configure(null);
}

/// <summary>Books menu: like Organize, but runs at once when the plan needs no decision and nothing failed.</summary>
public sealed class OrganizeQuickCommand : DotNetPlugin, IBooks, IConfig
{
	public override string Key { get; set; } = "library-organizer-net-quick";
	public override string Name { get; set; } = "Library Organizer (Quick)";
	public override string Description { get; set; } = "Organizes the selected books without the review step, unless a book needs a decision or fails.";

	void IBooks.Execute(ComicBook[] books) => Session.Get(ComicRack, ScriptPath).Organize(books, quick: true);
	void IConfig.Execute() => Session.Get(ComicRack, ScriptPath).Configure(null);
}

/// <summary>Automation menu: past runs, undo and finishing an interrupted run.</summary>
public sealed class HistoryCommand : DotNetPlugin, ILibrary, IConfig
{
	public override string Key { get; set; } = "library-organizer-net-history";
	public override string Name { get; set; } = "Library Organizer: History and Undo";
	public override string Description { get; set; } = "Lists past Library Organizer runs. Undo moves the books of a run back.";

	void ILibrary.Execute(ComicBook[] books) => Session.Get(ComicRack, ScriptPath).History();
	void IConfig.Execute() => Session.Get(ComicRack, ScriptPath).Configure(null);
}
