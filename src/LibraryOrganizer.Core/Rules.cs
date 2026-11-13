using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibraryOrganizer;

/// <summary>Rule operators. Which ones a field offers comes from its kind (<see cref="Operators.For"/>), so the editor and the evaluator agree.</summary>
public enum Operator
{
	Is, IsNot, Contains, DoesNotContain, StartsWith, MatchesRegex,
	GreaterThan, LessThan,
	ContainsItem, DoesNotContainItem,
	Before, After, On,
	IsEmpty, IsNotEmpty,
}

public static class Operators
{
	private static readonly Operator[] TextOperators =
		[Operator.Is, Operator.IsNot, Operator.Contains, Operator.DoesNotContain, Operator.StartsWith, Operator.MatchesRegex, Operator.IsEmpty, Operator.IsNotEmpty];

	private static readonly Operator[] NumberOperators =
		[Operator.Is, Operator.IsNot, Operator.GreaterThan, Operator.LessThan, Operator.IsEmpty, Operator.IsNotEmpty];

	private static readonly Operator[] YesNoOperators = [Operator.Is, Operator.IsNot];

	private static readonly Operator[] ItemsOperators =
		[Operator.ContainsItem, Operator.DoesNotContainItem, Operator.Contains, Operator.DoesNotContain, Operator.IsEmpty, Operator.IsNotEmpty];

	private static readonly Operator[] DateOperators = [Operator.Before, Operator.After, Operator.On, Operator.IsEmpty, Operator.IsNotEmpty];

	/// <summary>
	/// Text: Is, IsNot, Contains, DoesNotContain, StartsWith, MatchesRegex, IsEmpty, IsNotEmpty (case-insensitive).
	/// Int/Decimal/Issue/Month: Is, IsNot, GreaterThan, LessThan, IsEmpty, IsNotEmpty (numeric; Issue natural order).
	/// YesNo/MangaYesNo: Is, IsNot (operand is a YesNoValue or "Unknown").
	/// Items: ContainsItem, DoesNotContainItem (whole item, case-insensitive), Contains, DoesNotContain (substring of raw text), IsEmpty, IsNotEmpty.
	/// Date: Before, After, On, IsEmpty, IsNotEmpty.
	/// Synthetic: none.
	/// </summary>
	public static IReadOnlyList<Operator> For(FieldKind kind) => kind switch
	{
		FieldKind.Text => TextOperators,
		FieldKind.Int or FieldKind.Decimal or FieldKind.Issue or FieldKind.Month => NumberOperators,
		FieldKind.YesNo or FieldKind.MangaYesNo => YesNoOperators,
		FieldKind.Items => ItemsOperators,
		FieldKind.Date => DateOperators,
		_ => [],
	};

	public static string Describe(Operator op) => op switch
	{
		Operator.Is => "is",
		Operator.IsNot => "is not",
		Operator.Contains => "contains",
		Operator.DoesNotContain => "does not contain",
		Operator.StartsWith => "starts with",
		Operator.MatchesRegex => "matches",
		Operator.GreaterThan => "greater than",
		Operator.LessThan => "less than",
		Operator.ContainsItem => "has item",
		Operator.DoesNotContainItem => "has no item",
		Operator.Before => "before",
		Operator.After => "after",
		Operator.On => "on",
		Operator.IsEmpty => "is empty",
		Operator.IsNotEmpty => "is not empty",
		_ => op.ToString(),
	};

	internal static bool TakesOperand(Operator op) => op is not (Operator.IsEmpty or Operator.IsNotEmpty);
}

/// <summary>A parsed operand, typed by the field kind at the settings boundary.</summary>
public abstract record Operand
{
	private Operand() { }

	public sealed record None : Operand;
	public sealed record Text(string Value) : Operand;
	public sealed record Number(decimal Value) : Operand;
	public sealed record Issue(IssueNumber Value) : Operand;
	/// <summary>Null means "Unknown": the field has no value.</summary>
	public sealed record YesNo(YesNoValue? Value) : Operand;
	public sealed record Date(DateTime Value) : Operand;
	public sealed record Pattern(Regex Value) : Operand;
}

public enum Combinator { Any, All }

