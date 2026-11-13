using System;
using System.Collections.Generic;

namespace LibraryOrganizer;

/// <summary>ComicRack's ComicBook.Id. Stable across sessions and renames; the journal keys on it.</summary>
public readonly record struct BookId(Guid Value);

public abstract record BookLocation
{
	private BookLocation() { }

	public sealed record File(AbsPath Path, bool Exists) : BookLocation;
	public sealed record Fileless(bool HasCustomCover) : BookLocation;
}

/// <summary>A frozen snapshot of one ComicBook, taken on the UI thread. The engine reads only this.</summary>
public sealed class BookRecord
{
	private readonly FieldValue[] values;
	private readonly IReadOnlyDictionary<string, string> custom;

	/// <param name="values">One value per registry field, indexed by <see cref="Field.Ordinal"/>. Series and Run slots hold Unset.</param>
	/// <param name="custom">ComicRack's custom values. The caller chooses the key comparer.</param>
	public BookRecord(BookId id, BookLocation location, string caption, FieldValue[] values, IReadOnlyDictionary<string, string> custom)
	{
		if (values.Length != Fields.All.Count)
		{
			throw new ArgumentException($"Expected one value per registry field ({Fields.All.Count}), got {values.Length}.", nameof(values));
		}
		Id = id;
		Location = location;
		Caption = caption;
		this.values = (FieldValue[])values.Clone();
		this.custom = custom;
		SeriesKey = new SeriesKey(
			this[Fields.Publisher] is FieldValue.Text publisher ? publisher.Value : "",
			this[Fields.Series] is FieldValue.Text series ? series.Value : "",
			this[Fields.Volume] is FieldValue.Int volume ? volume.Value : null);
	}

	public BookId Id { get; }
	public BookLocation Location { get; }

	public string Caption { get; }

	/// <summary>Book-sourced field value. Series and Run fields are read through FieldReader, not here.</summary>
	public FieldValue this[Field field] => values[field.Ordinal];

	public string? Custom(string key) => custom.TryGetValue(key, out string? value) ? value : null;

	public SeriesKey SeriesKey { get; }
}

/// <summary>Publisher + series + volume, compared case-insensitively. 2.x concatenated strings with no separator.</summary>
public readonly record struct SeriesKey(string Publisher, string Series, int? Volume)
{
	public bool Equals(SeriesKey other)
		=> StringComparer.OrdinalIgnoreCase.Equals(Publisher, other.Publisher)
			&& StringComparer.OrdinalIgnoreCase.Equals(Series, other.Series)
			&& Volume == other.Volume;

	public override int GetHashCode()
		=> (StringComparer.OrdinalIgnoreCase.GetHashCode(Publisher ?? ""), StringComparer.OrdinalIgnoreCase.GetHashCode(Series ?? ""), Volume).GetHashCode();
}
