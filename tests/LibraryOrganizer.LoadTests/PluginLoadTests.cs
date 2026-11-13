using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Automation;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace LibraryOrganizer.LoadTests;

public class PluginLoadTests
{
	[Test]
	public async Task ComicRack_registers_the_commands_of_the_installed_crplugin()
	{
		string scripts = Path.Combine(Path.GetTempPath(), "lo-load-" + Guid.NewGuid().ToString("N"));
		ZipFile.ExtractToDirectory(CrPlugin(), Path.Combine(scripts, "Library Organizer"));
		try
		{
			var engine = new PluginEngine();
			engine.Initialize(new StubEnvironment(), scripts);

			string[] hooks = [.. engine.GetAllCommands().Where(c => c.Key == "library-organizer-net").Select(c => c.Hook).OrderBy(h => h)];

			await Assert.That(string.Join(",", hooks)).IsEqualTo(string.Join(",", ExpectedHooks.OrderBy(h => h)));
		}
		finally
		{
			try { Directory.Delete(scripts, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
		}
	}

	private static readonly string[] ExpectedHooks = ["Books"];

	private static string CrPlugin()
	{
		string configuration = AppDomain.CurrentDomain.BaseDirectory.Contains(@"\Release\") ? "Release" : "Debug";
		string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
		string path = Path.Combine(root, $@"src\LibraryOrganizer\bin\{configuration}\net48\LibraryOrganizer.crplugin");
		return File.Exists(path) ? path : throw new FileNotFoundException("Build the plugin first", path);
	}

	private sealed class StubEnvironment : IPluginEnvironment
	{
		public IWin32Window MainWindow => null!;
		public IApplication App => null!;
		public IOpenBooksManager OpenBooks => null!;
		public IBrowser Browser => null!;
		public IComicDisplay ComicDisplay => null!;
		public string CommandPath { get; set; } = "";
		public IThemePlugin Theme { get; set; } = null!;
		public IEnumerable<string> LibraryPaths => [];
		public string Localize(string resourceKey, string elementKey, string text) => text;
		public object Clone() => MemberwiseClone();
	}
}
