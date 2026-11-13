using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using LibraryOrganizer.Plugin.Forms;

namespace LibraryOrganizer.Plugin;

/// <summary>
/// Process-wide state shared by every command class: the stores and the multi-value answers given during this ComicRack
/// session. Created on the first hook call, never in a constructor (ComicRack builds disabled plugins too). UI thread only.
/// </summary>
internal sealed partial class Session
{
	private static Session? instance;

	private readonly IPluginEnvironment env;
	private readonly string scriptPath;
	private bool busy;

	private Session(IPluginEnvironment env, string scriptPath)
	{
		this.env = env;
		this.scriptPath = scriptPath;
		DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LibraryOrganizer.NET");
		Directory.CreateDirectory(DataFolder);
		Settings = new SettingsStore(DataFolder);
		Journals = new JournalStore(DataFolder, keepRuns: 20);
	}

	public static Session Get(IPluginEnvironment env, string scriptPath) => instance ??= new Session(env, scriptPath);

	public string DataFolder { get; }
	public SettingsStore Settings { get; }
	public JournalStore Journals { get; }
	public AnswerBook Answers { get; private set; } = AnswerBook.Empty;

	private IWin32Window Owner => env.MainWindow;

	/// <summary>The settings dialog. Previews render on the first of <paramref name="books"/>, or on a library book.</summary>
	public void Configure(IReadOnlyList<ComicBook>? books) => Guarded(() =>
	{
		Settings settings = LoadSettings();
		IReadOnlyList<ComicBook> library = [.. env.App.GetLibraryBooks()];
		ComicBook? sample = books?.FirstOrDefault() ?? library.FirstOrDefault(b => b.IsLinked);
		LibrarySnapshot? preview = sample is null ? null : LibrarySnapshot.Take([sample], library);
		Settings? edited = ConfigForm.Show(Owner, settings, profile => PreviewText(profile, preview), CustomKeys(library));
		if (edited is not null)
		{
			Settings.Save(edited);
		}
	});

	private string PreviewText(ProfileSettings settings, LibrarySnapshot? snapshot)
	{
		if (snapshot?.Selection.FirstOrDefault() is not BookRecord book)
		{
			return "(no book to preview)";
		}
		CompileResult compiled = settings.Compile();
		if (compiled.Profile is null)
		{
			return compiled.Errors.FirstOrDefault()?.Message ?? "(this profile has errors)";
		}
		PreviewResult result = Organizer.Preview(compiled.Profile, book, snapshot.Series, Answers);
		string text = result.Path ?? result.Problem ?? "";
		return result.Filter.Accepted ? text : $"{text}   (skipped: {result.Filter.Reason})";
	}

	private static IReadOnlyList<string> CustomKeys(IEnumerable<ComicBook> library)
		=> [.. library.SelectMany(b => b.GetCustomValues()).Select(pair => pair.Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];

	/// <summary>
	/// Loads settings. The first time, when no settings file exists yet and a 2.x install is found, offers to import it.
	/// </summary>
	private Settings LoadSettings()
	{
		bool firstRun = !File.Exists(Settings.Path);
		SettingsLoad load = Settings.Load();
		ShowWarnings("Library Organizer settings", load.Warnings);
		if (!firstRun || Legacy2xFile() is not string legacy)
		{
			return load.Settings;
		}
		if (MessageBox.Show(Owner, $"Library Organizer 2.x settings were found:\n{legacy}\n\nImport its profiles?", "Library Organizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Settings.Save(load.Settings);
			return load.Settings;
		}
		ImportResult imported = SettingsStore.ImportFile(legacy);
		ShowWarnings("Imported from Library Organizer 2.x", imported.Warnings);
		Settings settings = imported.Profiles.Count == 0
			? load.Settings
			: load.Settings with { Profiles = imported.Profiles, LastRunOrder = imported.LastUsed };
		Settings.Save(settings);
		return settings;
	}

	/// <summary>The Python plugin lives in a sibling folder of this plugin's folder under ComicRack's Scripts folder.</summary>
	private string? Legacy2xFile()
		=> Directory.GetParent(scriptPath) is DirectoryInfo scripts ? LegacyImport.FindInstalled2x(scripts.FullName) : null;

	private void ShowWarnings(string title, IReadOnlyList<string> warnings)
	{
		if (warnings.Count > 0)
		{
			MessageBox.Show(Owner, string.Join(Environment.NewLine, warnings), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	/// <summary>Errors are shown, never passed to ComicRack. A second command while one is open does nothing.</summary>
	private void Guarded(Action action)
	{
		if (busy)
		{
			return;
		}
		busy = true;
		try
		{
			action();
		}
		catch (Exception error)
		{
			MessageBox.Show(Owner, error.ToString(), "Library Organizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			busy = false;
		}
	}
}
