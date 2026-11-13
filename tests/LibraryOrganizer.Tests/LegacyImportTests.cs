using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LibraryOrganizer.Tests;

public class LegacyImportTests
{
	private static ImportResult Import(string fixture)
		=> LegacyImport.Read(XDocument.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy", fixture)));

	private static ImportResult ImportText(string xml) => LegacyImport.Read(XDocument.Parse(xml));

	private static bool HasWarning(ImportResult result, string fragment) => result.Warnings.Any(w => w.Contains(fragment));

	[Test]
	public async Task The_default_2x_file_imports_every_setting()
	{
		ImportResult result = Import("default-2.1.dat");

		await Assert.That(result.Warnings.Count).IsEqualTo(0);
		await Assert.That(result.Profiles.Count).IsEqualTo(1);
		ProfileSettings p = result.Profiles[0];
		await Assert.That(p.Name).IsEqualTo("Default");
		await Assert.That(p.FolderTemplate).IsEqualTo(@"{<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}");
		await Assert.That(p.FileTemplate).IsEqualTo("{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}");
		await Assert.That(p.Action).IsEqualTo(new ProfileAction.Move());
		await Assert.That(p.BaseFolder).IsEqualTo("");
		await Assert.That(p.UseFolder).IsTrue();
		await Assert.That(p.UseFileName).IsTrue();
		await Assert.That(p.CollapseSpaces).IsTrue();
		await Assert.That(p.DontAskWhenOneValue).IsTrue();
		await Assert.That(p.KeepReadProgressOnReplace).IsTrue();
		await Assert.That(p.AutoSpaceFields).IsTrue();
		await Assert.That(p.PruneEmptyFolders).IsTrue();
		await Assert.That(p.FilelessCovers).IsEqualTo(CoverFormat.Jpg);
		await Assert.That(p.RuleMode).IsEqualTo(RuleMode.Only);
		await Assert.That(p.Rules).IsEqualTo(new RuleSettings.Group(Combinator.Any, []));
		await Assert.That(p.RequiredFields.Count).IsEqualTo(0);
		await Assert.That(p.FailedBooksFolder).IsEqualTo("");
		await Assert.That(p.ExcludedFolders.Count).IsEqualTo(0);
		await Assert.That(p.EmptySubstitutes.Count).IsEqualTo(0);
		await Assert.That(p.IllegalCharacters.Count).IsEqualTo(9);
		await Assert.That(p.IllegalCharacters.First(pair => pair.Key == ":").Value).IsEqualTo(" - ");
		await Assert.That(string.Join("|", p.MonthNames)).IsEqualTo(string.Join("|", PathDefaults.MonthNames));
		await Assert.That(p.LastPrefix["number"]).IsEqualTo("#");
		await Assert.That(p.LastPrefix["startYear"]).IsEqualTo("(");
		await Assert.That(p.LastPrefix["Month Number"]).IsEqualTo("(");
		await Assert.That(p.LastPostfix["year"]).IsEqualTo(")");
		await Assert.That(p.LastSeparator["writer"]).IsEqualTo(", ");
		await Assert.That(p.LastSeparator["scanInformation"]).IsEqualTo(",");
		await Assert.That(result.LastUsed.Count).IsEqualTo(1);
		await Assert.That(result.LastUsed[0]).IsEqualTo(p.Id);
	}

	[Test]
	public async Task A_nested_rule_tree_imports_with_attributes_winning_over_elements()
	{
		ImportResult result = Import("nested-rules-2.1.dat");

		ProfileSettings p = result.Profiles[0];
		await Assert.That(p.RuleMode).IsEqualTo(RuleMode.Except);
		var expected = new RuleSettings.Group(
			Combinator.Any,
			[
				new RuleSettings.Condition("publisher", Operator.Is, "DC"),
				new RuleSettings.Group(
					Combinator.All,
					[
						new RuleSettings.Condition("format", Operator.Contains, "Annual"),
						new RuleSettings.Condition("year", Operator.LessThan, "1990"),
						new RuleSettings.Group(
							Combinator.Any,
							[
								new RuleSettings.Condition("manga", Operator.Is, "YesAndRightToLeft"),
								new RuleSettings.Condition("releasedDate", Operator.After, "2020-01-01"),
							]),
					]),
				new RuleSettings.Condition("year", Operator.Contains, "19"),
			]);
		await Assert.That(p.Rules).IsEqualTo(expected);
	}

	[Test]
	public async Task A_nested_file_merges_dictionaries_and_replaces_lists()
	{
		ProfileSettings p = Import("nested-rules-2.1.dat").Profiles[0];

		await Assert.That(p.BaseFolder).IsEqualTo(@"D:\Comics");
		await Assert.That(p.EmptySubstitutes["publisher"]).IsEqualTo("Unknown Publisher");
		await Assert.That(p.EmptySubstitutes["series"]).IsEqualTo("Untitled");
		await Assert.That(p.EmptySubstitutes.Count).IsEqualTo(2);
		await Assert.That(p.IllegalCharacters.Count).IsEqualTo(10);
		await Assert.That(p.IllegalCharacters[0].Key).IsEqualTo("?");
		await Assert.That(p.IllegalCharacters.First(pair => pair.Key == ":").Value).IsEqualTo("-");
		await Assert.That(p.IllegalCharacters.Last()).IsEqualTo(new System.Collections.Generic.KeyValuePair<string, string>("%", "pct"));
		await Assert.That(p.MonthNames[2]).IsEqualTo("Mar");
		await Assert.That(p.MonthNames[0]).IsEqualTo("January");
		await Assert.That(string.Join("|", p.ExcludedFolders)).IsEqualTo(@"D:\Comics\Unsorted|Unsorted");
		await Assert.That(string.Join("|", p.KeepFolders)).IsEqualTo(@"D:\Comics\Keep");
		await Assert.That(p.PruneEmptyFolders).IsFalse();
		await Assert.That(p.FilelessCovers).IsEqualTo(CoverFormat.Png);
		await Assert.That(p.LastPrefix["startYear"]).IsEqualTo("(");
	}

	[Test]
	public async Task A_nested_file_warns_about_what_it_drops_or_cannot_carry_over()
	{
		ImportResult result = Import("nested-rules-2.1.dat");

		await Assert.That(HasWarning(result, "Bogus Field")).IsTrue();
		await Assert.That(HasWarning(result, "Nonsense")).IsTrue();
		await Assert.That(HasWarning(result, "'Unsorted' is not a full path")).IsTrue();
		await Assert.That(HasWarning(result, "Year contains 19")).IsTrue();
		await Assert.That(result.Warnings.Count).IsEqualTo(5);
	}

	[Test]
	public async Task Duplicate_names_get_a_number_and_last_used_maps_names_to_ids()
	{
		ImportResult result = Import("nested-rules-2.1.dat");

		await Assert.That(string.Join("|", result.Profiles.Select(p => p.Name))).IsEqualTo("Default|Manga|Default (2)");
		await Assert.That(HasWarning(result, "Default (2)")).IsTrue();
		await Assert.That(string.Join("|", result.LastUsed.Select(id => id.Value))).IsEqualTo(string.Join("|", new[] { result.Profiles[1].Id.Value, result.Profiles[0].Id.Value }));
		await Assert.That(result.Profiles.Select(p => p.Id).Distinct().Count()).IsEqualTo(3);
	}

	[Test]
	public async Task Copy_mode_keeps_the_add_to_library_choice()
	{
		ProfileSettings manga = Import("nested-rules-2.1.dat").Profiles[1];

		await Assert.That(manga.Action).IsEqualTo(new ProfileAction.Copy(AddToLibrary: true));
		await Assert.That(manga.RuleMode).IsEqualTo(RuleMode.Only);
		await Assert.That(manga.Rules).IsEqualTo(new RuleSettings.Group(Combinator.All, [new RuleSettings.Condition("manga", Operator.IsNot, "No")]));
	}

	[Test]
	public async Task A_1x_settings_root_with_text_attributes_and_the_test_mode_imports()
	{
		ImportResult result = Import("v1.7-text-attribute.lop");

		await Assert.That(result.Profiles.Count).IsEqualTo(1);
		ProfileSettings p = result.Profiles[0];
		await Assert.That(p.Name).IsEqualTo("Old");
		await Assert.That(p.Action).IsEqualTo(new ProfileAction.Move());
		await Assert.That(HasWarning(result, "Simulate")).IsTrue();
		await Assert.That(p.EmptySubstitutes["series"]).IsEqualTo("No Series");
		await Assert.That(p.EmptySubstitutes["language"]).IsEqualTo("xx");
		await Assert.That(p.LastPrefix["startYear"]).IsEqualTo("(");
		await Assert.That(p.RuleMode).IsEqualTo(RuleMode.Only);
		await Assert.That(p.Rules).IsEqualTo(new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition("writer", Operator.Contains, "Moore")]));
		await Assert.That(result.LastUsed[0]).IsEqualTo(p.Id);
		await Assert.That(result.Warnings.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Simulate_becomes_move_with_a_warning_and_failed_fields_become_required_fields()
	{
		ImportResult result = Import("failed-fields.dat");

		ProfileSettings simulated = result.Profiles[0];
		await Assert.That(simulated.Action).IsEqualTo(new ProfileAction.Move());
		await Assert.That(string.Join(",", simulated.RequiredFields)).IsEqualTo("series,number");
		await Assert.That(simulated.FailedBooksFolder).IsEqualTo(@"D:\Failed");
		await Assert.That(HasWarning(result, "Profile 'Simulated': used Simulate")).IsTrue();
		await Assert.That(HasWarning(result, "Not A Field")).IsTrue();
	}

	[Test]
	public async Task Failed_fields_are_ignored_when_the_check_was_off_and_copy_can_skip_the_library()
	{
		ImportResult result = Import("failed-fields.dat");

		ProfileSettings disabled = result.Profiles[1];
		await Assert.That(disabled.RequiredFields.Count).IsEqualTo(0);
		await Assert.That(disabled.FailedBooksFolder).IsEqualTo("");
		await Assert.That(result.Profiles[2].Action).IsEqualTo(new ProfileAction.Copy(AddToLibrary: false));
		await Assert.That(result.LastUsed.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_list_element_with_no_items_keeps_the_default_and_a_profile_root_imports_alone()
	{
		ImportResult result = ImportText("<Profile Name=\"Solo\"><ExcludeFolders /><Months><Item Name=\"20\" Value=\"Never\" /></Months></Profile>");

		await Assert.That(result.Profiles.Count).IsEqualTo(1);
		await Assert.That(result.Profiles[0].ExcludedFolders.Count).IsEqualTo(0);
		await Assert.That(result.Profiles[0].MonthNames.Count).IsEqualTo(16);
		await Assert.That(HasWarning(result, "'20'")).IsTrue();
	}

	[Test]
	public async Task A_file_with_another_root_is_reported_and_imports_nothing()
	{
		ImportResult result = ImportText("<Nope />");

		await Assert.That(result.Profiles.Count).IsEqualTo(0);
		await Assert.That(result.Warnings.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Imported_profiles_survive_the_settings_store()
	{
		string folder = Path.Combine(Path.GetTempPath(), "lo-tests-" + Guid.NewGuid().ToString("N"));
		try
		{
			ImportResult result = Import("nested-rules-2.1.dat");
			var store = new SettingsStore(folder);

			store.Save(new Settings(result.Profiles, result.LastUsed, RunPreferences.Default));
			SettingsLoad load = store.Load();

			await Assert.That(load.Warnings.Count).IsEqualTo(0);
			for (int i = 0; i < result.Profiles.Count; i++)
			{
				await Assert.That(load.Settings.Profiles[i]).IsEqualTo(result.Profiles[i]);
			}
		}
		finally
		{
			if (Directory.Exists(folder))
			{
				Directory.Delete(folder, recursive: true);
			}
		}
	}

	[Test]
	public async Task The_installed_2x_file_is_found_in_the_scripts_folder_or_a_subfolder()
	{
		string scripts = Path.Combine(Path.GetTempPath(), "lo-tests-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(Path.Combine(scripts, "Library Organizer"));
			await Assert.That(LegacyImport.FindInstalled2x(scripts)).IsNull();
			await Assert.That(LegacyImport.FindInstalled2x(Path.Combine(scripts, "missing"))).IsNull();

			string settings = Path.Combine(scripts, "Library Organizer", "losettingsx.dat");
			File.WriteAllText(settings, "<Profiles />");

			await Assert.That(LegacyImport.FindInstalled2x(scripts)).IsEqualTo(settings);
		}
		finally
		{
			if (Directory.Exists(scripts))
			{
				Directory.Delete(scripts, recursive: true);
			}
		}
	}
}
