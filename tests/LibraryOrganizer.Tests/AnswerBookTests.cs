using System.Linq;
using System.Threading.Tasks;
using static LibraryOrganizer.Tests.TemplateFixture;

namespace LibraryOrganizer.Tests;

public class AnswerBookTests
{
	[Test]
	public async Task With_returns_a_new_book_and_leaves_the_old_one_alone()
	{
		BookRecord book = XMen7();
		QuestionKey key = IssueKey(Fields.Writer, book);

		AnswerBook answers = AnswerBook.Empty.With(key, Pick("Jim Lee"));

		await Assert.That(AnswerBook.Empty.Remembered.Count).IsEqualTo(0);
		await Assert.That(answers.Remembered.Single().Key).IsEqualTo(key);
		await Assert.That(answers.Remembered.Single().Answer.Selected.Single()).IsEqualTo("Jim Lee");
		await Assert.That(answers.Presets.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_later_answer_for_the_same_question_replaces_the_earlier_one()
	{
		BookRecord book = XMen7();
		QuestionKey key = IssueKey(Fields.Writer, book);

		AnswerBook answers = AnswerBook.Empty.With(key, Pick("Jim Lee")).With(key, Pick("Chris Claremont"));

		await Assert.That(answers.Remembered.Single().Answer.Selected.Single()).IsEqualTo("Chris Claremont");
	}

	[Test]
	public async Task Series_answers_are_keyed_by_series_ignoring_case()
	{
		BookRecord book = TestBooks.Book(series: "X-Men", publisher: "Marvel");
		BookRecord sameSeries = TestBooks.Book(series: "x-men", publisher: "MARVEL");
		BookRecord otherSeries = TestBooks.Book(series: "X-Factor", publisher: "Marvel");
		AnswerBook answers = AnswerBook.Empty.With(SeriesKey(Fields.Writer, book), Pick("Jim Lee"));

		await Assert.That(answers.Find(SeriesKey(Fields.Writer, sameSeries), [])).IsNotNull();
		await Assert.That(answers.Find(SeriesKey(Fields.Writer, otherSeries), [])).IsNull();
		await Assert.That(answers.Find(SeriesKey(Fields.Penciller, book), [])).IsNull();
	}

	[Test]
	public async Task Forget_removes_one_answer()
	{
		BookRecord book = XMen7();
		AnswerBook answers = AnswerBook.Empty
			.With(IssueKey(Fields.Writer, book), Pick("Jim Lee"))
			.With(SeriesKey(Fields.Writer, book), Pick("Chris Claremont"));

		AnswerBook forgotten = answers.Forget(IssueKey(Fields.Writer, book));

		await Assert.That(forgotten.Remembered.Single().Key).IsEqualTo(SeriesKey(Fields.Writer, book));
		await Assert.That(answers.Remembered.Count).IsEqualTo(2);
	}

	[Test]
	public async Task Saving_a_preset_keeps_the_items_and_both_flags()
	{
		AnswerBook answers = AnswerBook.Empty.With(
			IssueKey(Fields.Writer, XMen7()), Pick("Jim Lee", "Chris Claremont") with { SaveAsPreset = true, PresetAskWhenMore = true, AsFolders = true });

		Preset preset = answers.Presets.Single();

		await Assert.That(preset.Field).IsSameReferenceAs(Fields.Writer);
		await Assert.That(string.Join("|", preset.Items)).IsEqualTo("Jim Lee|Chris Claremont");
		await Assert.That(preset.AskWhenMore).IsTrue();
		await Assert.That(preset.AsFolders).IsTrue();
	}

	[Test]
	public async Task Saving_the_same_items_again_replaces_the_preset()
	{
		AnswerBook answers = AnswerBook.Empty
			.With(IssueKey(Fields.Writer, XMen7()), Pick("Jim Lee", "Chris Claremont") with { SaveAsPreset = true, PresetAskWhenMore = true })
			.With(IssueKey(Fields.Writer, XMen7()), Pick("chris claremont", "Jim Lee") with { SaveAsPreset = true });

		await Assert.That(answers.Presets.Count).IsEqualTo(1);
		await Assert.That(answers.Presets[0].AskWhenMore).IsFalse();
	}

	[Test]
	public async Task A_series_answer_or_an_empty_selection_saves_no_preset()
	{
		BookRecord book = XMen7();
		AnswerBook answers = AnswerBook.Empty
			.With(SeriesKey(Fields.Writer, book), Pick("Jim Lee") with { SaveAsPreset = true })
			.With(IssueKey(Fields.Writer, book), Pick() with { SaveAsPreset = true });

		await Assert.That(answers.Presets.Count).IsEqualTo(0);
	}

	[Test]
	public async Task ForgetPreset_removes_the_preset_but_not_the_answer_that_made_it()
	{
		AnswerBook answers = AnswerBook.Empty
			.With(IssueKey(Fields.Writer, XMen7()), Pick("Jim Lee") with { SaveAsPreset = true })
			.With(IssueKey(Fields.Penciller, XMen7()), Pick("Jim Lee") with { SaveAsPreset = true });

		AnswerBook forgotten = answers.ForgetPreset(answers.Presets.First(preset => preset.Field == Fields.Writer));

		await Assert.That(forgotten.Presets.Single().Field).IsSameReferenceAs(Fields.Penciller);
		await Assert.That(forgotten.Remembered.Count).IsEqualTo(2);
		await Assert.That(answers.Presets.Count).IsEqualTo(2);
	}
}
