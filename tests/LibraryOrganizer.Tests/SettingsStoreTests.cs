using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class SettingsStoreTests
{
	private sealed class TempFolder : IDisposable
	{
		public TempFolder() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lo-tests-" + Guid.NewGuid().ToString("N"));

		public string Path { get; }

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}

	internal static ProfileSettings FullyPopulated(string name = "Full") => new()
	{
		Id = new ProfileId(Guid.NewGuid()),
		Name = name,
		Action = new ProfileAction.Copy(AddToLibrary: false),
		FolderTemplate = "{<publisher>}\\{<series}{ (<startyear>}",
		FileTemplate = "{<series>}\t{ #<number9z>}\r\nline & <b> \"q\" 'x'  trailing ",
		UseFolder = false,
		UseFileName = false,
		BaseFolder = @"D:\Comics\Base",
		EmptyFolderName = "Unknown",
		EmptySubstitutes = new Dictionary<string, string> { ["publisher"] = "Unknown Publisher", ["series"] = " spaced " },
		CollapseSpaces = false,
		IllegalCharacters = [new("?", "Q"), new(":", " - "), new("%", "")],
		MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", "Spr", "Sum", "Fal", "Win"],
		ExcludedFolders = [@"D:\Comics\Unsorted", "not a path"],
		Rules = new RuleSettings.Group(
			Combinator.All,
			[
				new RuleSettings.Condition("publisher", Operator.Is, "DC"),
				new RuleSettings.Group(
					Combinator.Any,
					[
						new RuleSettings.Condition("year", Operator.LessThan, "not a number"),
						new RuleSettings.Condition("futureField", Operator.MatchesRegex, "(unclosed"),
						new RuleSettings.Group(Combinator.Any, []),
					]),
				new RuleSettings.Condition("manga", Operator.Is, "Yes (Right to Left)"),
			]),
		RuleMode = RuleMode.Only,
		RequiredFields = ["series", "number"],
		FailedBooksFolder = @"D:\Failed",
		FilelessCovers = CoverFormat.Bmp,
		PruneEmptyFolders = false,
		KeepFolders = [@"D:\Comics\Keep"],
		DontAskWhenOneValue = false,
		KeepReadProgressOnReplace = false,
		AutoSpaceFields = false,
		LastPrefix = new Dictionary<string, string> { ["number"] = "#", ["month"] = "(" },
		LastPostfix = new Dictionary<string, string> { ["year"] = ")" },
		LastSeparator = new Dictionary<string, string> { ["writer"] = ", " },
	};

	[Test]
	public async Task A_missing_file_gives_one_default_profile()
	{
		using var folder = new TempFolder();

		SettingsLoad load = new SettingsStore(folder.Path).Load();

		await Assert.That(load.Warnings.Count).IsEqualTo(0);
		await Assert.That(load.Settings.Profiles.Count).IsEqualTo(1);
		await Assert.That(load.Settings.Profiles[0].Name).IsEqualTo("Default");
		await Assert.That(load.Settings.Profiles[0].FolderTemplate).IsEqualTo(PathDefaults.FolderTemplate);
		await Assert.That(load.Settings.Profiles[0].FileTemplate).IsEqualTo(PathDefaults.FileTemplate);
	}

	[Test]
	public async Task A_fully_populated_profile_survives_save_and_load()
	{
		using var folder = new TempFolder();
		ProfileSettings full = FullyPopulated();
		ProfileSettings plain = new() { Name = "Plain" };
		var settings = new Settings(
			[full, plain],
			[plain.Id, full.Id],
			new RunPreferences(ConflictDefault.KeepBoth, ReviewWhen.OnlyWhenNeeded, 7));
		var store = new SettingsStore(folder.Path);

		store.Save(settings);
		SettingsLoad load = new SettingsStore(folder.Path).Load();

		await Assert.That(load.Warnings.Count).IsEqualTo(0);
		await Assert.That(load.Settings.Profiles.Count).IsEqualTo(2);
		await Assert.That(load.Settings.Profiles[0]).IsEqualTo(full);
		await Assert.That(load.Settings.Profiles[1]).IsEqualTo(plain);
		await Assert.That(string.Join(",", load.Settings.LastRunOrder.Select(id => id.Value))).IsEqualTo(string.Join(",", new[] { plain.Id.Value, full.Id.Value }));
		await Assert.That(load.Settings.Preferences).IsEqualTo(new RunPreferences(ConflictDefault.KeepBoth, ReviewWhen.OnlyWhenNeeded, 7));
	}

	[Test]
	public async Task A_profile_with_no_fileless_format_and_a_move_action_survives()
	{
		using var folder = new TempFolder();
		ProfileSettings profile = new() { Name = "Moves", FilelessCovers = null };
		var store = new SettingsStore(folder.Path);

		store.Save(new Settings([profile], [], RunPreferences.Default));

		await Assert.That(store.Load().Settings.Profiles[0]).IsEqualTo(profile);
	}

	[Test]
	public async Task Rule_fields_are_stored_by_field_key()
	{
		using var folder = new TempFolder();
		ProfileSettings profile = new()
		{
			Name = "Keys",
			Rules = new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition(Fields.Publisher.Key, Operator.Is, "DC")]),
		};
		var store = new SettingsStore(folder.Path);

		store.Save(new Settings([profile], [], RunPreferences.Default));
		string xml = File.ReadAllText(store.Path);

		await Assert.That(xml).Contains("<LibraryOrganizer schema=\"1\">");
		await Assert.That(xml).Contains("field=\"publisher\"");
	}

	[Test]
	public async Task Saving_over_an_existing_file_replaces_it_and_leaves_no_temp_file()
	{
		using var folder = new TempFolder();
		var store = new SettingsStore(folder.Path);
		store.Save(new Settings([new ProfileSettings { Name = "First" }], [], RunPreferences.Default));

		store.Save(new Settings([new ProfileSettings { Name = "Second" }], [], RunPreferences.Default));

		await Assert.That(store.Load().Settings.Profiles[0].Name).IsEqualTo("Second");
		await Assert.That(string.Join(",", Directory.GetFiles(folder.Path).Select(Path.GetFileName))).IsEqualTo("settings.xml");
	}

	[Test]
	[Arguments("<LibraryOrganizer schema=\"1\"><Profiles>")]
	[Arguments("<Other schema=\"1\" />")]
	[Arguments("<LibraryOrganizer />")]
	[Arguments("<LibraryOrganizer schema=\"99\" />")]
	[Arguments("not xml at all")]
	public async Task An_unreadable_file_is_kept_aside_and_never_overwritten(string content)
	{
		using var folder = new TempFolder();
		Directory.CreateDirectory(folder.Path);
		var store = new SettingsStore(folder.Path);
		File.WriteAllText(store.Path, content);

		SettingsLoad load = store.Load();

		string[] aside = Directory.GetFiles(folder.Path, "settings.xml.corrupt-*");
		await Assert.That(aside.Length).IsEqualTo(1);
		await Assert.That(File.ReadAllText(aside[0])).IsEqualTo(content);
		await Assert.That(File.Exists(store.Path)).IsFalse();
		await Assert.That(load.Warnings.Count).IsEqualTo(1);
		await Assert.That(load.Warnings[0]).Contains(Path.GetFileName(aside[0]));
		await Assert.That(load.Settings.Profiles[0].Name).IsEqualTo("Default");

		store.Save(load.Settings);

		await Assert.That(File.ReadAllText(aside[0])).IsEqualTo(content);
		await Assert.That(store.Load().Warnings.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_bad_value_inside_a_readable_file_warns_and_uses_the_default()
	{
		using var folder = new TempFolder();
		Directory.CreateDirectory(folder.Path);
		var store = new SettingsStore(folder.Path);
		File.WriteAllText(
			store.Path,
			"<LibraryOrganizer schema=\"1\"><Profiles><Profile id=\"" + Guid.NewGuid() + "\"><Name>P</Name><UseFolder>maybe</UseFolder>"
			+ "<Rules mode=\"Only\" combinator=\"Any\"><Condition field=\"year\" op=\"Nope\" value=\"1\" /><Condition field=\"year\" op=\"Is\" value=\"2\" /></Rules>"
			+ "</Profile></Profiles></LibraryOrganizer>");

		SettingsLoad load = store.Load();

		ProfileSettings profile = load.Settings.Profiles[0];
		await Assert.That(profile.UseFolder).IsTrue();
		await Assert.That(profile.Rules.Children.Count).IsEqualTo(1);
		await Assert.That(load.Warnings.Count).IsEqualTo(2);
		await Assert.That(File.Exists(store.Path)).IsTrue();
	}

	[Test]
	public async Task Equality_sees_a_change_to_every_setting()
	{
		ProfileSettings baseline = FullyPopulated();
		var changes = new Dictionary<string, Func<ProfileSettings, ProfileSettings>>
		{
			[nameof(ProfileSettings.Id)] = p => p with { Id = new ProfileId(Guid.NewGuid()) },
			[nameof(ProfileSettings.Name)] = p => p with { Name = p.Name + "x" },
			[nameof(ProfileSettings.Action)] = p => p with { Action = new ProfileAction.Move() },
			[nameof(ProfileSettings.FolderTemplate)] = p => p with { FolderTemplate = p.FolderTemplate + "x" },
			[nameof(ProfileSettings.FileTemplate)] = p => p with { FileTemplate = p.FileTemplate + "x" },
			[nameof(ProfileSettings.UseFolder)] = p => p with { UseFolder = !p.UseFolder },
			[nameof(ProfileSettings.UseFileName)] = p => p with { UseFileName = !p.UseFileName },
			[nameof(ProfileSettings.BaseFolder)] = p => p with { BaseFolder = p.BaseFolder + "x" },
			[nameof(ProfileSettings.EmptyFolderName)] = p => p with { EmptyFolderName = p.EmptyFolderName + "x" },
			[nameof(ProfileSettings.EmptySubstitutes)] = p => p with { EmptySubstitutes = new Dictionary<string, string>() },
			[nameof(ProfileSettings.CollapseSpaces)] = p => p with { CollapseSpaces = !p.CollapseSpaces },
			[nameof(ProfileSettings.IllegalCharacters)] = p => p with { IllegalCharacters = p.IllegalCharacters.Reverse().ToList() },
			[nameof(ProfileSettings.MonthNames)] = p => p with { MonthNames = p.MonthNames.Reverse().ToList() },
			[nameof(ProfileSettings.ExcludedFolders)] = p => p with { ExcludedFolders = [] },
			[nameof(ProfileSettings.Rules)] = p => p with { Rules = RuleSettings.Empty },
			[nameof(ProfileSettings.RuleMode)] = p => p with { RuleMode = RuleMode.Except },
			[nameof(ProfileSettings.RequiredFields)] = p => p with { RequiredFields = [] },
			[nameof(ProfileSettings.FailedBooksFolder)] = p => p with { FailedBooksFolder = "" },
			[nameof(ProfileSettings.FilelessCovers)] = p => p with { FilelessCovers = null },
			[nameof(ProfileSettings.PruneEmptyFolders)] = p => p with { PruneEmptyFolders = !p.PruneEmptyFolders },
			[nameof(ProfileSettings.KeepFolders)] = p => p with { KeepFolders = [] },
			[nameof(ProfileSettings.DontAskWhenOneValue)] = p => p with { DontAskWhenOneValue = !p.DontAskWhenOneValue },
			[nameof(ProfileSettings.KeepReadProgressOnReplace)] = p => p with { KeepReadProgressOnReplace = !p.KeepReadProgressOnReplace },
			[nameof(ProfileSettings.AutoSpaceFields)] = p => p with { AutoSpaceFields = !p.AutoSpaceFields },
			[nameof(ProfileSettings.LastPrefix)] = p => p with { LastPrefix = new Dictionary<string, string>() },
			[nameof(ProfileSettings.LastPostfix)] = p => p with { LastPostfix = new Dictionary<string, string>() },
			[nameof(ProfileSettings.LastSeparator)] = p => p with { LastSeparator = new Dictionary<string, string>() },
		};

		string[] properties = typeof(ProfileSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.Name != "EqualityContract")
			.Select(p => p.Name)
			.ToArray();
		await Assert.That(string.Join(",", properties.OrderBy(name => name))).IsEqualTo(string.Join(",", changes.Keys.OrderBy(name => name)));
		foreach (KeyValuePair<string, Func<ProfileSettings, ProfileSettings>> change in changes)
		{
			await Assert.That(change.Value(baseline).Equals(baseline)).IsFalse();
		}
	}
}
