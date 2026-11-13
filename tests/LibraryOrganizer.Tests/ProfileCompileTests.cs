using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static LibraryOrganizer.Tests.MemoryFileSystem;

namespace LibraryOrganizer.Tests;

public class ProfileCompileTests
{
	private static readonly ProfileSettings Valid = new() { Name = "Main", BaseFolder = @"D:\Comics" };

	private static string Where(CompileResult result) => string.Join(",", result.Errors.Select(error => error.Where));

	[Test]
	public async Task Valid_settings_compile_to_a_profile_with_both_templates()
	{
		CompileResult result = Valid.Compile();

		await Assert.That(result.Errors.Count).IsEqualTo(0);
		var destination = (Destination.FolderAndName)result.Profile!.Destination;
		await Assert.That(destination.BaseFolder).IsEqualTo(P(@"D:\Comics"));
		await Assert.That(destination.Folder.Source).IsEqualTo(PathDefaults.FolderTemplate);
		await Assert.That(destination.FileName.Source).IsEqualTo(PathDefaults.FileTemplate);
		await Assert.That(result.Profile.Id).IsEqualTo(Valid.Id);
		await Assert.That(result.Profile.Name).IsEqualTo("Main");
	}

	[Test]
	public async Task The_use_flags_choose_the_destination()
	{
		Profile folderOnly = (Valid with { UseFileName = false }).Compile().Profile!;
		Profile nameOnly = (Valid with { UseFolder = false, BaseFolder = "" }).Compile().Profile!;

		await Assert.That(folderOnly.Destination).IsTypeOf<Destination.FolderOnly>();
		await Assert.That(nameOnly.Destination).IsTypeOf<Destination.NameOnly>();
	}

	[Test]
	public async Task Both_use_flags_off_is_an_error()
	{
		CompileResult result = (Valid with { UseFolder = false, UseFileName = false }).Compile();

		await Assert.That(result.Profile).IsNull();
		await Assert.That(Where(result)).IsEqualTo("UseFolder");
	}

	[Test]
	[Arguments("")]
	[Arguments("Comics")]
	public async Task A_folder_profile_needs_a_full_base_folder(string baseFolder)
	{
		CompileResult result = (Valid with { BaseFolder = baseFolder }).Compile();

		await Assert.That(result.Profile).IsNull();
		await Assert.That(Where(result)).IsEqualTo("BaseFolder");
	}

	[Test]
	public async Task A_template_error_carries_the_span_the_parser_reported()
	{
		const string folder = @"{<publisher>}\{<nosuch>}";
		const string file = "{<series>}{ #<number>";

		CompileResult result = (Valid with { FolderTemplate = folder, FileTemplate = file }).Compile();

		await Assert.That(result.Profile).IsNull();
		SettingsError folderError = result.Errors.Single(error => error.Where == "FolderTemplate");
		SettingsError fileError = result.Errors.Single(error => error.Where == "FileTemplate");
		await Assert.That(folderError.Span).IsEqualTo(Template.Parse(folder, TemplateTarget.Folder).Errors.Single());
		await Assert.That(folder.Substring(folderError.Span!.Value.Start, folderError.Span.Value.Length)).Contains("nosuch");
		await Assert.That(fileError.Span).IsEqualTo(Template.Parse(file, TemplateTarget.FileName).Errors.Single());
		await Assert.That(fileError.Message).IsEqualTo(fileError.Span!.Value.Message);
	}

	[Test]
	public async Task A_template_the_profile_does_not_use_is_not_checked()
	{
		CompileResult result = (Valid with { UseFolder = false, FolderTemplate = "{<nosuch>}", BaseFolder = "not a path" }).Compile();

		await Assert.That(result.Errors.Count).IsEqualTo(0);
		await Assert.That(result.Profile).IsNotNull();
	}

