using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>
/// A normalized absolute Windows path. Equality is ordinal-ignore-case, the way NTFS compares names.
/// Every path comparison in the engine (conflicts, excluded folders, prune roots, undo) goes through this type.
/// </summary>
public readonly record struct AbsPath
{
	private const string Separator = "\\";
	private const string UncPrefix = @"\\";

	private AbsPath(string value) => Value = value;

	public string Value { get; }

	/// <summary>
	/// Boundary: rejects relative paths and invalid characters. Normalizes separators, the drive letter's case, "." and ".."
	/// segments, and strips a trailing '\'. A drive root keeps its separator ("D:\").
	/// </summary>
	public static AbsPath? TryParse(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw!.Trim().Replace('/', '\\');
		string root;
		string[] rest;
		if (text.Length >= 3 && IsDriveLetter(text[0]) && text[1] == ':' && text[2] == '\\')
		{
			root = char.ToUpperInvariant(text[0]) + ":";
			rest = text.Substring(3).Split('\\');
		}
		else if (text.StartsWith(UncPrefix, StringComparison.Ordinal))
		{
			string[] parts = text.Substring(UncPrefix.Length).Split('\\');
			if (parts.Length < 2 || !IsValidName(parts[0]) || !IsValidName(parts[1]))
			{
				return null;
			}
			root = UncPrefix + parts[0] + Separator + parts[1];
			rest = parts.Skip(2).ToArray();
		}
		else
		{
			return null;
		}

		var segments = new List<string> { root };
		foreach (string segment in rest)
		{
			if (segment.Length == 0 || segment == ".")
			{
				continue;
			}
			if (segment == "..")
			{
				if (segments.Count == 1)
				{
					return null;
				}
				segments.RemoveAt(segments.Count - 1);
			}
			else if (IsValidName(segment))
			{
				segments.Add(segment);
			}
			else
			{
				return null;
			}
		}
		return new AbsPath(segments.Count == 1 && root.Length == 2 ? root + Separator : string.Join(Separator, segments));
	}

	/// <summary>True for a drive root ("D:\") or a share root ("\\server\share").</summary>
	public bool IsRoot => Value.Length <= RootLength;

	/// <summary>The containing folder. A root is its own parent.</summary>
	public AbsPath Parent => IsRoot ? this : new AbsPath(Value.Substring(0, Math.Max(Value.LastIndexOf('\\'), RootLength)));

	/// <summary>The last segment. Empty for a root.</summary>
	public string Name => IsRoot ? "" : Value.Substring(Value.LastIndexOf('\\') + 1);

	/// <summary>The extension of <see cref="Name"/> with its dot (".cbz"), or empty.</summary>
	public string Extension
	{
		get
		{
			string name = Name;
			int dot = name.LastIndexOf('.');
			return dot < 0 || dot == name.Length - 1 ? "" : name.Substring(dot);
		}
	}

	/// <summary>Appends one segment. Throws for a name that is empty or holds a separator or another character Windows forbids.</summary>
	public AbsPath Combine(string childName)
	{
		if (!IsValidName(childName) || childName == "." || childName == "..")
		{
			throw new ArgumentException($"'{childName}' is not a single valid file or folder name.", nameof(childName));
		}
		return new AbsPath(Value.EndsWith(Separator, StringComparison.Ordinal) ? Value + childName : Value + Separator + childName);
	}

	/// <summary>True when this is <paramref name="folder"/> or below it, by whole segments ("D:\Manga" does not contain "D:\Manga Extra").</summary>
	public bool IsUnder(AbsPath folder)
	{
		if (Equals(folder))
		{
			return true;
		}
		string prefix = folder.Value.EndsWith(Separator, StringComparison.Ordinal) ? folder.Value : folder.Value + Separator;
		return Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
	}

	public bool DiffersOnlyByCase(AbsPath other) => Equals(other) && !string.Equals(Value, other.Value, StringComparison.Ordinal);

	public bool Equals(AbsPath other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
	public override int GetHashCode() => Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
	public override string ToString() => Value ?? "";

	private int RootLength
	{
		get
		{
			if (!Value.StartsWith(UncPrefix, StringComparison.Ordinal))
			{
				return 3;
			}
			int afterServer = Value.IndexOf('\\', UncPrefix.Length);
			int afterShare = Value.IndexOf('\\', afterServer + 1);
			return afterShare < 0 ? Value.Length : afterShare;
		}
	}

	private static bool IsDriveLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

	private static bool IsValidName(string name) => name.Length > 0 && !name.Any(c => c < ' ' || "<>:\"/\\|?*".IndexOf(c) >= 0);
}
