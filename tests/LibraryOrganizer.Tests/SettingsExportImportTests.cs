using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class SettingsExportImportTests
{
	private sealed class TempFolder : IDisposable
	{
		public TempFolder()
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lo-export-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Path { get; }

		public string File(string name) => System.IO.Path.Combine(Path, name);

		public void Dispose() => Directory.Delete(Path, recursive: true);
	}

	private static string Fixture(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy", name);

	[Test]
	public async Task An_export_is_a_settings_file_the_store_can_load()
	{
		using var folder = new TempFolder();
		ProfileSettings full = SettingsStoreTests.FullyPopulated();
		ProfileSettings plain = new() { Name = "Plain" };

		SettingsStore.Export(folder.File("settings.xml"), [full, plain]);
		SettingsLoad load = new SettingsStore(folder.Path).Load();

		await Assert.That(load.Warnings.Count).IsEqualTo(0);
		await Assert.That(load.Settings.Profiles.Count).IsEqualTo(2);
		await Assert.That(load.Settings.Profiles[0]).IsEqualTo(full);
		await Assert.That(load.Settings.Profiles[1]).IsEqualTo(plain);
	}

	[Test]
	public async Task Importing_an_export_returns_the_same_profiles_under_new_ids()
	{
		using var folder = new TempFolder();
		ProfileSettings full = SettingsStoreTests.FullyPopulated();
		ProfileSettings plain = new() { Name = "Plain" };
		SettingsStore.Export(folder.File("backup.xml"), [full, plain]);

		ImportResult result = SettingsStore.ImportFile(folder.File("backup.xml"));

		await Assert.That(result.Warnings.Count).IsEqualTo(0);
		await Assert.That(result.Profiles.Count).IsEqualTo(2);
		await Assert.That(result.Profiles[0]).IsEqualTo(full with { Id = result.Profiles[0].Id });
		await Assert.That(result.Profiles[1]).IsEqualTo(plain with { Id = result.Profiles[1].Id });
		await Assert.That(result.Profiles[0].Id).IsNotEqualTo(full.Id);
		await Assert.That(result.Profiles[1].Id).IsNotEqualTo(plain.Id);
	}

	[Test]
	public async Task A_2x_file_imports_through_the_legacy_importer()
	{
		ImportResult result = SettingsStore.ImportFile(Fixture("default-2.1.dat"));

		await Assert.That(result.Profiles.Count).IsEqualTo(1);
		await Assert.That(result.Profiles[0].Name).IsEqualTo("Default");
		await Assert.That(result.Profiles[0].FolderTemplate).IsEqualTo(PathDefaults.FolderTemplate);
	}

	[Test]
	public async Task A_1x_file_imports_through_the_legacy_importer()
	{
		ImportResult result = SettingsStore.ImportFile(Fixture("v1.7-text-attribute.lop"));

		await Assert.That(result.Profiles.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task A_file_from_a_newer_schema_is_refused_with_a_warning()
	{
		using var folder = new TempFolder();
		File.WriteAllText(folder.File("future.xml"), "<LibraryOrganizer schema=\"99\"><Profiles/></LibraryOrganizer>");

		ImportResult result = SettingsStore.ImportFile(folder.File("future.xml"));

		await Assert.That(result.Profiles.Count).IsEqualTo(0);
		await Assert.That(result.Warnings.Any(w => w.Contains("99"))).IsTrue();
	}

	[Test]
	public async Task A_file_that_is_not_xml_gives_a_warning_and_no_profiles()
	{
		using var folder = new TempFolder();
		File.WriteAllText(folder.File("junk.dat"), "this is not xml");

		ImportResult result = SettingsStore.ImportFile(folder.File("junk.dat"));

		await Assert.That(result.Profiles.Count).IsEqualTo(0);
		await Assert.That(result.Warnings.Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_missing_file_gives_a_warning_and_no_profiles()
	{
		using var folder = new TempFolder();

		ImportResult result = SettingsStore.ImportFile(folder.File("nothing.xml"));

		await Assert.That(result.Profiles.Count).IsEqualTo(0);
		await Assert.That(result.Warnings.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Exporting_over_an_existing_file_replaces_it_and_leaves_no_temp_file()
	{
		using var folder = new TempFolder();
		string file = folder.File("export.xml");

		SettingsStore.Export(file, [new ProfileSettings { Name = "First" }]);
		SettingsStore.Export(file, [new ProfileSettings { Name = "Second" }]);

		await Assert.That(SettingsStore.ImportFile(file).Profiles.Single().Name).IsEqualTo("Second");
		await Assert.That(Directory.GetFiles(folder.Path).Length).IsEqualTo(1);
	}
}
