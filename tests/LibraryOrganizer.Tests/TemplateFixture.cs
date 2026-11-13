using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer.Tests;

/// <summary>Books and render helpers for the template tests. Each helper cleans the output the way the planner will.</summary>
internal static class TemplateFixture
{
	public static readonly RenderContext DefaultContext = new(PathStyle.Default, SeriesIndex.Empty, AnswerBook.Empty, DontAskWhenOneValue: true, Ordinal: 0, Preview: false);

	/// <summary>The book of the "Test vectors" table in the 2.x rebuild guide, with overrides applied on top.</summary>
	public static BookRecord XMen7(params (Field Field, object? Value)[] overrides)
		=> Book(
		[
			(Fields.Publisher, "Marvel"), (Fields.Series, "X-Men"), (Fields.Volume, 2), (Fields.Number, "7"), (Fields.Count, 12),
			(Fields.Month, 3), (Fields.Year, 1992), (Fields.Writer, "Chris Claremont, Jim Lee"), (Fields.Manga, YesNoValue.No),
			.. overrides,
		]);

	/// <summary>The library of the test vectors: issues 1 to 120, issue 1 from 1991, plus the book itself.</summary>
	public static SeriesIndex XMenSeries(BookRecord book)
		=> SeriesIndex.Build(Enumerable.Range(1, 120)
			.Select(number => TestBooks.Book(series: "X-Men", volume: 2, number: number.ToString(), publisher: "Marvel", year: number == 1 ? 1991 : null))
			.Append(book));

	/// <summary>Later values for a field replace earlier ones. Null leaves the field unset.</summary>
	public static BookRecord Book(IEnumerable<(Field Field, object? Value)> values, IReadOnlyDictionary<string, string>? custom = null)
	{
		FieldValue[] slots = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		foreach ((Field field, object? value) in values)
		{
			slots[field.Ordinal] = value switch
			{
				null => FieldValue.Unset.Value,
				string text when field.Kind == FieldKind.Issue => new FieldValue.Issue(new IssueNumber(text)),
				string text when field.Kind == FieldKind.Items => FieldValue.ParseItems(text),
				string text => new FieldValue.Text(text),
				int number => new FieldValue.Int(number),
				decimal number => new FieldValue.Decimal(number),
				YesNoValue yesNo => new FieldValue.YesNo(yesNo),
				DateTime date => new FieldValue.Date(date),
				_ => throw new ArgumentException($"No FieldValue for {value.GetType().Name}."),
			};
		}
		return new BookRecord(
			new BookId(Guid.NewGuid()),
			new BookLocation.File(AbsPath.TryParse(@"D:\Incoming\book.cbz")!.Value, Exists: true),
			"X-Men #7",
			slots,
			custom ?? new Dictionary<string, string>());
	}

	public static Template Parse(string source, TemplateTarget target)
	{
		TemplateParse parse = Template.Parse(source, target);
		return parse.Template
			?? throw new InvalidOperationException($"'{source}' did not parse: {string.Join("; ", parse.Errors.Select(e => $"{e.Start}+{e.Length} {e.Message}"))}");
	}

	public static RenderOutcome Render(string source, TemplateTarget target, BookRecord book, RenderContext? context = null)
		=> Renderer.Render(Parse(source, target), book, context ?? DefaultContext);

	/// <summary>The cleaned file name, or "ASKS: field, field" when the render needs answers.</summary>
	public static string FileName(string source, BookRecord book, RenderContext? context = null)
		=> Render(source, TemplateTarget.FileName, book, context) switch
		{
			RenderOutcome.Done done => (context ?? DefaultContext).Style.CleanFileName(done.Pieces),
			RenderOutcome.NeedsAnswers needs => Asks(needs),
			_ => throw new InvalidOperationException(),
		};

	/// <summary>The cleaned folder levels joined with '\', or "ASKS: field, field" when the render needs answers.</summary>
	public static string Folder(string source, BookRecord book, RenderContext? context = null)
		=> Render(source, TemplateTarget.Folder, book, context) switch
		{
			RenderOutcome.Done done => string.Join(@"\", (context ?? DefaultContext).Style.CleanFolder(done.Pieces)),
			RenderOutcome.NeedsAnswers needs => Asks(needs),
			_ => throw new InvalidOperationException(),
		};

	public static QuestionKey IssueKey(Field field, BookRecord book) => new(field, new AnswerScope.Issue(book.Id));

	public static QuestionKey SeriesKey(Field field, BookRecord book) => new(field, new AnswerScope.Series(book.SeriesKey));

	public static Answer Pick(params string[] selected) => new(selected, AsFolders: false, EveryIssue: false, SaveAsPreset: false, PresetAskWhenMore: false);

	private static string Asks(RenderOutcome.NeedsAnswers needs) => "ASKS: " + string.Join(", ", needs.Questions.Select(question => question.Key.Field.Key));
}
