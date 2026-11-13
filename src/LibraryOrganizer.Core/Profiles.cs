using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>A profile's stable identity. Names are labels and can change; the journal and the last run order store ids.</summary>
public readonly record struct ProfileId(Guid Value);

public abstract record ProfileAction
{
	private ProfileAction() { }

	/// <summary>Moves the file. The last accepting Move profile in the run order wins.</summary>
	public sealed record Move : ProfileAction;

	/// <summary>Copies the file. Every accepting Copy profile makes its own copy.</summary>
	public sealed record Copy(bool AddToLibrary) : ProfileAction;
}

public enum CoverFormat { Jpg, Png, Bmp }

/// <summary>A rule tree in its stored form: field keys and raw operand text, valid or not.</summary>
public abstract record RuleSettings
{
	private RuleSettings() { }

	public sealed record Group(Combinator Combinator, IReadOnlyList<RuleSettings> Children) : RuleSettings
	{
		public bool Equals(Group? other) => other is not null && Combinator == other.Combinator && ValueEquality.SequenceEqual(Children, other.Children);

		public override int GetHashCode() => (Combinator, Children.Count).GetHashCode();
	}

	/// <param name="FieldKey">A <see cref="Field.Key"/>. Kept as text so a key this build does not know survives a save.</param>
	public sealed record Condition(string FieldKey, Operator Operator, string RawOperand) : RuleSettings;

	public static Group Empty => new(Combinator.Any, []);
}

/// <summary>
/// The editable, storable form of a profile: text templates, raw rule operands. The configure form binds to this.
/// Round-trips to settings.xml exactly, including invalid text the user has not fixed yet.
/// </summary>
public sealed record ProfileSettings
{
	private static readonly IReadOnlyDictionary<string, string> NoEntries = new Dictionary<string, string>();

	public ProfileId Id { get; init; } = new(Guid.NewGuid());
	public string Name { get; init; } = "";
	public ProfileAction Action { get; init; } = new ProfileAction.Move();

	public string FolderTemplate { get; init; } = PathDefaults.FolderTemplate;
	public string FileTemplate { get; init; } = PathDefaults.FileTemplate;
	public bool UseFolder { get; init; } = true;
	public bool UseFileName { get; init; } = true;
	public string BaseFolder { get; init; } = "";

	/// <summary>Name for a folder level that renders empty. Empty removes the level.</summary>
	public string EmptyFolderName { get; init; } = "";

	/// <summary>Substitute text for an empty field, keyed by <see cref="Field.Key"/>.</summary>
	public IReadOnlyDictionary<string, string> EmptySubstitutes { get; init; } = NoEntries;

	public bool CollapseSpaces { get; init; } = true;

	/// <summary>Character to replacement, in the order they apply.</summary>
	public IReadOnlyList<KeyValuePair<string, string>> IllegalCharacters { get; init; } = PathDefaults.IllegalCharacters;

	/// <summary>Index 0 is month 1. Months 13 to 16 are the seasons.</summary>
	public IReadOnlyList<string> MonthNames { get; init; } = PathDefaults.MonthNames;

	/// <summary>Folder paths as typed. A book under one is skipped.</summary>
	public IReadOnlyList<string> ExcludedFolders { get; init; } = [];

	public RuleSettings.Group Rules { get; init; } = RuleSettings.Empty;
	public RuleMode RuleMode { get; init; } = RuleMode.Except;

	/// <summary><see cref="Field.Key"/> values.</summary>
	public IReadOnlyList<string> RequiredFields { get; init; } = [];

	/// <summary>Empty skips a book that fails the required fields.</summary>
	public string FailedBooksFolder { get; init; } = "";

	/// <summary>Null skips fileless books.</summary>
	public CoverFormat? FilelessCovers { get; init; }

	public bool PruneEmptyFolders { get; init; } = true;
	public IReadOnlyList<string> KeepFolders { get; init; } = [];
	public bool DontAskWhenOneValue { get; init; } = true;
	public bool KeepReadProgressOnReplace { get; init; } = true;
	public bool AutoSpaceFields { get; init; } = true;

