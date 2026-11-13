using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibraryOrganizer;

/// <summary>
/// Turns a token's name and args into a Field, a ValueFormat and a ValueTest. The rules for which args a field accepts
/// live here, keyed by FieldKind.
/// </summary>
internal static class TokenResolver
{
	private const int MaxPadWidth = 50;

	/// <param name="digits">The digit run after the name, or null. Never set together with <paramref name="arguments"/>.</param>
	/// <param name="arguments">The contents of each parenthesized group, in order.</param>
	public static (Token? Token, IReadOnlyList<TemplateError> Errors) Resolve(
		string rawName, string? digits, IReadOnlyList<string> arguments, int start, int length)
	{
		try
		{
			return (ResolveOrThrow(rawName, digits, arguments.ToList(), start, length), []);
		}
		catch (TokenException e)
		{
			return (null, [new TemplateError(start, length, e.Message)]);
		}
	}

	private static Token ResolveOrThrow(string rawName, string? digits, List<string> arguments, int start, int length)
	{
		string name = rawName;
		char prefix = name.Length > 0 && name[0] is '?' or '!' ? name[0] : '\0';
		string? testArgument = null;
		if (prefix != '\0')
		{
			name = name.Substring(1);
			if (arguments.Count > 0)
			{
				testArgument = arguments[arguments.Count - 1];
				arguments.RemoveAt(arguments.Count - 1);
			}
			else if (prefix == '?')
			{
				throw new TokenException($"'?' needs a value to compare with in parentheses, as in <?{name}(Annual)>.");
			}
		}

		bool numberSign = name.EndsWith("#", StringComparison.Ordinal);
		if (numberSign)
		{
			name = name.Substring(0, name.Length - 1);
		}
		if (name.Length == 0)
		{
			throw new TokenException("Expected a field name after '<'.");
		}
		Field field = Fields.ByTemplateName(name) ?? throw new TokenException(UnknownField(name));
		if (numberSign && field.Kind != FieldKind.Month)
		{
			throw new TokenException($"'#' writes a month as a number. '{name}' is not a month.");
		}

		ValueFormat format = Format(field, name, numberSign, digits, arguments);
		return new Token(field, format, Test(prefix, testArgument), start, length);
	}

	private static ValueTest? Test(char prefix, string? argument)
	{
		if (prefix == '\0')
		{
			return null;
		}
		if (argument is null)
		{
			return new ValueTest.IsEmpty();
		}
		if (!argument.StartsWith("!", StringComparison.Ordinal))
		{
			return prefix == '?' ? new ValueTest.EqualsText(argument) : new ValueTest.NotEqualsText(argument);
		}
		Regex pattern;
		try
		{
			// "(?!)" never matches, which is what 2.x did with an empty pattern.
			string body = argument.Length == 1 ? "(?!)" : argument.Substring(1);
			pattern = new Regex(@"\A(?:" + body + ")", RegexOptions.CultureInvariant);
		}
		catch (ArgumentException e)
		{
			throw new TokenException($"'{argument.Substring(1)}' is not a valid pattern: {e.Message}");
		}
		return prefix == '?' ? new ValueTest.MatchesStart(pattern) : new ValueTest.NotMatchesStart(pattern);
	}

