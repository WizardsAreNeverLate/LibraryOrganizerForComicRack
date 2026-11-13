using System;
using System.Collections.Generic;

namespace LibraryOrganizer;

/// <summary>What a field's value is. Decides the template formats it accepts, the rule operators it offers, and how "empty" is judged.</summary>
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

/// <summary>Where the value comes from. Drives the snapshot demand and the series index.</summary>
public enum FieldSource
{
	/// <summary>Read from the book's snapshot.</summary>
	Book,
	/// <summary>Computed from the book's series in the SeriesIndex (start year, last issue number...).</summary>
	Series,
	/// <summary>Computed by the run itself (counter).</summary>
	Run,
}

/// <summary>UI grouping for the insert-field tabs and the rule editor. Order of the enum is tab order.</summary>
public enum FieldGroup { Text, Number, YesNo, Items, Calculated }

/// <summary>
/// One comic attribute. The single source for templates, rules, required fields, empty substitutes, UI field lists,
/// the host snapshot binding and the 2.x import name maps. Instances exist only as static members of <see cref="Fields"/>.
/// </summary>
public sealed class Field
{
	private Field() => throw new NotImplementedException();

	/// <summary>Dense index into <see cref="BookRecord"/> values. Assigned by registry order.</summary>
	public int Ordinal { get; }

	/// <summary>Stable key in settings.xml and answers.xml. Never renamed once shipped.</summary>
	public string Key { get; }

	/// <summary>The name inside &lt;...&gt; (case-sensitive, as in 2.x): "series", "number", "startyear", "EndYear".</summary>
	public string TemplateName { get; }

	public string DisplayName { get; }
	public FieldKind Kind { get; }
	public FieldSource Source { get; }
	public FieldGroup Group { get; }

	/// <summary>ComicBook property the plugin snapshots, e.g. "ShadowSeries". Null for Series/Run/Synthetic fields.</summary>
	public string? HostProperty { get; }

	/// <summary>2.x names that import maps from: the property name ("ShadowSeries", used in EmptyData/FailedFields) and display name ("Series", used in rules).</summary>
	public IReadOnlyList<string> LegacyNames { get; }

	/// <summary>For Series fields: the book field it is derived from (StartYear -> Year, LastIssueNumber -> Number).</summary>
	public Field? DerivedFrom { get; }

	/// <summary>Book-scoped fields that a user can require or give an empty substitute.</summary>
	public bool CanBeRequired => Source == FieldSource.Book && Kind != FieldKind.Synthetic;
}

/// <summary>The registry. Adding a field is one line here; nothing else lists fields.</summary>
public static class Fields
{
	// TODO: one static readonly per field, in tab order. Sketch of the set (template name / host property / kind):
	// series/ShadowSeries/Text, number/ShadowNumber/Issue, count/ShadowCount/Int, volume/ShadowVolume/Int, year/ShadowYear/Int,
	// month/Month/Month, Day/Day/Int, title/ShadowTitle/Text, format/ShadowFormat/Text, publisher, imprint, altSeries/Items,
	// altNumber/Issue, altCount/Int, ageRating, language/LanguageAsText, writer..editor/Items, genre,tags,characters,teams,locations/Items,
	// storyarc, seriesgroup, maincharacter/MainCharacterOrTeam, scaninfo/Items, manga/MangaYesNo, seriesComplete/YesNo,
	// blackAndWhite/YesNo (rules only in 2.x, now templates too), read/ReadPercentage/Int, ReleasedDate/ReleasedTime/Date,
	// AddedDate/AddedTime/Date, Rating/Decimal, CommunityRating/Decimal, notes, web, review, filePath/fileName/fileFormat (rules),
	// startyear,startmonth,EndYear,EndMonth,firstissuenumber,lastissuenumber (Series), counter (Run, Synthetic),
	// first (Synthetic), Custom (Synthetic). "month#" and "startmonth#" are not fields: '#' is a format suffix (see TokenResolver).
	public static readonly Field Series = null!;
	public static readonly Field Number = null!;
	public static readonly Field Volume = null!;
	public static readonly Field Publisher = null!;
	public static readonly Field Year = null!;
	public static readonly Field Month = null!;

	public static IReadOnlyList<Field> All => throw new NotImplementedException();

	/// <summary>Exact, case-sensitive template-name lookup, as 2.x.</summary>
	public static Field? ByTemplateName(string name) => throw new NotImplementedException();

	public static Field? ByKey(string key) => throw new NotImplementedException();

	/// <summary>Import only: 2.x property or display name ("ShadowSeries", "Series", "Released Date") to field.</summary>
	public static Field? ByLegacyName(string name) => throw new NotImplementedException();

	/// <summary>The fields that identify a series. The snapshot always reads these for every library book.</summary>
	public static IReadOnlyList<Field> SeriesKeyFields => [Publisher, Series, Volume];
}

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

	/// <summary>The uncleaned text a plain token writes and a test compares against.</summary>
	public string ToRawText() => throw new NotImplementedException();
}

/// <summary>Both ComicRack enums folded into one; YesNo fields never hold YesRightToLeft.</summary>
public enum YesNoValue { No, Yes, YesRightToLeft }

/// <summary>
/// An issue number with natural ordering and 2.x padding rules. Replaces string compares ("10" &lt; "9") and int-only parsing
/// ("12.1" never last). Value semantics; parse never fails (non-numeric text keeps its text and sorts after numbers with the same prefix).
/// </summary>
public readonly record struct IssueNumber(string Text) : IComparable<IssueNumber>
{
	public int CompareTo(IssueNumber other) => throw new NotImplementedException();

	/// <summary>
	/// 2.x pad: left-pad the integer part to width, keep the remainder, keep a leading '-'. Non-numeric ("½", "7b") unchanged.
	/// 7/3 -> "007", 7.1/3 -> "007.1", -1.5/3 -> "-001.5", 123/2 -> "123".
	/// </summary>
	public string Pad(int width) => throw new NotImplementedException();
}

/// <summary>What the snapshot must read: book fields for the selection, and the extra fields library books in the same series need.</summary>
/// <remarks>net48 has no IReadOnlySet; the sets are built once by <c>Organizer.Demand</c> and never mutated.</remarks>
public sealed record FieldDemand(IReadOnlyCollection<Field> SelectionFields, IReadOnlyCollection<Field> SeriesMemberFields, IReadOnlyCollection<string> CustomKeys);