/// <summary>A rule tree node. Closed: a Condition or a Group.</summary>
public abstract record Rule
{
	private Rule() { }

	public sealed record Group(Combinator Combinator, IReadOnlyList<Rule> Children) : Rule;

	public sealed record Condition : Rule
	{
		private Condition(Field field, Operator op, Operand operand, string rawOperand)
		{
			Field = field;
			Operator = op;
			Operand = operand;
			RawOperand = rawOperand;
		}

		public Field Field { get; }
		public Operator Operator { get; }
		public Operand Operand { get; }

		/// <summary>The text the editor shows and settings.xml stores.</summary>
		public string RawOperand { get; }

		/// <summary>Boundary: rejects a field that is not a rule field, an operator the field kind does not offer, and an operand that does not parse for the kind.</summary>
		public static Condition? Create(Field field, Operator op, string rawOperand, out string? error)
		{
			rawOperand ??= "";
			if (!field.Uses.HasFlag(FieldUses.Rule))
			{
				error = $"{field.DisplayName} cannot be used in a rule.";
				return null;
			}
			if (!Operators.For(field.Kind).Contains(op))
			{
				error = $"{field.DisplayName} does not support '{Operators.Describe(op)}'.";
				return null;
			}
			Operand? operand = OperandParser.Parse(field.Kind, op, rawOperand, out error);
			return operand is null ? null : new Condition(field, op, operand, rawOperand);
		}

		/// <summary>"Publisher is DC", for the review grid.</summary>
		public string Describe()
			=> Operators.TakesOperand(Operator) ? $"{Field.DisplayName} {Operators.Describe(Operator)} {RawOperand}" : $"{Field.DisplayName} {Operators.Describe(Operator)}";
	}
}

public enum RuleMode
{
	/// <summary>Organize only books that match.</summary>
	Only,
	/// <summary>Organize every book except those that match (2.x "Do not").</summary>
	Except,
}

/// <summary>Excluded folders as normalized paths; a book is excluded when its file is under one of them, by whole segments.</summary>
public sealed record FolderSet(IReadOnlyList<AbsPath> Folders)
{
	public static readonly FolderSet None = new([]);

	public bool Contains(AbsPath file) => Find(file) is not null;

	/// <summary>The first excluded folder that holds the file.</summary>
	public AbsPath? Find(AbsPath file)
	{
		foreach (AbsPath folder in Folders)
		{
			if (file.IsUnder(folder))
			{
				return folder;
			}
		}
		return null;
	}
}

/// <summary>A profile's filter. An empty root group accepts every book in both modes, as 2.x.</summary>
public sealed record Filter(FolderSet ExcludedFolders, RuleMode Mode, Rule.Group Root)
{
	public static readonly Filter AcceptAll = new(FolderSet.None, RuleMode.Except, new Rule.Group(Combinator.Any, []));

	/// <summary>
	/// Excluded folder first, then the tree. Evaluation short-circuits. An empty group is "no opinion" and its parent skips it.
	/// The reason is for the review grid: "In excluded folder D:\Comics\Unsorted", "Matches rule: Publisher is DC", "Does not match: ...".
	/// </summary>
	internal FilterVerdict Evaluate(BookRecord book, SeriesIndex series)
	{
		if (book.Location is BookLocation.File file && ExcludedFolders.Find(file.Path) is { } folder)
		{
			return new FilterVerdict(false, $"In excluded folder {folder}");
		}
		RuleOutcome outcome = RuleEvaluator.Evaluate(Root, book, series);
		return (outcome.Matched, Mode) switch
		{
			(null, _) => new FilterVerdict(true, null),
			(true, RuleMode.Only) => new FilterVerdict(true, $"Matches rule: {outcome.Text}"),
			(false, RuleMode.Only) => new FilterVerdict(false, $"Does not match: {outcome.Text}"),
			(true, RuleMode.Except) => new FilterVerdict(false, $"Excluded by rule: {outcome.Text}"),
			_ => new FilterVerdict(true, null),
		};
	}
}

internal readonly record struct FilterVerdict(bool Accepted, string? Reason);

internal readonly record struct RuleOutcome(bool? Matched, string Text, bool Compound);