	[Test]
	public async Task A_rule_that_does_not_compile_is_an_error()
	{
		var rules = new RuleSettings.Group(Combinator.All,
		[
			new RuleSettings.Condition("year", Operator.GreaterThan, "soon"),
			new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition("no-such-field", Operator.Is, "x")]),
			new RuleSettings.Condition("publisher", Operator.GreaterThan, "DC"),
		]);

		CompileResult result = (Valid with { Rules = rules }).Compile();

		await Assert.That(result.Profile).IsNull();
		await Assert.That(Where(result)).IsEqualTo("Rules,Rules,Rules");
	}

	[Test]
	public async Task Rules_and_excluded_folders_compile_into_the_filter()
	{
		var rules = new RuleSettings.Group(Combinator.All, [new RuleSettings.Condition("publisher", Operator.Is, "DC")]);

		Profile profile = (Valid with { Rules = rules, RuleMode = RuleMode.Only, ExcludedFolders = [@"D:\Comics\Unsorted\", " "] }).Compile().Profile!;

		await Assert.That(profile.Filter.Mode).IsEqualTo(RuleMode.Only);
		await Assert.That(profile.Filter.Root.Combinator).IsEqualTo(Combinator.All);
		await Assert.That(((Rule.Condition)profile.Filter.Root.Children.Single()).Describe()).IsEqualTo("Publisher is DC");
		await Assert.That(profile.Filter.ExcludedFolders.Folders.Single()).IsEqualTo(P(@"D:\Comics\Unsorted"));
	}

	[Test]
	public async Task Folder_lists_reject_a_path_that_is_not_full()
	{
		CompileResult result = (Valid with { ExcludedFolders = ["Unsorted"], KeepFolders = ["Keep"], FailedBooksFolder = "Failed" }).Compile();

		await Assert.That(result.Profile).IsNull();
		await Assert.That(Where(result)).IsEqualTo("ExcludedFolders,FailedBooksFolder,KeepFolders");
	}

	[Test]
	public async Task Required_fields_reject_an_unknown_key_and_a_field_that_cannot_be_required()
	{
		CompileResult result = (Valid with { RequiredFields = ["series", "no-such-field", "filePath"] }).Compile();

		await Assert.That(result.Profile).IsNull();
		await Assert.That(Where(result)).IsEqualTo("RequiredFields,RequiredFields");
	}

	[Test]
	public async Task The_failed_folder_decides_the_route_of_required_fields()
	{
		Profile none = Valid.Compile().Profile!;
		Profile skip = (Valid with { RequiredFields = ["series", "year"] }).Compile().Profile!;
		Profile moveUnder = (Valid with { RequiredFields = ["series"], FailedBooksFolder = @"D:\Failed" }).Compile().Profile!;

		await Assert.That(none.Required).IsNull();
		await Assert.That(string.Join(",", skip.Required!.Fields)).IsEqualTo("series,year");
		await Assert.That(skip.Required.Route).IsEqualTo(new RequiredFields.FailRoute.Skip());
		await Assert.That(moveUnder.Required!.Route).IsEqualTo(new RequiredFields.FailRoute.MoveUnder(P(@"D:\Failed")));
	}

	[Test]
	public async Task Text_settings_map_to_the_path_style()
	{
		Profile profile = (Valid with
		{
			EmptyFolderName = "Unknown",
			CollapseSpaces = false,
			EmptySubstitutes = new Dictionary<string, string> { ["publisher"] = "No Publisher", ["no-such-field"] = "x" },
			IllegalCharacters = [new(":", "-")],
			MonthNames = ["Jan"],
		}).Compile().Profile!;

		await Assert.That(profile.Style.EmptyFolderName).IsEqualTo("Unknown");
		await Assert.That(profile.Style.CollapseSpaces).IsFalse();
		await Assert.That(profile.Style.EmptySubstitutes.Count).IsEqualTo(1);
		await Assert.That(profile.Style.EmptySubstitutes[Fields.Publisher]).IsEqualTo("No Publisher");
		await Assert.That(profile.Style.IllegalCharacters.Single().Value).IsEqualTo("-");
		await Assert.That(profile.Style.MonthNames.Single()).IsEqualTo("Jan");
	}

	[Test]
	public async Task The_remaining_settings_carry_over()
	{
		Profile changed = (Valid with
		{
			Action = new ProfileAction.Copy(AddToLibrary: true),
			FilelessCovers = CoverFormat.Bmp,
			PruneEmptyFolders = true,
			KeepFolders = [@"D:\Comics\Keep"],
			DontAskWhenOneValue = false,
			KeepReadProgressOnReplace = false,
		}).Compile().Profile!;
		Profile noPruning = (Valid with { PruneEmptyFolders = false, KeepFolders = [@"D:\Comics\Keep"] }).Compile().Profile!;

		await Assert.That(changed.Action).IsEqualTo(new ProfileAction.Copy(AddToLibrary: true));
		await Assert.That(changed.FilelessCovers).IsEqualTo(CoverFormat.Bmp);
		await Assert.That(changed.Pruning!.Keep.Single()).IsEqualTo(P(@"D:\Comics\Keep"));
		await Assert.That(changed.DontAskWhenOneValue).IsFalse();
		await Assert.That(changed.KeepReadProgressOnReplace).IsFalse();
		await Assert.That(noPruning.Pruning).IsNull();
	}
}