	private static ValueFormat Format(Field field, string name, bool numberSign, string? digits, List<string> arguments)
	{
		if (digits != null)
		{
			PadWidth width = Width(digits);
			return field.Kind switch
			{
				FieldKind.Int or FieldKind.Issue => new ValueFormat.Padded(width),
				// 2.x padded the month number for <month2> as well as <month#2>.
				FieldKind.Month => new ValueFormat.MonthNumber(width),
				_ => throw new TokenException($"'{name}' is not a number, so it cannot take a padding width."),
			};
		}
		if (numberSign)
		{
			return arguments.Count == 0
				? new ValueFormat.MonthNumber(null)
				: throw new TokenException($"'{name}#' takes a padding width only, as in <{name}#2>.");
		}
		if (field == Fields.ReadPercentage && arguments.Count == 3)
		{
			return new ValueFormat.ReadThreshold(arguments[0], Operator(arguments[1]), Integer(arguments[2], "The percentage"));
		}
		if (field == Fields.FirstLetter)
		{
			return arguments.Count == 1
				? new ValueFormat.FirstLetterOf(FirstLetterSource(arguments[0]))
				: throw new TokenException("'first' takes the field to read, as in <first(Series)>.");
		}
		if (field == Fields.Counter)
		{
			if (arguments.Count != 3)
			{
				throw new TokenException("'counter' takes a start, a step and a padding width, as in <counter(1)(1)(3)>.");
			}
			int pad = Integer(arguments[2], "The padding width");
			return pad is >= 0 and <= MaxPadWidth
				? new ValueFormat.Counter(Integer(arguments[0], "The start"), Integer(arguments[1], "The step"), pad)
				: throw new TokenException($"The padding width must be from 0 to {MaxPadWidth}.");
		}
		if (field == Fields.Custom)
		{
			return arguments.Count == 1 && arguments[0].Length > 0
				? new ValueFormat.CustomKey(arguments[0])
				: throw new TokenException("'Custom' takes the name of a custom value, as in <Custom(MyKey)>.");
		}
		if (arguments.Count == 0)
		{
			return field.Kind == FieldKind.Date ? new ValueFormat.DateFormat(null) : new ValueFormat.Plain();
		}
		return field.Kind switch
		{
			FieldKind.Date when arguments.Count == 1 => new ValueFormat.DateFormat(arguments[0].Length == 0 ? null : DatePattern(arguments[0])),
			FieldKind.Date => throw new TokenException($"'{name}' takes one date format, as in <{name}(yyyy-MM)>."),
			FieldKind.YesNo or FieldKind.MangaYesNo when arguments.Count == 1 => new ValueFormat.YesNoText(arguments[0], OnNo: false),
			FieldKind.YesNo or FieldKind.MangaYesNo when arguments.Count == 2 && arguments[1] == "!" => new ValueFormat.YesNoText(arguments[0], OnNo: true),
			FieldKind.YesNo or FieldKind.MangaYesNo
				=> throw new TokenException($"'{name}' takes the text to write for Yes, as in <{name}(Text)>, or for No, as in <{name}(Text)(!)>."),
			FieldKind.Items when arguments.Count == 2 => new ValueFormat.MultiValue(arguments[0], Scope(arguments[1], name)),
			FieldKind.Items => throw new TokenException($"'{name}' takes a separator and a scope, as in <{name}(, )(issue)> or <{name}(, )(series)>."),
			_ when field == Fields.ReadPercentage => throw new TokenException("'read' takes a text, an operator and a percentage, as in <read(Read)(>)(90)>."),
			_ => throw new TokenException($"'{name}' takes no arguments in parentheses."),
		};
	}

	private static PadWidth Width(string digits)
		=> int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int width) && width <= MaxPadWidth
			? width == 0 ? new PadWidth.AutoFromLastIssue() : new PadWidth.Fixed(width)
			: throw new TokenException($"The padding width must be from 0 to {MaxPadWidth}.");

	private static int Integer(string text, string what)
		=> int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
			? value
			: throw new TokenException($"{what} must be a whole number, not '{text}'.");

	private static Comparison Operator(string text) => text.Trim() switch
	{
		"<" => Comparison.Less,
		"=" => Comparison.Equal,
		">" => Comparison.Greater,
		_ => throw new TokenException($"The operator must be <, = or >, not '{text}'."),
	};

	private static AnswerScopeKind Scope(string text, string name)
	{
		string scope = text.Trim();
		return scope.Equals("issue", StringComparison.OrdinalIgnoreCase) ? AnswerScopeKind.Issue
			: scope.Equals("series", StringComparison.OrdinalIgnoreCase) ? AnswerScopeKind.Series
			: throw new TokenException($"The scope of '{name}' must be issue or series, not '{text}'.");
	}

	/// <summary>2.x accepted a display name ("Series") or a property name ("ShadowSeries"); the template name is the natural third.</summary>
	private static Field FirstLetterSource(string text)
	{
		string name = text.Trim();
		Field? source = Fields.ByLegacyName(name) ?? Fields.ByTemplateName(name)
			?? Fields.All.FirstOrDefault(field => field.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
		return source is { Kind: not FieldKind.Synthetic }
			? source
			: throw new TokenException($"'first' cannot read '{text}'. Name a field, as in <first(Series)>.");
	}

	private static string DatePattern(string pattern)
	{
		try
		{
			_ = new DateTime(2000, 1, 2, 3, 4, 5).ToString(pattern, CultureInfo.InvariantCulture);
			return pattern;
		}
		catch (FormatException)
		{
			throw new TokenException($"'{pattern}' is not a valid date format. Try yyyy-MM-dd.");
		}
	}

	private static string UnknownField(string name)
	{
		string? closest = Fields.All
			.Where(field => field.TemplateName != null)
			.Select(field => (Name: field.TemplateName!, Distance: EditDistance(name.ToLowerInvariant(), field.TemplateName!.ToLowerInvariant())))
			.Where(candidate => candidate.Distance <= Math.Max(1, name.Length / 3))
			.OrderBy(candidate => candidate.Distance)
			.Select(candidate => candidate.Name)
			.FirstOrDefault();
		return closest is null ? $"Unknown field '{name}'." : $"Unknown field '{name}'. Did you mean '{closest}'?";
	}

	private static int EditDistance(string a, string b)
	{
		int[] row = Enumerable.Range(0, b.Length + 1).ToArray();
		for (int i = 1; i <= a.Length; i++)
		{
			int diagonal = row[0];
			row[0] = i;
			for (int j = 1; j <= b.Length; j++)
			{
				int above = row[j];
				row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
				diagonal = above;
			}
		}
		return row[b.Length];
	}

	private sealed class TokenException(string message) : Exception(message);
}
