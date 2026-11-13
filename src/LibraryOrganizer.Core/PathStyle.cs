using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LibraryOrganizer;

/// <summary>Profile-level text policy, and the one place rendered values become path text.</summary>
/// <param name="IllegalCharacters">Text to replace and its replacement. The longest key wins where two overlap.</param>
/// <param name="CollapseSpaces">Replace each run of whitespace with one space.</param>
/// <param name="EmptyFolderName">Name for a folder level that renders blank. Blank drops the level.</param>
/// <param name="EmptySubstitutes">Text a token writes when its field is empty.</param>
/// <param name="MonthNames">Index 0 is month 1.</param>
public sealed record PathStyle(
	IReadOnlyList<KeyValuePair<string, string>> IllegalCharacters,
	bool CollapseSpaces,
	string EmptyFolderName,
	IReadOnlyDictionary<Field, string> EmptySubstitutes,
	IReadOnlyList<string> MonthNames)
{
	private static readonly Regex WhitespaceRun = new(@"\s{2,}", RegexOptions.CultureInvariant);

	private static readonly HashSet<string> ReservedNames = new(
		new[] { "CON", "PRN", "AUX", "NUL" }
			.Concat(Enumerable.Range(1, 9).SelectMany(n => new[] { "COM" + n, "LPT" + n })),
		StringComparer.OrdinalIgnoreCase);

	public static PathStyle Default { get; } = new(
		PathDefaults.IllegalCharacters, CollapseSpaces: true, EmptyFolderName: "", new Dictionary<Field, string>(), PathDefaults.MonthNames);

	/// <summary>One cleaned name per folder level. Only a Break starts a level; a level that stays blank is dropped.</summary>
	internal IReadOnlyList<string> CleanFolder(IReadOnlyList<Piece> pieces)
	{
		var levels = new List<string>();
		var level = new List<Piece>();
		foreach (Piece piece in pieces.Append(new Piece.Break()))
		{
			if (piece is not Piece.Break)
			{
				level.Add(piece);
				continue;
			}
			string name = CleanName(level);
			if (name.Length == 0)
			{
				name = CleanName([new Piece.Text(EmptyFolderName)]);
			}
			if (name.Length > 0)
			{
				levels.Add(name);
			}
			level.Clear();
		}
		return levels;
	}

	/// <summary>The cleaned file name without its extension. Breaks vanish. Blank stays blank, for the planner to fail.</summary>
	internal string CleanFileName(IReadOnlyList<Piece> pieces) => CleanName(pieces.Where(piece => piece is not Piece.Break));

	private string CleanName(IEnumerable<Piece> pieces)
	{
		var name = new StringBuilder();
		var text = new StringBuilder();
		foreach (Piece piece in pieces)
		{
			if (piece is Piece.Text raw)
			{
				text.Append(raw.Raw);
			}
			else if (piece is Piece.Placeholder placeholder)
			{
				name.Append(ReplaceIllegal(text.ToString())).Append('‹').Append(placeholder.Name).Append("?›");
				text.Clear();
			}
		}
		name.Append(ReplaceIllegal(text.ToString()));

		string cleaned = CollapseSpaces ? WhitespaceRun.Replace(name.ToString(), " ") : name.ToString();
		int start = 0;
		int end = cleaned.Length;
		while (start < end && IsTrimmed(cleaned[start]))
		{
			start++;
		}
		while (end > start && IsTrimmed(cleaned[end - 1]))
		{
			end--;
		}
		return GuardReserved(cleaned.Substring(start, end - start));
	}

	private string ReplaceIllegal(string text)
	{
		List<KeyValuePair<string, string>> table = IllegalCharacters
			.Where(entry => entry.Key.Length > 0)
			.OrderByDescending(entry => entry.Key.Length)
			.ToList();
		var replaced = new StringBuilder(text.Length);
		int at = 0;
		while (at < text.Length)
		{
			int match = table.FindIndex(entry => string.CompareOrdinal(text, at, entry.Key, 0, entry.Key.Length) == 0);
			if (match < 0)
			{
				replaced.Append(text[at++]);
			}
			else
			{
				replaced.Append(table[match].Value);
				at += table[match].Key.Length;
			}
		}
		for (int i = replaced.Length - 1; i >= 0; i--)
		{
			if (replaced[i] < ' ' || "<>:\"/\\|?*".IndexOf(replaced[i]) >= 0)
			{
				replaced.Remove(i, 1);
			}
		}
		return replaced.ToString();
	}

	/// <summary>Windows treats "NUL" and "NUL.anything" as a device, so the '_' goes before the first dot.</summary>
	private static string GuardReserved(string name)
	{
		int dot = name.IndexOf('.');
		int stemLength = dot < 0 ? name.Length : dot;
		return ReservedNames.Contains(name.Substring(0, stemLength).TrimEnd()) ? name.Insert(stemLength, "_") : name;
	}

	private static bool IsTrimmed(char c) => c == '.' || char.IsWhiteSpace(c);
}
