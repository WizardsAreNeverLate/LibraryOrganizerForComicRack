using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

public enum FieldKind
{
	Text,
	/// <summary>An integer where ComicRack's -1 means unset (Count, Volume, Year, Day, AlternateCount).</summary>
	Int,
	/// <summary>A float such as Rating or CommunityRating. 0 and -1 snapshot as Unset.</summary>
	Decimal,
	/// <summary>A string issue number with natural ordering ("7", "7.1", "12a", "½").</summary>
	Issue,
	/// <summary>Month 1-12 (and 13-16 seasons in names). Renders as a name by default, a number with '#'.</summary>
	Month,
	YesNo,
	/// <summary>Manga's YesNo plus YesAndRightToLeft.</summary>
	MangaYesNo,
	Date,
	/// <summary>Comma-separated list (Writer, Characters...). Supports per-issue and per-series selection.</summary>
	Items,
	/// <summary>Has no value of its own; the format supplies it (counter, first letter, custom).</summary>
	Synthetic,
}

public enum FieldSource
{
	Book,
	/// <summary>Computed from the book's series in the SeriesIndex (start year, last issue number...).</summary>
	Series,
	/// <summary>Computed by the run itself (counter).</summary>
	Run,
}

/// <summary>UI grouping for the insert-field tabs and the rule editor. Order of the enum is tab order.</summary>
public enum FieldGroup { Text, Number, YesNo, Items, Calculated }

[Flags]
public enum FieldUses
{
	None = 0,
	Template = 1,
	Rule = 2,
	/// <summary>In the required-field and empty-substitute pickers.</summary>
	Required = 4,
}

internal enum SeriesEnd { First, Last }

/// <summary>
/// One comic attribute. The single source for templates, rules, required fields, empty substitutes, UI field lists,
/// the host snapshot binding and the 2.x import name maps. Instances exist only as static members of <see cref="Fields"/>.
/// </summary>
public sealed class Field
{
	internal Field(
		int ordinal, string key, string? templateName, string displayName, FieldKind kind, FieldSource source, FieldGroup group,
		FieldUses uses, string? hostProperty, IReadOnlyList<string> legacyNames, Field? derivedFrom, SeriesEnd? seriesEnd)
	{
		Ordinal = ordinal;
		Key = key;
		TemplateName = templateName;
		DisplayName = displayName;
		Kind = kind;
		Source = source;
		Group = group;
		Uses = uses;
		HostProperty = hostProperty;
		LegacyNames = legacyNames;
		DerivedFrom = derivedFrom;
		SeriesEnd = seriesEnd;
	}

	/// <summary>Dense index into <see cref="BookRecord"/> values. Assigned by registry order.</summary>
	public int Ordinal { get; }

	/// <summary>Stable key in settings.xml. Never renamed once shipped.</summary>
	public string Key { get; }

	/// <summary>The name inside &lt;...&gt; (case-sensitive, as in 2.x): "series", "number", "startyear", "EndYear". Null for a rules-only field.</summary>
	public string? TemplateName { get; }

	public string DisplayName { get; }
	public FieldKind Kind { get; }
	public FieldSource Source { get; }
	public FieldGroup Group { get; }
	public FieldUses Uses { get; }

	/// <summary>ComicBook property the plugin snapshots, e.g. "ShadowSeries". Null for Series, Run and Synthetic fields.</summary>
	public string? HostProperty { get; }

	/// <summary>2.x names that import maps from: the property name ("ShadowSeries", used in EmptyData/FailedFields) and display name ("Series", used in rules).</summary>
	public IReadOnlyList<string> LegacyNames { get; }

	/// <summary>For Series fields: the book field it is derived from (StartYear -> Year, LastIssueNumber -> Number).</summary>
	public Field? DerivedFrom { get; }

	/// <summary>For Series fields: which issue of the series supplies <see cref="DerivedFrom"/>.</summary>
	internal SeriesEnd? SeriesEnd { get; }

	public override string ToString() => Key;
}

