using System.Linq;
using System.Threading.Tasks;
using static LibraryOrganizer.Tests.TemplateFixture;

namespace LibraryOrganizer.Tests;

public class MultiValueTests
{
	private const string WriterPerIssue = "{<writer(, )(issue)>}";
	private const string WriterPerSeries = "{<writer( & )(series)>}";

	private static readonly RenderContext AlwaysAsk = DefaultContext with { DontAskWhenOneValue = false };

	private static RenderContext With(RenderContext context, QuestionKey key, Answer answer) => context with { Answers = context.Answers.With(key, answer) };

	private static Question OnlyQuestion(RenderOutcome outcome) => ((RenderOutcome.NeedsAnswers)outcome).Questions.Single();

	[Test]
	public async Task Vector_issue_scope_asks_then_joins_the_chosen_values()
	{
		BookRecord book = XMen7();

		Question question = OnlyQuestion(Render(WriterPerIssue, TemplateTarget.FileName, book, AlwaysAsk));

		await Assert.That(question.Key).IsEqualTo(IssueKey(Fields.Writer, book));
		await Assert.That(question.FieldName).IsEqualTo("Writer");
		await Assert.That(string.Join("|", question.Choices)).IsEqualTo("Chris Claremont|Jim Lee");
		await Assert.That(question.Preselected.Count).IsEqualTo(0);
		await Assert.That(question.ExampleCaption).IsEqualTo(book.Caption);
		await Assert.That(FileName(WriterPerIssue, book, With(AlwaysAsk, question.Key, Pick("Chris Claremont", "Jim Lee"))))
			.IsEqualTo("Chris Claremont, Jim Lee");
	}

