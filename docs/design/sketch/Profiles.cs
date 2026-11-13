using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace LibraryOrganizer;

/// <summary>A profile's stable identity. Names are labels and can change; the journal and LastUsed store ids.</summary>
public readonly record struct ProfileId(Guid Value);

/// <summary>What a profile does with a book it accepts. Simulate is gone: every run is reviewed before it executes.</summary>
public abstract record ProfileAction
{
	private ProfileAction() { }

	/// <summary>Moves the file. The last accepting Move profile in the run order wins.</summary>
	public sealed record Move : ProfileAction;

	/// <summary>Copies the file. Every accepting Copy profile makes its own copy.</summary>
	public sealed record Copy(bool AddToLibrary) : ProfileAction;
}

/// <summary>Which part of the path a profile renders. "Neither" cannot be constructed.</summary>
public abstract record Destination
{
	private Destination() { }

	public sealed record FolderAndName(AbsPath BaseFolder, Template Folder, Template FileName) : Destination;
	/// <summary>Keeps the current file name.</summary>
	public sealed record FolderOnly(AbsPath BaseFolder, Template Folder) : Destination;
	/// <summary>Keeps the current folder. No base folder needed.</summary>
	public sealed record NameOnly(Template FileName) : Destination;
}

/// <summary>
/// Fields that must have a value, checked on the book directly before rendering (2.x checked only fields a template rendered,
/// and counted an EmptyData substitute as a failure). The route is part of the type, so "move failed books" always has a folder.
/// </summary>
public sealed record RequiredFields(IReadOnlyList<Field> Fields, RequiredFields.FailRoute Route)
{
	public abstract record FailRoute
	{
		private FailRoute() { }
		public sealed record Skip : FailRoute;
		/// <summary>Render the rest of the path as normal under this root, and report the book as failed.</summary>
		public sealed record MoveUnder(AbsPath Folder) : FailRoute;
	}
}

/// <summary>Empty-folder removal after moves. Stops at library roots and base folders, never climbs above them.</summary>
public sealed record FolderPruning(IReadOnlyList<AbsPath> Keep, bool DeleteJunkFiles);

/// <summary>
/// A compiled, valid profile. The engine accepts only this type. Built by <see cref="ProfileSettings.Compile"/>.
/// </summary>
/// <param name="FilelessCovers">Null skips fileless books. Otherwise their custom cover is written as an image in this format.</param>
/// <param name="Pruning">Null keeps empty folders.</param>
public sealed record Profile(
	ProfileId Id,
	string Name,
	ProfileAction Action,
	Destination Destination,
	Filter Filter,
	RequiredFields? Required,
	PathStyle Style,
	CoverFormat? FilelessCovers,
	FolderPruning? Pruning,
	bool DontAskWhenOneValue,
	bool KeepReadProgressOnReplace)
{
	/// <summary>Every template this profile renders, for Organizer.Demand.</summary>
	internal IEnumerable<Template> Templates => throw new NotImplementedException();
}

public enum CoverFormat { Jpg, Png, Bmp }

/// <summary>
/// The editable, storable form of a profile: text templates, raw rule operands. The configure form binds to this.
/// Round-trips to settings.xml exactly, including invalid text the user has not fixed yet.
/// </summary>
public sealed record ProfileSettings
{
	public ProfileId Id { get; init; }
	public string Name { get; init; } = "";
	public string FolderTemplate { get; init; } = "";
	public string FileTemplate { get; init; } = "";
	public bool UseFolder { get; init; } = true;
	public bool UseFileName { get; init; } = true;
	public string BaseFolder { get; init; } = "";
	// ... one property per Profile setting, plus the UI-only conveniences (last prefix/postfix per field control, keyed by Field.Key).

	/// <summary>
	/// The one mapping from settings to engine types. Parses both templates, every rule, every path.
	/// Errors carry a location the form can focus: (page, control, template span).
	/// </summary>
	public CompileResult Compile() => throw new NotImplementedException();
}

public sealed record CompileResult(Profile? Profile, IReadOnlyList<SettingsError> Errors);

public sealed record SettingsError(string Where, string Message, TemplateError? Span = null);

/// <summary>All profiles plus the last run order. Immutable; the store swaps whole values.</summary>
public sealed record Settings(IReadOnlyList<ProfileSettings> Profiles, IReadOnlyList<ProfileId> LastRunOrder, RunPreferences Preferences);

/// <summary>Plugin-wide choices, not per profile.</summary>
/// <param name="DefaultResolution">Applied to every open conflict until the user changes it in the review grid. Null leaves them open.</param>
/// <param name="ReviewWhen">When "Organize now" shows the review grid instead of executing at once.</param>
/// <param name="KeepRuns">How many past run journals to keep for undo.</param>
public sealed record RunPreferences(Resolution? DefaultResolution, ReviewWhen ReviewWhen, int KeepRuns);

public enum ReviewWhen
{
	Always,
	/// <summary>Only when the plan has questions or conflicts, or any entry failed.</summary>
	OnlyWhenNeeded,
}

/// <summary>
/// settings.xml in the data folder. Single owner of the file: load, save atomically (temp file + File.Replace).
/// Root: &lt;LibraryOrganizer schema="3"&gt;. Profiles are &lt;Profile id=".." name=".."&gt; with one element per setting,
/// rules as nested &lt;Group combinator=".."&gt;/&lt;Condition field="key" op=".." value=".."/&gt;, fields by Field.Key.
/// </summary>
public sealed class SettingsStore
{
	public SettingsStore(string dataFolder) => throw new NotImplementedException();

	public string Path { get; }

	/// <summary>Missing file -> one "Default" profile with the 2.x default templates. A corrupt file is renamed aside and reported, never overwritten silently.</summary>
	public SettingsLoad Load() => throw new NotImplementedException();

	public void Save(Settings settings) => throw new NotImplementedException();
}

public sealed record SettingsLoad(Settings Settings, IReadOnlyList<string> Warnings);

/// <summary>
/// 2.x and 1.7 import: losettingsx.dat and .lop files (roots Profiles, Profile, Settings, Setting).
/// Pure: XDocument in, profiles and warnings out. Never writes. The configure form merges the result and asks about name clashes.
/// </summary>
public static class LegacyImport
{
	/// <summary>
	/// TODO: ordered migrations on the XML before building typed objects, one per version step:
	///   1.x: Mode Test -> Simulate; EmptyData display names -> property names; UI keys camel case -> "Start Year".
	///   2.x -> 3: Mode Simulate -> Move (warning: "review replaces Simulate"); ExcludeMode "Do not" -> Except;
	///             property/display names -> Field via Fields.ByLegacyName ("Released Date" now works);
	///             rule Value or Text attribute; root ExcludeOperator/ExcludeMode read from the attributes (they win in 2.x);
	///             dict elements MERGE into defaults, list elements REPLACE only when non-empty (2.x load rules);
	///             templates are parsed: each error becomes a warning with the profile name and span, the text is kept;
	///             text rules: warn when case-insensitive compare could change the result;
	///             new ProfileId per profile; LastUsed names -> ids.
	/// </summary>
	public static ImportResult Read(XDocument document) => throw new NotImplementedException();

	/// <summary>The 2.x install's settings file next to this plugin, if any, for a first-run offer to import.</summary>
	public static string? FindInstalled2x(string pluginFolder) => throw new NotImplementedException();
}

public sealed record ImportResult(IReadOnlyList<ProfileSettings> Profiles, IReadOnlyList<ProfileId> LastUsed, IReadOnlyList<string> Warnings);
