using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class PreviewTests
{
	private static readonly Profile WriterInName = TestProfiles.Make(edit: settings => settings with { FileTemplate = "{<series>}{ #<number>}{ by <writer(, )(issue)>}" });

	private static BookRecord Book(string? writer = null, string publisher = "Marvel")
		=> TestBooks.Book(number: "7", writer: writer, publisher: publisher, path: @"D:\Incoming\seven.cbz");

	[Test]
	public async Task A_preview_renders_the_full_path()
	{
		PreviewResult preview = Organizer.Preview(TestProfiles.Make(), Book(), SeriesIndex.Empty, AnswerBook.Empty);

		await Assert.That(preview.Path).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7.cbz");
		await Assert.That(preview.Problem).IsNull();
		await Assert.That(preview.Filter.Accepted).IsTrue();
		await Assert.That(preview.Unanswered.Count).IsEqualTo(0);
	}

	[Test]
	public async Task An_unanswered_multi_value_token_shows_a_placeholder()
	{
		PreviewResult preview = Organizer.Preview(WriterInName, Book("Chris Claremont, Jim Lee"), SeriesIndex.Empty, AnswerBook.Empty);

		await Assert.That(preview.Path).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 by ‹writer?›.cbz");
		await Assert.That(preview.Unanswered.Single()).IsEqualTo(Fields.Writer);
		await Assert.That(preview.Problem).IsNull();
	}

	[Test]
	public async Task An_answer_replaces_the_placeholder()
	{
		BookRecord book = Book("Chris Claremont, Jim Lee");
		AnswerBook answers = AnswerBook.Empty.With(TemplateFixture.IssueKey(Fields.Writer, book), TemplateFixture.Pick("Jim Lee"));

		PreviewResult preview = Organizer.Preview(WriterInName, book, SeriesIndex.Empty, answers);

		await Assert.That(preview.Path).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 by Jim Lee.cbz");
		await Assert.That(preview.Unanswered.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_book_the_filter_rejects_still_shows_its_path_and_says_why()
	{
		Profile dcOnly = TestProfiles.Make(edit: settings => settings with
		{
			RuleMode = RuleMode.Only,
			Rules = new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition("publisher", Operator.Is, "DC")]),
		});

		PreviewResult preview = Organizer.Preview(dcOnly, Book(), SeriesIndex.Empty, AnswerBook.Empty);

		await Assert.That(preview.Path).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7.cbz");
		await Assert.That(preview.Filter.Accepted).IsFalse();
		await Assert.That(preview.Filter.Reason).Contains("Publisher is DC");
		await Assert.That(preview.Problem).IsEqualTo(preview.Filter.Reason);
	}

	[Test]
	public async Task A_blank_file_name_has_no_path_and_a_problem()
	{
		Profile blank = TestProfiles.Make(edit: settings => settings with { FileTemplate = "{<title>}" });

		PreviewResult preview = Organizer.Preview(blank, Book(), SeriesIndex.Empty, AnswerBook.Empty);

		await Assert.That(preview.Path).IsNull();
		await Assert.That(preview.Problem).IsNotNull();
	}

	[Test]
	public async Task An_empty_required_field_is_a_problem_and_the_path_goes_under_the_failed_folder()
	{
		Profile required = TestProfiles.Make(edit: settings => settings with { RequiredFields = ["year"], FailedBooksFolder = @"D:\Failed" });

		PreviewResult preview = Organizer.Preview(required, Book(), SeriesIndex.Empty, AnswerBook.Empty);

		await Assert.That(preview.Path).IsEqualTo(@"D:\Failed\Marvel\X-Men\X-Men #7.cbz");
		await Assert.That(preview.Problem!).Contains("Year");
	}
}