	/// <summary>The last text typed into each insert-field control, keyed by <see cref="Field.Key"/>.</summary>
	public IReadOnlyDictionary<string, string> LastPrefix { get; init; } = NoEntries;
	public IReadOnlyDictionary<string, string> LastPostfix { get; init; } = NoEntries;
	public IReadOnlyDictionary<string, string> LastSeparator { get; init; } = NoEntries;

	/// <summary>
	/// The one mapping from settings to engine types: parses the templates the profile uses, every rule and every path.
	/// A template or base folder the Use flags switch off is not read.
	/// </summary>
	public CompileResult Compile() => ProfileCompiler.Compile(this);

	public bool Equals(ProfileSettings? other)
		=> other is not null
			&& Id == other.Id
			&& Name == other.Name
			&& Action.Equals(other.Action)
			&& FolderTemplate == other.FolderTemplate
			&& FileTemplate == other.FileTemplate
			&& UseFolder == other.UseFolder
			&& UseFileName == other.UseFileName
			&& BaseFolder == other.BaseFolder
			&& EmptyFolderName == other.EmptyFolderName
			&& ValueEquality.DictionaryEqual(EmptySubstitutes, other.EmptySubstitutes)
			&& CollapseSpaces == other.CollapseSpaces
			&& ValueEquality.SequenceEqual(IllegalCharacters, other.IllegalCharacters)
			&& ValueEquality.SequenceEqual(MonthNames, other.MonthNames)
			&& ValueEquality.SequenceEqual(ExcludedFolders, other.ExcludedFolders)
			&& Rules.Equals(other.Rules)
			&& RuleMode == other.RuleMode
			&& ValueEquality.SequenceEqual(RequiredFields, other.RequiredFields)
			&& FailedBooksFolder == other.FailedBooksFolder
			&& FilelessCovers == other.FilelessCovers
			&& PruneEmptyFolders == other.PruneEmptyFolders
			&& ValueEquality.SequenceEqual(KeepFolders, other.KeepFolders)
			&& DontAskWhenOneValue == other.DontAskWhenOneValue
			&& KeepReadProgressOnReplace == other.KeepReadProgressOnReplace
			&& AutoSpaceFields == other.AutoSpaceFields
			&& ValueEquality.DictionaryEqual(LastPrefix, other.LastPrefix)
			&& ValueEquality.DictionaryEqual(LastPostfix, other.LastPostfix)
			&& ValueEquality.DictionaryEqual(LastSeparator, other.LastSeparator);

	public override int GetHashCode() => (Id, Name, FolderTemplate, FileTemplate).GetHashCode();
}

/// <summary>What "Organize" does when a target already exists, applied until the user changes it in the review grid.</summary>
public enum ConflictDefault { Ask, Skip, Replace, KeepBoth }

public static class ConflictDefaults
{
	/// <summary>The resolution a plan applies to every conflict that allows it. Ask leaves conflicts open.</summary>
	public static Resolution? ToResolution(this ConflictDefault choice) => choice switch
	{
		ConflictDefault.Skip => new Resolution.Skip(),
		ConflictDefault.Replace => new Resolution.Replace(),
		ConflictDefault.KeepBoth => new Resolution.KeepBoth(),
		_ => null,
	};
}

public enum ReviewWhen
{
	Always,
	/// <summary>Only when the plan has questions or conflicts, or any entry failed.</summary>
	OnlyWhenNeeded,
}

/// <summary>Plugin-wide choices, not per profile.</summary>
/// <param name="KeepRuns">How many past run journals to keep for undo.</param>
public sealed record RunPreferences(ConflictDefault DefaultResolution, ReviewWhen ReviewWhen, int KeepRuns)
{
	public static readonly RunPreferences Default = new(ConflictDefault.Ask, ReviewWhen.Always, 20);
}

/// <summary>All profiles plus the last run order. Immutable; the store swaps whole values.</summary>
public sealed record Settings(IReadOnlyList<ProfileSettings> Profiles, IReadOnlyList<ProfileId> LastRunOrder, RunPreferences Preferences)
{
	/// <summary>One "Default" profile with the 2.x default templates.</summary>
	public static Settings CreateDefault()
		=> new([new ProfileSettings { Name = "Default" }], [], RunPreferences.Default);
}

internal static class ValueEquality
{
	public static bool SequenceEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) => a.SequenceEqual(b);

	public static bool DictionaryEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
		=> a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? value) && value == pair.Value);
}
