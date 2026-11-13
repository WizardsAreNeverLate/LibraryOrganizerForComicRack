using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibraryOrganizer.Plugin.Forms;

namespace LibraryOrganizer.Plugin.Tests;

public class ConfigFormModelTests
{
	private static string Token(Field field, TokenInputs? inputs = null) => TokenBuilder.Build(field, inputs ?? new TokenInputs());

	[Test]
	public async Task A_plain_field_gets_its_automatic_space_inside_the_braces()
	{
		await Assert.That(Token(Fields.Series)).IsEqualTo("{ <series>}");
	}

	[Test]
	public async Task Prefix_and_postfix_wrap_the_field_and_the_space_comes_first()
	{
		string spaced = Token(Fields.Series, new TokenInputs { Prefix = "Vol.", Postfix = ")" });
		string tight = Token(Fields.Series, new TokenInputs { AutoSpace = false, Prefix = "(", Postfix = ")" });

		await Assert.That(spaced).IsEqualTo("{ Vol.<series>)}");
		await Assert.That(tight).IsEqualTo("{(<series>)}");
	}

	[Test]
	public async Task A_number_pads_to_a_fixed_width_or_to_the_last_issue()
	{
		await Assert.That(Token(Fields.Number, new TokenInputs { Pad = PadSpec.Fixed(3) })).IsEqualTo("{ <number3>}");
		await Assert.That(Token(Fields.Volume, new TokenInputs { Pad = PadSpec.Auto })).IsEqualTo("{ <volume0>}");
		await Assert.That(Token(Fields.Count)).IsEqualTo("{ <count>}");
	}

	[Test]
	public async Task A_month_is_a_name_unless_the_number_is_asked_for()
	{
		await Assert.That(Token(Fields.Month)).IsEqualTo("{ <month>}");
		await Assert.That(Token(Fields.StartMonth, new TokenInputs { MonthAsNumber = true, Pad = PadSpec.Fixed(2) })).IsEqualTo("{ <startmonth#2>}");
		await Assert.That(Token(Fields.Month, new TokenInputs { MonthAsNumber = true })).IsEqualTo("{ <month#>}");
	}

	[Test]
	public async Task A_multi_value_field_takes_a_separator_and_a_scope()
	{
		await Assert.That(Token(Fields.Writer)).IsEqualTo("{ <writer(, )(issue)>}");
		await Assert.That(Token(Fields.Writer, new TokenInputs { Separator = " & ", Scope = AnswerScopeKind.Series })).IsEqualTo("{ <writer( & )(series)>}");
	}

	[Test]
	public async Task A_yes_no_field_writes_text_for_yes_or_for_no()
	{
		await Assert.That(Token(Fields.Manga, new TokenInputs { YesNoText = "Manga" })).IsEqualTo("{ <manga(Manga)>}");
		await Assert.That(Token(Fields.Manga, new TokenInputs { YesNoText = "Western", InvertYesNo = true })).IsEqualTo("{ <manga(Western)(!)>}");
	}

	[Test]
	public async Task Read_percentage_takes_text_operator_and_percent_or_stands_alone()
	{
		await Assert.That(Token(Fields.ReadPercentage, new TokenInputs { ReadText = "Read", ReadOperator = '>', ReadPercent = 90 })).IsEqualTo("{ <read(Read)(>)(90)>}");
		await Assert.That(Token(Fields.ReadPercentage)).IsEqualTo("{ <read>}");
	}

	[Test]
	public async Task Counter_custom_first_letter_and_date_build_their_arguments()
	{
		await Assert.That(Token(Fields.Counter, new TokenInputs { CounterStart = 5, CounterStep = 2, CounterPad = 3 })).IsEqualTo("{ <counter(5)(2)(3)>}");
		await Assert.That(Token(Fields.Custom, new TokenInputs { CustomKey = "Source" })).IsEqualTo("{ <Custom(Source)>}");
		await Assert.That(Token(Fields.FirstLetter)).IsEqualTo("{ <first(Series)>}");
		await Assert.That(Token(Fields.ReleasedDate, new TokenInputs { DateFormat = "yyyy-MM" })).IsEqualTo("{ <ReleasedDate(yyyy-MM)>}");
		await Assert.That(Token(Fields.ReleasedDate)).IsEqualTo("{ <ReleasedDate>}");
	}

