using System;
using System.Collections.Generic;

namespace LibraryOrganizer;

/// <summary>ComicRack's ComicBook.Id. Stable across sessions and renames; the journal and answers key on it.</summary>
public readonly record struct BookId(Guid Value);

/// <summary>
/// A normalized absolute Windows path. Equality is ordinal-ignore-case, the way NTFS compares names.
/// Every path comparison in the engine (conflicts, excluded folders, prune roots, undo) goes through this type.
/// </summary>
public readonly record struct AbsPath
{
	private AbsPath(string value) => Value = value;

	public string Value { get; }

	/// <summary>Boundary: rejects relative paths and invalid characters. Normalizes separators and strips a trailing '\'.</summary>
	public static AbsPath? TryParse(string raw) => throw new NotImplementedException();

	public AbsPath Parent => throw new NotImplementedException();
	public string Name => throw new NotImplementedException();
	public string Extension => throw new NotImplementedException();
	public AbsPath Combine(string childName) => throw new NotImplementedException();

	/// <summary>True when this is <paramref name="folder"/> or below it, by whole segments ("D:\Manga" does not contain "D:\Manga Extra").</summary>
	public bool IsUnder(AbsPath folder) => throw new NotImplementedException();

	/// <summary>Same path ignoring case, different spelling: the RenameCase case.</summary>
	public bool DiffersOnlyByCase(AbsPath other) => throw new NotImplementedException();

	public bool Equals(AbsPath other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

/// <summary>Where the book's content lives. Fileless books have only metadata, and maybe a custom cover.</summary>
public abstract record BookLocation
{
	private BookLocation() { }

	public sealed record File(AbsPath Path, bool Exists) : BookLocation;
	public sealed record Fileless(bool HasCustomCover) : BookLocation;
}

/// <summary>
/// A frozen snapshot of one ComicBook, taken on the UI thread. The engine reads only this.
/// Values hold exactly the fields the run demanded; a field not demanded reads as Unset (a demand bug, caught by a debug assert).
/// </summary>
public sealed class BookRecord
{
	public BookRecord(BookId id, BookLocation location, string caption, FieldValue[] values, IReadOnlyDictionary<string, string> custom)
		=> throw new NotImplementedException();

	public BookId Id { get; }
	public BookLocation Location { get; }

	/// <summary>ComicRack's caption, for the review grid and log.</summary>
	public string Caption { get; }

	/// <summary>Book-sourced field value. Series and Run fields are read through FieldReader, not here.</summary>
	public FieldValue this[Field field] => throw new NotImplementedException();

	public string? Custom(string key) => throw new NotImplementedException();

	public SeriesKey SeriesKey => throw new NotImplementedException();
}

/// <summary>Publisher + series + volume, compared case-insensitively. 2.x concatenated strings with no separator.</summary>
public readonly record struct SeriesKey(string Publisher, string Series, int? Volume)
{
	public bool Equals(SeriesKey other) => throw new NotImplementedException();
	public override int GetHashCode() => throw new NotImplementedException();
}

/// <summary>
/// Series lookups for one run. Built from snapshots of every library book that shares a series key with the selection,
/// so it is fresh per run (2.x cached forever).
/// </summary>
public sealed class SeriesIndex
{
	private SeriesIndex() => throw new NotImplementedException();

	public static SeriesIndex Build(IEnumerable<BookRecord> libraryBooksInSelectedSeries) => throw new NotImplementedException();

	public static SeriesIndex Empty => throw new NotImplementedException();

	/// <summary>
	/// Earliest issue: known year before unknown, then year, month (known before unknown), then natural issue number.
	/// TODO: this replaces get_earliest_book; it fixes the "year != 1" typo and the string sort.
	/// </summary>
	public BookRecord? First(SeriesKey key) => throw new NotImplementedException();

	/// <summary>Highest natural issue number; "12.1" and "12a" beat "12". Release date plays no part, as in 2.x.</summary>
	public BookRecord? Last(SeriesKey key) => throw new NotImplementedException();

	/// <summary>Distinct trimmed values of an Items field across the series, sorted, for series-scope multi-value questions.</summary>
	public IReadOnlyList<string> DistinctItems(SeriesKey key, Field itemsField) => throw new NotImplementedException();

	public int IssueCount(SeriesKey key) => throw new NotImplementedException();
}

/// <summary>
/// The one read path for templates, rules and required fields. Book fields come from the record; Series fields from the index.
/// </summary>
internal static class FieldReader
{
	/// <summary>
	/// TODO: switch on field.Source:
	///   Book   -> book[field]
	///   Series -> index.First/Last(book.SeriesKey)?[field.DerivedFrom] (StartYear, StartMonth, EndYear, EndMonth, First/LastIssueNumber)
	///   Run    -> Unset (counter values come from the render state, not the book)
	/// </summary>
	public static FieldValue Read(Field field, BookRecord book, SeriesIndex index) => throw new NotImplementedException();
}