/// <summary>The registry. Adding a field is one line here; nothing else lists fields.</summary>
public static class Fields
{
	private const FieldUses Everywhere = FieldUses.Template | FieldUses.Rule | FieldUses.Required;
	private const FieldUses TemplateAndRule = FieldUses.Template | FieldUses.Rule;

	private static readonly List<Field> Registry = [];

	public static readonly Field Series = Book("series", "series", "Series", FieldKind.Text, FieldGroup.Text, Everywhere, "ShadowSeries");
	public static readonly Field Title = Book("title", "title", "Title", FieldKind.Text, FieldGroup.Text, Everywhere, "ShadowTitle");
	public static readonly Field Format = Book("format", "format", "Format", FieldKind.Text, FieldGroup.Text, Everywhere, "ShadowFormat");
	public static readonly Field Publisher = Book("publisher", "publisher", "Publisher", FieldKind.Text, FieldGroup.Text, Everywhere, "Publisher");
	public static readonly Field Imprint = Book("imprint", "imprint", "Imprint", FieldKind.Text, FieldGroup.Text, Everywhere, "Imprint");
	public static readonly Field AgeRating = Book("ageRating", "ageRating", "Age Rating", FieldKind.Text, FieldGroup.Text, Everywhere, "AgeRating");
	// 2.x rules read LanguageISO while templates read LanguageAsText. Both names import to this one field.
	public static readonly Field Language = Book("language", "language", "Language", FieldKind.Text, FieldGroup.Text, Everywhere, "LanguageAsText", "LanguageISO");
	public static readonly Field StoryArc = Book("storyArc", "storyarc", "Story Arc", FieldKind.Text, FieldGroup.Text, Everywhere, "StoryArc");
	public static readonly Field SeriesGroup = Book("seriesGroup", "seriesgroup", "Series Group", FieldKind.Text, FieldGroup.Text, Everywhere, "SeriesGroup");
	public static readonly Field MainCharacterOrTeam = Book("mainCharacterOrTeam", "maincharacter", "Main Character Or Team", FieldKind.Text, FieldGroup.Text, Everywhere, "MainCharacterOrTeam");
	public static readonly Field Notes = Book("notes", null, "Notes", FieldKind.Text, FieldGroup.Text, FieldUses.Rule, "Notes");
	public static readonly Field Review = Book("review", null, "Review", FieldKind.Text, FieldGroup.Text, FieldUses.Rule, "Review");
	public static readonly Field Web = Book("web", null, "Web", FieldKind.Text, FieldGroup.Text, FieldUses.Rule, "Web");
	public static readonly Field FilePath = Book("filePath", null, "File Path", FieldKind.Text, FieldGroup.Text, FieldUses.Rule, "FilePath");
	public static readonly Field FileName = Book("fileName", null, "File Name", FieldKind.Text, FieldGroup.Text, FieldUses.Rule, "FileName");
	public static readonly Field FileFormat = Book("fileFormat", null, "File Format", FieldKind.Text, FieldGroup.Text, FieldUses.Rule, "FileFormat");
	public static readonly Field Custom = Synthetic("custom", "Custom", "Custom", FieldSource.Book, FieldGroup.Text);

	public static readonly Field Number = Book("number", "number", "Number", FieldKind.Issue, FieldGroup.Number, Everywhere, "ShadowNumber");
	public static readonly Field Count = Book("count", "count", "Count", FieldKind.Int, FieldGroup.Number, Everywhere, "ShadowCount");
	public static readonly Field Volume = Book("volume", "volume", "Volume", FieldKind.Int, FieldGroup.Number, Everywhere, "ShadowVolume");
	public static readonly Field Year = Book("year", "year", "Year", FieldKind.Int, FieldGroup.Number, Everywhere, "ShadowYear");
	public static readonly Field Month = Book("month", "month", "Month", FieldKind.Month, FieldGroup.Number, Everywhere, "Month");
	public static readonly Field Day = Book("day", "Day", "Day", FieldKind.Int, FieldGroup.Number, Everywhere, "Day");
	public static readonly Field AlternateNumber = Book("alternateNumber", "altNumber", "Alternate Number", FieldKind.Issue, FieldGroup.Number, Everywhere, "AlternateNumber");
	public static readonly Field AlternateCount = Book("alternateCount", "altCount", "Alternate Count", FieldKind.Int, FieldGroup.Number, Everywhere, "AlternateCount");
	public static readonly Field Rating = Book("rating", "Rating", "Rating", FieldKind.Decimal, FieldGroup.Number, Everywhere, "Rating");
	public static readonly Field CommunityRating = Book("communityRating", "CommunityRating", "Community Rating", FieldKind.Decimal, FieldGroup.Number, Everywhere, "CommunityRating");