	[Test]
	public async Task Every_token_the_picker_offers_parses_as_a_template()
	{
		var inputs = new TokenInputs { CustomKey = "Source", YesNoText = "Yes!", ReadText = "Read" };
		string[] rejected =
		[
			.. Fields.All
				.Where(f => f.Uses.HasFlag(FieldUses.Template) && f.TemplateName is not null)
				.Select(f => (Field: f, Text: TokenBuilder.Build(f, inputs)))
				.Where(t => !Template.Parse(t.Text, TemplateTarget.FileName).Ok)
				.Select(t => t.Text),
		];

		await Assert.That(rejected).IsEmpty();
	}

	[Test]
	public async Task Every_date_format_the_picker_offers_parses()
	{
		string[] rejected = [.. TokenBuilder.DateFormats.Select(format => Token(Fields.AddedDate, new TokenInputs { DateFormat = format })).Where(text => !Template.Parse(text, TemplateTarget.FileName).Ok)];

		await Assert.That(rejected).IsEmpty();
	}

	private static ProfileSettings Valid() => new() { Name = "P", BaseFolder = @"D:\Comics" };

	private static IReadOnlyList<ProfileProblem> Problems(ProfileSettings profile) => ProfileValidator.Validate(profile);

	[Test]
	public async Task A_default_profile_with_a_base_folder_has_no_problems()
	{
		await Assert.That(Problems(Valid())).IsEmpty();
	}

	[Test]
	public async Task An_empty_base_folder_is_a_problem_only_when_folders_are_used()
	{
		await Assert.That(Problems(Valid() with { BaseFolder = "" }).Select(p => p.Tab)).IsEquivalentTo([ConfigTab.General]);
		await Assert.That(Problems(Valid() with { BaseFolder = "", UseFolder = false })).IsEmpty();
	}

	[Test]
	public async Task A_relative_base_folder_is_a_problem()
	{
		await Assert.That(Problems(Valid() with { BaseFolder = @"Comics\Sorted" }).Count).IsEqualTo(1);
	}

	[Test]
	public async Task Turning_both_uses_off_is_a_problem()
	{
		IReadOnlyList<ProfileProblem> problems = Problems(Valid() with { UseFolder = false, UseFileName = false });

		await Assert.That(problems.Count).IsEqualTo(1);
		await Assert.That(problems[0].Tab).IsEqualTo(ConfigTab.General);
	}

	[Test]
	public async Task Template_errors_are_reported_with_their_position()
	{
		IReadOnlyList<ProfileProblem> problems = Problems(Valid() with { FolderTemplate = "{<seriez>}", FileTemplate = "{<series>" });

		await Assert.That(problems.Count).IsEqualTo(2);
		await Assert.That(problems.All(p => p.Tab == ConfigTab.Templates)).IsTrue();
		await Assert.That(problems[0].Message).Contains("character 2");
	}

	[Test]
	public async Task A_template_that_is_switched_off_is_not_checked()
	{
		await Assert.That(Problems(Valid() with { UseFileName = false, FileTemplate = "{<nope>}" })).IsEmpty();
	}

	[Test]
	public async Task A_blank_file_template_is_a_problem_while_file_names_are_used()
	{
		await Assert.That(Problems(Valid() with { FileTemplate = "  " }).Count).IsEqualTo(1);
	}

	[Test]
	public async Task Invalid_rules_are_all_reported_including_nested_ones()
	{
		var rules = new RuleSettings.Group(
			Combinator.All,
			[
				new RuleSettings.Condition("year", Operator.GreaterThan, "twenty"),
				new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition("nonesuch", Operator.Is, "x"), new RuleSettings.Condition("publisher", Operator.Is, "DC")]),
			]);

		IReadOnlyList<ProfileProblem> problems = Problems(Valid() with { Rules = rules });

		await Assert.That(problems.Count).IsEqualTo(2);
		await Assert.That(problems.All(p => p.Tab == ConfigTab.Rules)).IsTrue();
	}

	[Test]
	public async Task An_illegal_character_must_be_one_character_and_listed_once()
	{
		ProfileSettings profile = Valid() with { IllegalCharacters = [new("ab", ""), new("?", ""), new("?", "x")] };

		await Assert.That(Problems(profile).Count).IsEqualTo(2);
	}

	[Test]
	public async Task A_yes_no_rule_accepts_the_stored_right_to_left_spelling_for_manga_only()
	{
		await Assert.That(ProfileValidator.ConditionError(new RuleSettings.Condition("manga", Operator.Is, "YesAndRightToLeft"))).IsNull();
		await Assert.That(ProfileValidator.ConditionError(new RuleSettings.Condition("blackAndWhite", Operator.Is, "YesAndRightToLeft"))).IsNotNull();
	}
}
