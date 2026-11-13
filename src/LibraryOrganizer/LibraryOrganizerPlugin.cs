using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace LibraryOrganizer.Plugin;

public sealed class LibraryOrganizerPlugin : DotNetPlugin, IBooks, IConfig
{
	public override string Key { get; set; } = "library-organizer-net";

	public override string Name { get; set; } = "Library Organizer";

	void IBooks.Execute(ComicBook[] books)
	{
	}

	void IConfig.Execute()
	{
	}
}
