using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>How a number is padded in a token: not at all, as wide as the series' last issue (2.x width 0), or to a fixed width.</summary>
internal readonly record struct PadSpec(int? Width)
{
	public static readonly PadSpec None = new(null);
	public static readonly PadSpec Auto = new(0);

	public static PadSpec Fixed(int width) => new(width);
}

/// <summary>What the insert-field controls hold. Only the inputs that suit the field's kind are read.</summary>
internal sealed record TokenInputs
{
	public string Prefix { get; init; } = "";
	public string Postfix { get; init; } = "";
	public bool AutoSpace { get; init; } = true;
	public PadSpec Pad { get; init; } = PadSpec.None;
	public bool MonthAsNumber { get; init; }
	public string Separator { get; init; } = ", ";
	public AnswerScopeKind Scope { get; init; } = AnswerScopeKind.Issue;
	public string YesNoText { get; init; } = "";
	public bool InvertYesNo { get; init; }
	public string ReadText { get; init; } = "";
	public char ReadOperator { get; init; } = '>';
	public int ReadPercent { get; init; } = 90;
	public int CounterStart { get; init; } = 1;
	public int CounterStep { get; init; } = 1;
	public int CounterPad { get; init; }
	public string DateFormat { get; init; } = "";
	public string CustomKey { get; init; } = "";
	public string FirstLetterSource { get; init; } = Fields.Series.DisplayName;
}

/// <summary>
/// Builds the template text for one field: <c>{</c> + optional space + prefix + <c>&lt;name args&gt;</c> + postfix + <c>}</c>.
/// The space sits inside the braces, so it appears only when the field has a value.
/// </summary>
internal static class TokenBuilder
{
	public static string Build(Field field, TokenInputs inputs)
	{
		string token = "<" + field.TemplateName + Arguments(field, inputs) + ">";
		return "{" + (inputs.AutoSpace ? " " : "") + inputs.Prefix + token + inputs.Postfix + "}";
	}

	private static string Arguments(Field field, TokenInputs i)
	{
		if (field == Fields.ReadPercentage)
		{
			return i.ReadText.Length == 0
				? ""
				: Paren(i.ReadText) + Paren(i.ReadOperator.ToString()) + Paren(i.ReadPercent.ToString(CultureInfo.InvariantCulture));
		}
		if (field == Fields.Counter)
		{
			return Paren(i.CounterStart.ToString(CultureInfo.InvariantCulture))
				+ Paren(i.CounterStep.ToString(CultureInfo.InvariantCulture))
				+ Paren(i.CounterPad.ToString(CultureInfo.InvariantCulture));
		}
		if (field == Fields.Custom)
		{
			return Paren(i.CustomKey);
		}
		if (field == Fields.FirstLetter)
		{
			return Paren(i.FirstLetterSource);
		}
		return field.Kind switch
		{
			FieldKind.Int or FieldKind.Issue => Digits(i.Pad),
			FieldKind.Month => i.MonthAsNumber ? "#" + Digits(i.Pad) : "",
			FieldKind.Items => Paren(i.Separator) + Paren(i.Scope == AnswerScopeKind.Series ? "series" : "issue"),
			FieldKind.YesNo or FieldKind.MangaYesNo => i.YesNoText.Length == 0 ? "" : Paren(i.YesNoText) + (i.InvertYesNo ? Paren("!") : ""),
			FieldKind.Date => i.DateFormat.Length == 0 ? "" : Paren(i.DateFormat),
			_ => "",
		};
	}

	private static string Digits(PadSpec pad) => pad.Width?.ToString(CultureInfo.InvariantCulture) ?? "";

	private static string Paren(string text) => new StringBuilder("(").Append(text).Append(')').ToString();

	/// <summary>The date formats the picker offers. Slashes are left out: path cleaning would strip them.</summary>
	public static IReadOnlyList<string> DateFormats { get; } =
		["", "yyyy-MM-dd", "yyyy-MM", "yyyy", "MMM d, yyyy", "MMM dd, yyyy", "MMMM d, yyyy", "MMMM dd, yyyy", "dd-MM-yy", "MM-dd-yy", "yyyyMMdd"];
}