	public static readonly Field Manga = Book("manga", "manga", "Manga", FieldKind.MangaYesNo, FieldGroup.YesNo, Everywhere, "Manga");
	public static readonly Field SeriesComplete = Book("seriesComplete", "seriesComplete", "Series Complete", FieldKind.YesNo, FieldGroup.YesNo, Everywhere, "SeriesComplete");
	public static readonly Field BlackAndWhite = Book("blackAndWhite", "blackAndWhite", "Black And White", FieldKind.YesNo, FieldGroup.YesNo, Everywhere, "BlackAndWhite");

	public static readonly Field AlternateSeries = Book("alternateSeries", "altSeries", "Alternate Series", FieldKind.Items, FieldGroup.Items, Everywhere, "AlternateSeries");
	public static readonly Field Writer = Book("writer", "writer", "Writer", FieldKind.Items, FieldGroup.Items, Everywhere, "Writer");
	public static readonly Field Penciller = Book("penciller", "penciller", "Penciller", FieldKind.Items, FieldGroup.Items, Everywhere, "Penciller");
	public static readonly Field Inker = Book("inker", "inker", "Inker", FieldKind.Items, FieldGroup.Items, Everywhere, "Inker");
	public static readonly Field Colorist = Book("colorist", "colorist", "Colorist", FieldKind.Items, FieldGroup.Items, Everywhere, "Colorist");
	public static readonly Field Letterer = Book("letterer", "letterer", "Letterer", FieldKind.Items, FieldGroup.Items, Everywhere, "Letterer");
	public static readonly Field CoverArtist = Book("coverArtist", "coverartist", "Cover Artist", FieldKind.Items, FieldGroup.Items, Everywhere, "CoverArtist");
	public static readonly Field Editor = Book("editor", "editor", "Editor", FieldKind.Items, FieldGroup.Items, Everywhere, "Editor");
	public static readonly Field Genre = Book("genre", "genre", "Genre", FieldKind.Items, FieldGroup.Items, Everywhere, "Genre");
	public static readonly Field Tags = Book("tags", "tags", "Tags", FieldKind.Items, FieldGroup.Items, Everywhere, "Tags");
	public static readonly Field Characters = Book("characters", "characters", "Characters", FieldKind.Items, FieldGroup.Items, Everywhere, "Characters");
	public static readonly Field Teams = Book("teams", "teams", "Teams", FieldKind.Items, FieldGroup.Items, Everywhere, "Teams");
	public static readonly Field Locations = Book("locations", "locations", "Locations", FieldKind.Items, FieldGroup.Items, Everywhere, "Locations");
	public static readonly Field ScanInformation = Book("scanInformation", "scaninfo", "Scan Information", FieldKind.Items, FieldGroup.Items, Everywhere, "ScanInformation");