internal static class RuleEvaluator
{
	public static RuleOutcome Evaluate(Rule rule, BookRecord book, SeriesIndex series)
		=> rule switch
		{
			Rule.Condition condition => new RuleOutcome(Test(condition, FieldReader.Read(condition.Field, book, series)), condition.Describe(), false),
			Rule.Group group => EvaluateGroup(group, book, series),
			_ => throw new ArgumentOutOfRangeException(nameof(rule)),
		};

	private static RuleOutcome EvaluateGroup(Rule.Group group, BookRecord book, SeriesIndex series)
	{
		bool any = group.Combinator == Combinator.Any;
		var texts = new List<string>();
		foreach (Rule child in group.Children)
		{
			RuleOutcome outcome = Evaluate(child, book, series);
			if (outcome.Matched is not { } matched)
			{
				continue;
			}
			if (matched == any)
			{
				return outcome;
			}
			texts.Add(outcome.Compound ? $"({outcome.Text})" : outcome.Text);
		}
		return texts.Count == 0
			? new RuleOutcome(null, "", false)
			: new RuleOutcome(!any, string.Join(any ? " or " : " and ", texts), texts.Count > 1);
	}

	private static bool Test(Rule.Condition condition, FieldValue value)
	{
		switch (condition.Operator)
		{
			case Operator.IsEmpty:
				return IsEmpty(value);
			case Operator.IsNotEmpty:
				return !IsEmpty(value);
			case Operator.IsNot:
				return !Holds(Operator.Is, condition.Operand, value);
			case Operator.DoesNotContain:
				return !Holds(Operator.Contains, condition.Operand, value);
			case Operator.DoesNotContainItem:
				return !Holds(Operator.ContainsItem, condition.Operand, value);
			default:
				return Holds(condition.Operator, condition.Operand, value);
		}
	}

	private static bool IsEmpty(FieldValue value) => value is FieldValue.Unset || (value is FieldValue.Text text && string.IsNullOrWhiteSpace(text.Value));

	private static bool Holds(Operator op, Operand operand, FieldValue value)
	{
		if (value is FieldValue.Unset)
		{
			return op == Operator.Is && operand is Operand.YesNo { Value: null };
		}
		switch (op)
		{
			case Operator.Is:
				return Equal(operand, value);
			case Operator.Contains:
				return operand is Operand.Text contains && TextOf(value).IndexOf(contains.Value, StringComparison.OrdinalIgnoreCase) >= 0;
			case Operator.StartsWith:
				return operand is Operand.Text prefix && TextOf(value).StartsWith(prefix.Value, StringComparison.OrdinalIgnoreCase);
			case Operator.MatchesRegex:
				return operand is Operand.Pattern pattern && SafeIsMatch(pattern.Value, TextOf(value));
			case Operator.ContainsItem:
				return operand is Operand.Text item && value is FieldValue.Items items
					&& items.Values.Any(candidate => string.Equals(candidate, item.Value.Trim(), StringComparison.OrdinalIgnoreCase));
			case Operator.GreaterThan:
				return Compare(operand, value) is > 0;
			case Operator.LessThan:
				return Compare(operand, value) is < 0;
			case Operator.Before:
				return operand is Operand.Date before && value is FieldValue.Date a && a.Value.Date < before.Value.Date;
			case Operator.After:
				return operand is Operand.Date after && value is FieldValue.Date b && b.Value.Date > after.Value.Date;
			case Operator.On:
				return operand is Operand.Date on && value is FieldValue.Date c && c.Value.Date == on.Value.Date;
			default:
				return false;
		}
	}

	private static string TextOf(FieldValue value) => value is FieldValue.Items items ? items.RawText : value.ToRawText();

	private static bool Equal(Operand operand, FieldValue value) => (operand, value) switch
	{
		(Operand.Text text, FieldValue.Text actual) => string.Equals(text.Value.Trim(), actual.Value.Trim(), StringComparison.OrdinalIgnoreCase),
		(Operand.Number number, FieldValue.Int actual) => number.Value == actual.Value,
		(Operand.Number number, FieldValue.Decimal actual) => number.Value == actual.Value,
		(Operand.Issue issue, FieldValue.Issue actual) => SameIssue(issue.Value, actual.Value),
		(Operand.YesNo { Value: { } wanted }, FieldValue.YesNo actual) => wanted == actual.Value,
		_ => false,
	};

