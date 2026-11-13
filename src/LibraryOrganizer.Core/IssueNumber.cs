using System;
using System.Globalization;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>
/// An issue number with natural ordering and 2.x padding rules. Replaces string compares ("10" &lt; "9") and int-only parsing
/// ("12.1" never last). Parse never fails: "12a" orders just after "12", and text with no leading number orders after every number.
/// </summary>
public readonly record struct IssueNumber(string Text) : IComparable<IssueNumber>
{
	/// <summary>True when the text starts with a number, so the issue has a place in the numeric order.</summary>
	internal bool IsNumeric => TryReadNumber(out _, out _);

	public int CompareTo(IssueNumber other)
	{
		bool numeric = TryReadNumber(out decimal number, out string suffix);
		bool otherNumeric = other.TryReadNumber(out decimal otherNumber, out string otherSuffix);
		if (numeric != otherNumeric)
		{
			return numeric ? -1 : 1;
		}
		int result = number.CompareTo(otherNumber);
		if (result == 0)
		{
			result = StringComparer.OrdinalIgnoreCase.Compare(suffix, otherSuffix);
		}
		return result != 0 ? result : string.CompareOrdinal(Text ?? "", other.Text ?? "");
	}

	/// <summary>
	/// 2.x pad: left-pad the integer part to width, keep the remainder, keep a leading '-'. Non-numeric ("½", "7b") unchanged.
	/// 7/3 -> "007", 7.1/3 -> "007.1", -1.5/3 -> "-001.5", 123/2 -> "123".
	/// </summary>
	public string Pad(int width)
	{
		string text = Text ?? "";
		int start = text.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
		int dot = text.IndexOf('.', start);
		string whole = dot < 0 ? text.Substring(start) : text.Substring(start, dot - start);
		string fraction = dot < 0 ? "" : text.Substring(dot + 1);
		if (whole.Length == 0 || !whole.All(IsDigit) || !fraction.All(IsDigit))
		{
			return text;
		}
		return text.Substring(0, start) + whole.PadLeft(width, '0') + text.Substring(start + whole.Length);
	}

	public override string ToString() => Text ?? "";

	private bool TryReadNumber(out decimal number, out string suffix)
	{
		string text = (Text ?? "").Trim();
		bool negative = text.StartsWith("-", StringComparison.Ordinal);
		int end = negative ? 1 : 0;
		int digitsStart = end;
		while (end < text.Length && IsDigit(text[end]))
		{
			end++;
		}
		bool found = end > digitsStart;
		if (found && end + 1 < text.Length && text[end] == '.' && IsDigit(text[end + 1]))
		{
			end++;
			while (end < text.Length && IsDigit(text[end]))
			{
				end++;
			}
		}
		number = 0;
		if (found && !decimal.TryParse(text.Substring(0, end), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
		{
			found = false;
		}
		else if (end < text.Length && char.GetNumericValue(text[end]) is > 0 and < 1 and double vulgarFraction)
		{
			number += negative ? -(decimal)vulgarFraction : (decimal)vulgarFraction;
			end++;
			found = true;
		}
		suffix = found ? text.Substring(end) : text;
		return found;
	}

	private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