	// Not Required: ComicRack always reports a read percentage, so it is never empty.
	public static readonly Field ReadPercentage = Book("readPercentage", "read", "Read Percentage", FieldKind.Int, FieldGroup.Calculated, TemplateAndRule, "ReadPercentage", "Read");
	public static readonly Field ReleasedDate = Book("releasedDate", "ReleasedDate", "Released Date", FieldKind.Date, FieldGroup.Calculated, Everywhere, "ReleasedTime", "ReleasedDate");
	public static readonly Field AddedDate = Book("addedDate", "AddedDate", "Added Date", FieldKind.Date, FieldGroup.Calculated, Everywhere, "AddedTime", "AddedDate");
	public static readonly Field StartYear = FromSeries("startYear", "startyear", "Start Year", SeriesEnd.First, Year, "StartYear");
	public static readonly Field StartMonth = FromSeries("startMonth", "startmonth", "Start Month", SeriesEnd.First, Month, "StartMonth");
	public static readonly Field EndYear = FromSeries("endYear", "EndYear", "End Year", SeriesEnd.Last, Year, "EndYear");
	public static readonly Field EndMonth = FromSeries("endMonth", "EndMonth", "End Month", SeriesEnd.Last, Month, "EndMonth");
	public static readonly Field FirstIssueNumber = FromSeries("firstIssueNumber", "firstissuenumber", "First Issue Number", SeriesEnd.First, Number, "FirstIssueNumber");
	public static readonly Field LastIssueNumber = FromSeries("lastIssueNumber", "lastissuenumber", "Last Issue Number", SeriesEnd.Last, Number, "LastIssueNumber");
	public static readonly Field FirstLetter = Synthetic("firstLetter", "first", "First Letter", FieldSource.Book, FieldGroup.Calculated, "FirstLetter");
	public static readonly Field Counter = Synthetic("counter", "counter", "Counter", FieldSource.Run, FieldGroup.Calculated);

	private static readonly Dictionary<string, Field> KeyIndex = Registry.ToDictionary(f => f.Key, StringComparer.Ordinal);

	private static readonly Dictionary<string, Field> TemplateNameIndex =
		Registry.Where(f => f.TemplateName != null).ToDictionary(f => f.TemplateName!, StringComparer.Ordinal);

	private static readonly Dictionary<string, Field> LegacyNameIndex =
		Registry.SelectMany(f => f.LegacyNames.Select(name => (name, f))).ToDictionary(pair => pair.name, pair => pair.f, StringComparer.Ordinal);

	public static IReadOnlyList<Field> All { get; } = Registry.AsReadOnly();

	/// <summary>Exact, case-sensitive template-name lookup, as 2.x.</summary>
	public static Field? ByTemplateName(string name) => TemplateNameIndex.TryGetValue(name, out Field? field) ? field : null;

	public static Field? ByKey(string key) => KeyIndex.TryGetValue(key, out Field? field) ? field : null;

	/// <summary>Import only: 2.x property or display name ("ShadowSeries", "Series", "Released Date") to field.</summary>
	public static Field? ByLegacyName(string name) => LegacyNameIndex.TryGetValue(name, out Field? field) ? field : null;

	private static Field Book(
		string key, string? templateName, string displayName, FieldKind kind, FieldGroup group, FieldUses uses, string hostProperty,
		params string[] otherLegacyNames)
		=> Register(key, templateName, displayName, kind, FieldSource.Book, group, uses, hostProperty, [hostProperty, displayName, .. otherLegacyNames], null, null);

	private static Field FromSeries(string key, string templateName, string displayName, SeriesEnd end, Field derivedFrom, string legacyProperty)
		=> Register(key, templateName, displayName, derivedFrom.Kind, FieldSource.Series, FieldGroup.Calculated, TemplateAndRule, null, [legacyProperty, displayName], derivedFrom, end);

	private static Field Synthetic(string key, string templateName, string displayName, FieldSource source, FieldGroup group, params string[] otherLegacyNames)
		=> Register(key, templateName, displayName, FieldKind.Synthetic, source, group, FieldUses.Template, null, [displayName, .. otherLegacyNames], null, null);

	private static Field Register(
		string key, string? templateName, string displayName, FieldKind kind, FieldSource source, FieldGroup group, FieldUses uses,
		string? hostProperty, string[] legacyNames, Field? derivedFrom, SeriesEnd? seriesEnd)
	{
		var field = new Field(
			Registry.Count, key, templateName, displayName, kind, source, group, uses, hostProperty,
			legacyNames.Distinct(StringComparer.Ordinal).ToArray(), derivedFrom, seriesEnd);
		Registry.Add(field);
		return field;
	}
}