	private static int? Compare(Operand operand, FieldValue value) => (operand, value) switch
	{
		(Operand.Number number, FieldValue.Int actual) => ((decimal)actual.Value).CompareTo(number.Value),
		(Operand.Number number, FieldValue.Decimal actual) => actual.Value.CompareTo(number.Value),
		(Operand.Issue issue, FieldValue.Issue actual) => SameIssue(issue.Value, actual.Value) ? 0 : actual.Value.CompareTo(issue.Value),
		_ => null,
	};

	private static bool SameIssue(IssueNumber a, IssueNumber b)
	{
		string left = a.Text.Trim();
		string right = b.Text.Trim();
		return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
			|| (decimal.TryParse(left, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal x)
				&& decimal.TryParse(right, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal y)
				&& x == y);
	}

	private static bool SafeIsMatch(Regex regex, string text)
	{
		try
		{
			return regex.IsMatch(text);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}
}

internal static class OperandParser
{
	private const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

	public static Operand? Parse(FieldKind kind, Operator op, string raw, out string? error)
	{
		error = null;
		if (!Operators.TakesOperand(op))
		{
			return new Operand.None();
		}
		string trimmed = raw.Trim();
		if (trimmed.Length == 0 && kind is not (FieldKind.YesNo or FieldKind.MangaYesNo))
		{
			error = "Enter a value.";
			return null;
		}
		if (op == Operator.MatchesRegex)
		{
			try
			{
				return new Operand.Pattern(new Regex(raw, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
			}
			catch (ArgumentException ex)
			{
				error = $"'{raw}' is not a valid pattern: {ex.Message}";
				return null;
			}
		}
		switch (kind)
		{
			case FieldKind.Int or FieldKind.Decimal or FieldKind.Month:
				if (decimal.TryParse(trimmed, NumberStyle, CultureInfo.InvariantCulture, out decimal number))
				{
					return new Operand.Number(number);
				}
				error = $"'{trimmed}' is not a number.";
				return null;
			case FieldKind.Issue:
				return new Operand.Issue(new IssueNumber(trimmed));
			case FieldKind.YesNo or FieldKind.MangaYesNo:
				if (TryParseYesNo(trimmed, out YesNoValue? yesNo) && (yesNo != YesNoValue.YesRightToLeft || kind == FieldKind.MangaYesNo))
				{
					return new Operand.YesNo(yesNo);
				}
				error = kind == FieldKind.MangaYesNo ? $"'{trimmed}' is not Yes, No, Yes (Right to Left) or Unknown." : $"'{trimmed}' is not Yes, No or Unknown.";
				return null;
			case FieldKind.Date:
				if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
				{
					return new Operand.Date(date.Date);
				}
				error = $"'{trimmed}' is not a date. Use yyyy-MM-dd.";
				return null;
			default:
				return new Operand.Text(raw);
		}
	}

	/// <summary>Accepts the member names and the 2.x label "Yes (Right to Left)". "Unknown" and empty text give null, meaning no value.</summary>
	public static bool TryParseYesNo(string text, out YesNoValue? value)
	{
		string key = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '(' && c != ')').ToArray());
		value = null;
		if (key.Length == 0 || key.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (key.Equals("Yes", StringComparison.OrdinalIgnoreCase))
		{
			value = YesNoValue.Yes;
			return true;
		}
		if (key.Equals("No", StringComparison.OrdinalIgnoreCase))
		{
			value = YesNoValue.No;
			return true;
		}
		if (key.Equals("YesAndRightToLeft", StringComparison.OrdinalIgnoreCase) || key.Equals("YesRightToLeft", StringComparison.OrdinalIgnoreCase))
		{
			value = YesNoValue.YesRightToLeft;
			return true;
		}
		return false;
	}

	/// <summary>The spelling settings.xml stores for a yes/no operand, or null when the text is not one.</summary>
	public static string? CanonicalYesNo(string text)
		=> !TryParseYesNo(text.Trim(), out YesNoValue? value) ? null : value switch
		{
			null => "Unknown",
			YesNoValue.Yes => "Yes",
			YesNoValue.No => "No",
			_ => "YesAndRightToLeft",
		};
}