	[Test]
	public async Task An_issue_answer_is_written_in_the_users_order()
	{
		BookRecord book = XMen7();

		await Assert.That(FileName(WriterPerIssue, book, With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Jim Lee", "Chris Claremont"))))
			.IsEqualTo("Jim Lee, Chris Claremont");
		await Assert.That(FileName(WriterPerIssue, book, With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Jim Lee")))).IsEqualTo("Jim Lee");
	}

	[Test]
	public async Task An_empty_selection_writes_nothing_and_hides_the_group()
	{
		BookRecord book = XMen7();

		await Assert.That(FileName("X{ by <writer(, )(issue)>}", book, With(DefaultContext, IssueKey(Fields.Writer, book), Pick()))).IsEqualTo("X");
	}

	[Test]
	public async Task An_issue_answer_for_another_book_does_not_apply()
	{
		BookRecord book = XMen7();
		BookRecord other = XMen7();

		await Assert.That(FileName(WriterPerIssue, book, With(DefaultContext, IssueKey(Fields.Writer, other), Pick("Jim Lee")))).IsEqualTo("ASKS: writer");
	}

	[Test]
	public async Task An_issue_answer_naming_an_item_the_book_no_longer_has_is_asked_again()
	{
		BookRecord book = XMen7();

		await Assert.That(FileName(WriterPerIssue, book, With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Jim Lee", "Stan Lee")))).IsEqualTo("ASKS: writer");
	}

	[Test]
	public async Task One_value_is_written_without_asking_only_when_the_option_is_on()
	{
		BookRecord book = XMen7((Fields.Writer, "Chris Claremont"));

		await Assert.That(FileName(WriterPerIssue, book, DefaultContext)).IsEqualTo("Chris Claremont");
		await Assert.That(FileName(WriterPerIssue, book, AlwaysAsk)).IsEqualTo("ASKS: writer");
	}

	[Test]
	public async Task A_blank_field_writes_nothing_without_asking()
	{
		await Assert.That(FileName("X{ by <writer(, )(issue)>}", XMen7((Fields.Writer, null)), AlwaysAsk)).IsEqualTo("X");
	}

	[Test]
	public async Task Every_unanswered_question_in_a_template_is_returned_once()
	{
		BookRecord book = XMen7((Fields.Characters, "Cyclops, Storm"), (Fields.Teams, "X-Men, X-Factor"));
		const string template = "{<writer(, )(issue)>}{ <characters(, )(series)>}{ ({<teams(, )(issue)>} <year>)}{ <writer(+)(issue)>}";

		await Assert.That(FileName(template, book)).IsEqualTo("ASKS: writer, characters, teams");
		await Assert.That(FileName(template, book, With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Jim Lee")))).IsEqualTo("ASKS: characters, teams");
	}

	[Test]
	public async Task A_group_that_is_hidden_does_not_ask_for_its_children()
	{
		await Assert.That(FileName("X{ ({<writer(, )(issue)>} <format>)}", XMen7())).IsEqualTo("X");
	}

	[Test]
	public async Task A_preview_writes_a_placeholder_instead_of_asking()
	{
		await Assert.That(FileName("{<series>}{ <writer(, )(issue)>}", XMen7(), DefaultContext with { Preview = true })).IsEqualTo("X-Men ‹writer?›");
		await Assert.That(Folder(@"{<series>}\{<writer(, )(series)>}", XMen7(), DefaultContext with { Preview = true })).IsEqualTo(@"X-Men\‹writer?›");
	}

	[Test]
	public async Task Series_scope_offers_every_distinct_item_in_the_series()
	{
		BookRecord book = TestBooks.Book(number: "1", writer: "Chris Claremont, Jim Lee");
		SeriesIndex series = SeriesIndex.Build([book, TestBooks.Book(number: "2", writer: "jim lee, Scott Lobdell"), TestBooks.Book(series: "Other", writer: "Stan Lee")]);

		Question question = OnlyQuestion(Render(WriterPerSeries, TemplateTarget.FileName, book, DefaultContext with { Series = series }));

		await Assert.That(question.Key).IsEqualTo(SeriesKey(Fields.Writer, book));
		await Assert.That(string.Join("|", question.Choices)).IsEqualTo("Chris Claremont|Jim Lee|Scott Lobdell");
	}

	[Test]
	public async Task A_series_answer_writes_the_selected_items_each_issue_has()
	{
		BookRecord one = TestBooks.Book(number: "1", writer: "Chris Claremont, Jim Lee");
		BookRecord two = TestBooks.Book(number: "2", writer: "Jim Lee, Scott Lobdell");
		BookRecord three = TestBooks.Book(number: "3");
		RenderContext context = With(
			DefaultContext with { Series = SeriesIndex.Build([one, two, three]) }, SeriesKey(Fields.Writer, one), Pick("Scott Lobdell", "Chris Claremont", "Jim Lee"));

		await Assert.That(FileName(WriterPerSeries, one, context)).IsEqualTo("Chris Claremont & Jim Lee");
		await Assert.That(FileName(WriterPerSeries, two, context)).IsEqualTo("Scott Lobdell & Jim Lee");
		await Assert.That(FileName("X{ <writer( & )(series)>}", three, context)).IsEqualTo("X");
	}

	[Test]
	public async Task A_series_answer_for_every_issue_writes_all_selected_items()
	{
		BookRecord one = TestBooks.Book(number: "1", writer: "Chris Claremont, Jim Lee");
		BookRecord three = TestBooks.Book(number: "3");
		RenderContext context = With(
			DefaultContext with { Series = SeriesIndex.Build([one, three]) }, SeriesKey(Fields.Writer, one), Pick("Jim Lee", "Chris Claremont") with { EveryIssue = true });

		await Assert.That(FileName(WriterPerSeries, three, context)).IsEqualTo("Jim Lee & Chris Claremont");
	}

	[Test]
	public async Task Series_scope_with_one_distinct_item_follows_the_one_value_option()
	{
		BookRecord one = TestBooks.Book(number: "1", writer: "Jim Lee");
		BookRecord two = TestBooks.Book(number: "2");
		SeriesIndex series = SeriesIndex.Build([one, two]);

		await Assert.That(FileName(WriterPerSeries, one, DefaultContext with { Series = series })).IsEqualTo("Jim Lee");
		await Assert.That(FileName("X{ <writer( & )(series)>}", two, DefaultContext with { Series = series })).IsEqualTo("X");
		await Assert.That(FileName(WriterPerSeries, one, AlwaysAsk with { Series = series })).IsEqualTo("ASKS: writer");
	}

	[Test]
	public async Task A_preset_that_does_not_ask_is_used_for_any_book_that_has_all_its_items()
	{
		BookRecord saved = XMen7();
		BookRecord next = XMen7((Fields.Writer, "Jim Lee, Chris Claremont, Scott Lobdell"));
		BookRecord without = XMen7((Fields.Writer, "Jim Lee, Scott Lobdell"));
		RenderContext context = With(DefaultContext, IssueKey(Fields.Writer, saved), Pick("Chris Claremont") with { SaveAsPreset = true });

		await Assert.That(FileName(WriterPerIssue, next, context)).IsEqualTo("Chris Claremont");
		await Assert.That(FileName(WriterPerIssue, without, context)).IsEqualTo("ASKS: writer");
	}

	[Test]
	public async Task The_largest_preset_that_is_fully_present_wins()
	{
		BookRecord saved = XMen7();
		BookRecord other = XMen7();
		RenderContext context = With(
			With(DefaultContext, IssueKey(Fields.Writer, saved), Pick("Chris Claremont") with { SaveAsPreset = true }),
			IssueKey(Fields.Writer, other), Pick("Jim Lee", "Chris Claremont") with { SaveAsPreset = true });

		await Assert.That(FileName(WriterPerIssue, XMen7((Fields.Writer, "Chris Claremont, Jim Lee, Scott Lobdell")), context)).IsEqualTo("Jim Lee, Chris Claremont");
		await Assert.That(FileName(WriterPerIssue, XMen7((Fields.Writer, "Chris Claremont, Scott Lobdell")), context)).IsEqualTo("Chris Claremont");
	}

	[Test]
	public async Task A_preset_that_asks_when_more_is_preselected_on_a_book_with_more_items_and_used_on_an_exact_match()
	{
		BookRecord saved = XMen7();
		RenderContext context = With(
			DefaultContext, IssueKey(Fields.Writer, saved), Pick("Jim Lee", "Chris Claremont") with { SaveAsPreset = true, PresetAskWhenMore = true });

		Question question = OnlyQuestion(
			Render(WriterPerIssue, TemplateTarget.FileName, XMen7((Fields.Writer, "Chris Claremont, Jim Lee, Scott Lobdell")), context));

		await Assert.That(string.Join("|", question.Preselected)).IsEqualTo("Jim Lee|Chris Claremont");
		await Assert.That(string.Join("|", question.Choices)).IsEqualTo("Chris Claremont|Jim Lee|Scott Lobdell");
		await Assert.That(FileName(WriterPerIssue, XMen7(), context)).IsEqualTo("Jim Lee, Chris Claremont");
	}

	[Test]
	public async Task A_preset_applies_to_its_own_field_only()
	{
		BookRecord saved = XMen7();
		RenderContext context = With(DefaultContext, IssueKey(Fields.Penciller, saved), Pick("Jim Lee") with { SaveAsPreset = true });

		await Assert.That(FileName(WriterPerIssue, XMen7(), context)).IsEqualTo("ASKS: writer");
	}

	[Test]
	public async Task An_answer_as_folders_makes_one_level_per_item_in_a_folder_template()
	{
		BookRecord book = XMen7();
		RenderContext context = With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Chris Claremont", "Jim Lee") with { AsFolders = true });

		await Assert.That(Folder(@"{<publisher>}\{by <writer(, )(issue)>}\{<series>}", book, context)).IsEqualTo(@"Marvel\by Chris Claremont\Jim Lee\X-Men");
	}

	[Test]
	public async Task An_answer_as_folders_still_joins_with_the_separator_in_a_file_name()
	{
		BookRecord book = XMen7();
		RenderContext context = With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Chris Claremont", "Jim Lee") with { AsFolders = true });

		await Assert.That(FileName(WriterPerIssue, book, context)).IsEqualTo("Chris Claremont, Jim Lee");
	}

	[Test]
	public async Task A_preset_saved_as_folders_makes_folders()
	{
		BookRecord saved = XMen7();
		RenderContext context = With(
			DefaultContext, IssueKey(Fields.Writer, saved), Pick("Jim Lee", "Chris Claremont") with { SaveAsPreset = true, AsFolders = true });

		await Assert.That(Folder(@"{<writer(, )(issue)>}", XMen7(), context)).IsEqualTo(@"Jim Lee\Chris Claremont");
	}

	[Test]
	public async Task A_test_on_a_multi_value_token_compares_the_joined_text()
	{
		BookRecord book = XMen7();
		RenderContext context = With(DefaultContext, IssueKey(Fields.Writer, book), Pick("Jim Lee"));

		await Assert.That(FileName("{Lee<?writer(, )(issue)(Jim Lee)>}", book, context)).IsEqualTo("Lee");
		await Assert.That(FileName("X{Lee<?writer(, )(issue)(Stan Lee)>}", book, context)).IsEqualTo("X");
	}
}
