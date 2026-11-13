using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>
/// A snapshot value. "Unset" is its own case, so ComicRack's -1 / "" / Unknown / DateTime.MinValue never reach comparisons.
/// Closed: the private constructor stops new cases outside this file.
/// </summary>
public abstract record FieldValue
{
	private FieldValue() { }

	public sealed record Unset : FieldValue { public static readonly Unset Value = new(); }
	public sealed record Text(string Value) : FieldValue;
	public sealed record Int(int Value) : FieldValue;
	public sealed record Decimal(decimal Value) : FieldValue;
	public sealed record Issue(IssueNumber Value) : FieldValue;
	public sealed record YesNo(YesNoValue Value) : FieldValue;
	public sealed record Date(DateTime Value) : FieldValue;
	/// <summary>Trimmed, non-empty items in book order. Raw comma text is <see cref="RawText"/>.</summary>
	public sealed record Items(IReadOnlyList<string> Values, string RawText) : FieldValue;

	public bool IsUnset => this is Unset;

	/// <summary>Splits ComicRack's comma-separated text. Text with no items is <see cref="Unset"/>.</summary>
	public static FieldValue ParseItems(string? rawText)
	{
		string text = (rawText ?? "").Trim();
		string[] values = text.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
		return values.Length == 0 ? Unset.Value : new Items(values, text);
	}

	/// <summary>The uncleaned text a plain token writes and a test compares against.</summary>
	public string ToRawText() => this switch
	{
		Text text => text.Value,
		Int number => number.Value.ToString(CultureInfo.InvariantCulture),
		Decimal number => number.Value.ToString("0.##########", CultureInfo.InvariantCulture),
		Issue issue => issue.Value.ToString(),
		YesNo yesNo => yesNo.Value switch
		{
			YesNoValue.Yes => "Yes",
			YesNoValue.YesRightToLeft => "YesAndRightToLeft",
			_ => "No",
		},
		Date date => date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		Items items => items.RawText,
		_ => "",
	};
}

/// <summary>Both ComicRack enums folded into one; YesNo fields never hold YesRightToLeft.</summary>
public enum YesNoValue { No, Yes, YesRightToLeft }
