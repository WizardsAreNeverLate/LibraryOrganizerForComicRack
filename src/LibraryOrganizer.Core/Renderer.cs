using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>A rendered path before cleaning. Folder structure is a piece, never a character, so a value cannot forge a level.</summary>
internal abstract record Piece
{
	private Piece() { }

	public sealed record Text(string Raw) : Piece;
	public sealed record Break : Piece;

	/// <summary>Preview only: stands in for a multi-value token that has no answer yet. Cleans to "‹name?›".</summary>
	public sealed record Placeholder(string Name) : Piece;
}

internal abstract record RenderOutcome
{
	private RenderOutcome() { }

	public sealed record Done(IReadOnlyList<Piece> Pieces) : RenderOutcome;

	/// <summary>Every distinct question the template still needs for this book, in template order.</summary>
	public sealed record NeedsAnswers(IReadOnlyList<Question> Questions) : RenderOutcome;
}

/// <summary>Everything a render reads besides the template and the book.</summary>
/// <param name="Ordinal">The book's index among the books this profile renders, in selection order. Drives counter tokens.</param>
/// <param name="Preview">Write a placeholder for an unanswered multi-value token instead of asking.</param>
internal sealed record RenderContext(PathStyle Style, SeriesIndex Series, AnswerBook Answers, bool DontAskWhenOneValue, int Ordinal, bool Preview);

internal static class Renderer
{
	private static readonly HashSet<string> Articles = new(
		("the a an de het een die der das des dem ein eines einer einen la le l' les un une el las los "
			+ "una unos unas o os um uma uns umas en et il lo uno gli").Split(' '),
		StringComparer.OrdinalIgnoreCase);

	private static readonly string[] ElidedArticles = ["l'", "d'", "l’", "d’"];

	public static RenderOutcome Render(Template template, BookRecord book, RenderContext context)
	{
		var run = new Run(template.Target, book, context);
		var pieces = new List<Piece>();
		run.Write(template.Nodes, pieces);
		return run.Questions.Count > 0 ? new RenderOutcome.NeedsAnswers(run.Questions) : new RenderOutcome.Done(pieces);
	}

	private readonly record struct Value(string? Text, IReadOnlyList<string>? Folders = null);

	private sealed class Run(TemplateTarget target, BookRecord book, RenderContext context)
	{
		public List<Question> Questions { get; } = [];

		public void Write(IReadOnlyList<Node> nodes, List<Piece> pieces)
		{
			foreach (Node node in nodes)
			{
				switch (node)
				{
					case Node.Literal literal:
						pieces.Add(new Piece.Text(literal.Text));
						break;
					case Node.FolderBreak:
						pieces.Add(new Piece.Break());
						break;
					case Node.Group group:
						Write(group, pieces);
						break;
				}
			}
		}

		private void Write(Node.Group group, List<Piece> pieces)
		{
			Token token = group.Token;
			Value value = Format(token);
			if (value.Text is null)
			{
				Write(group.Before, pieces);
				if (token.Test is null)
				{
					pieces.Add(new Piece.Placeholder(token.Field.TemplateName!));
				}
				Write(group.After, pieces);
				return;
			}
			if (token.Test is not null)
			{
				if (token.Test.Passes(value.Text))
				{
					Write(group.Before, pieces);
					Write(group.After, pieces);
				}
				return;
			}
			if (value.Text.Length == 0)
			{
				if (context.Style.EmptySubstitutes.TryGetValue(token.Field, out string? substitute) && substitute.Length > 0)
				{
					pieces.Add(new Piece.Text(substitute));
				}
				return;
			}
			Write(group.Before, pieces);
			if (value.Folders is null)
			{
				pieces.Add(new Piece.Text(value.Text));
			}
			else
			{
				for (int i = 0; i < value.Folders.Count; i++)
				{
					if (i > 0)
					{
						pieces.Add(new Piece.Break());
					}
					pieces.Add(new Piece.Text(value.Folders[i]));
				}
			}
			Write(group.After, pieces);
		}

		private Value Format(Token token)
		{
			FieldValue raw = FieldReader.Read(token.Field, book, context.Series);
			return token.Format switch
			{
				ValueFormat.Plain => new Value(token.Field.Kind == FieldKind.Month ? MonthName(raw) : raw.ToRawText()),
				ValueFormat.Padded padded => new Value(Pad(raw, padded.Width, token.Field)),
				ValueFormat.MonthNumber month => new Value(month.Width is null ? raw.ToRawText() : Pad(raw, month.Width, token.Field)),
				ValueFormat.YesNoText yesNo => new Value(raw is FieldValue.YesNo answer && (answer.Value == YesNoValue.No) == yesNo.OnNo ? yesNo.Text : ""),
				ValueFormat.ReadThreshold read => new Value(raw is FieldValue.Int percent && Compare(percent.Value, read.Op, read.Percent) ? read.Text : ""),
				ValueFormat.FirstLetterOf first => new Value(FirstLetter(FieldReader.Read(first.Source, book, context.Series).ToRawText())),
				ValueFormat.Counter counter => new Value(
					new IssueNumber((counter.Start + (long)counter.Step * context.Ordinal).ToString(CultureInfo.InvariantCulture)).Pad(counter.Pad)),
				ValueFormat.DateFormat date => new Value(raw is FieldValue.Date day ? day.Value.ToString(date.Pattern ?? "yyyy-MM-dd", CultureInfo.InvariantCulture) : ""),
				ValueFormat.CustomKey custom => new Value(book.Custom(custom.Key) ?? ""),
				ValueFormat.MultiValue multi => MultiValue(token.Field, raw is FieldValue.Items items ? items.Values : [], multi),
				_ => throw new ArgumentOutOfRangeException(nameof(token)),
			};
		}

		private string MonthName(FieldValue raw)
			=> raw is FieldValue.Int month && month.Value >= 1 && month.Value <= context.Style.MonthNames.Count
				? context.Style.MonthNames[month.Value - 1]
				: raw.ToRawText();

		private string Pad(FieldValue raw, PadWidth width, Field field)
		{
			if (raw.IsUnset)
			{
				return "";
			}
			int digits = width is PadWidth.Fixed fixedWidth
				? fixedWidth.Width
				: FieldReader.Read(field, context.Series.Last(book.SeriesKey) ?? book, context.Series).ToRawText().Length;
			return new IssueNumber(raw.ToRawText()).Pad(digits);
		}

		private static bool Compare(int value, Comparison op, int other) => op switch
		{
			Comparison.Less => value < other,
			Comparison.Greater => value > other,
			_ => value == other,
		};

		private Value MultiValue(Field field, IReadOnlyList<string> bookItems, ValueFormat.MultiValue format)
		{
			bool Has(string item) => bookItems.Contains(item, StringComparer.OrdinalIgnoreCase);
			Value Chosen(IEnumerable<string> items, bool asFolders)
			{
				List<string> chosen = items.ToList();
				return new Value(string.Join(format.Separator, chosen), asFolders && target == TemplateTarget.Folder ? chosen : null);
			}

			if (format.Scope == AnswerScopeKind.Series)
			{
				var key = new QuestionKey(field, new AnswerScope.Series(book.SeriesKey));
				if (context.Answers.Find(key, bookItems) is { } answer)
				{
					return Chosen(answer.EveryIssue ? answer.Selected : answer.Selected.Where(Has), answer.AsFolders);
				}
				List<string> choices = context.Series.DistinctItems(book.SeriesKey, field)
					.Concat(bookItems)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
					.ToList();
				if (choices.Count == 0 || (choices.Count == 1 && context.DontAskWhenOneValue))
				{
					return Chosen(choices.Where(Has), asFolders: false);
				}
				return Ask(new Question(key, field.DisplayName, choices, [], book.Caption, Waiting: 1));
			}

			if (bookItems.Count == 0)
			{
				return new Value("");
			}
			var issueKey = new QuestionKey(field, new AnswerScope.Issue(book.Id));
			if (context.Answers.Find(issueKey, bookItems) is { } issueAnswer)
			{
				return Chosen(issueAnswer.Selected, issueAnswer.AsFolders);
			}
			if (bookItems.Count == 1 && context.DontAskWhenOneValue)
			{
				return Chosen(bookItems, asFolders: false);
			}
			Preset? preset = context.Answers.BestPreset(field, bookItems);
			if (preset is not null && (!preset.AskWhenMore || bookItems.All(item => preset.Items.Contains(item, StringComparer.OrdinalIgnoreCase))))
			{
				return Chosen(preset.Items, preset.AsFolders);
			}
			return Ask(new Question(issueKey, field.DisplayName, bookItems, preset?.Items ?? [], book.Caption, Waiting: 1));
		}

		private Value Ask(Question question)
		{
			if (!context.Preview && Questions.All(asked => asked.Key != question.Key))
			{
				Questions.Add(question);
			}
			return new Value(null);
		}
	}

	private static string FirstLetter(string value)
	{
		string text = value.TrimStart();
		string rest = text;
		int space = 0;
		while (space < text.Length && !char.IsWhiteSpace(text[space]))
		{
			space++;
		}
		if (space < text.Length && Articles.Contains(text.Substring(0, space)))
		{
			rest = text.Substring(space).TrimStart();
		}
		else if (ElidedArticles.Any(article => text.StartsWith(article, StringComparison.OrdinalIgnoreCase)))
		{
			rest = text.Substring(2).TrimStart();
		}
		if (rest.Length == 0)
		{
			rest = text;
		}
		if (rest.Length == 0)
		{
			return "";
		}
		return rest.Substring(0, char.IsHighSurrogate(rest[0]) && rest.Length > 1 ? 2 : 1).ToUpperInvariant();
	}
}
