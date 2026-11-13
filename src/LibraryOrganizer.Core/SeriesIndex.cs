using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>
/// Series lookups for one run. Built from snapshots of every library book that shares a series key with the selection,
/// so it is fresh per run (2.x cached forever).
/// </summary>
public sealed class SeriesIndex
{
	private readonly Dictionary<SeriesKey, Issues> series;

	private SeriesIndex(Dictionary<SeriesKey, Issues> series) => this.series = series;

	public static SeriesIndex Empty { get; } = new([]);

	public static SeriesIndex Build(IEnumerable<BookRecord> libraryBooksInSelectedSeries)
		=> new(libraryBooksInSelectedSeries
			.GroupBy(book => book.SeriesKey)
			.ToDictionary(group => group.Key, group => new Issues(group.ToList(), FindFirst(group), FindLast(group))));

	/// <summary>Earliest issue: known year before unknown, then year, month (known before unknown), then natural issue number.</summary>
	public BookRecord? First(SeriesKey key) => series.TryGetValue(key, out Issues? issues) ? issues.First : null;

	/// <summary>
	/// Highest natural issue number; "12.1" and "12a" beat "12". Release date plays no part, as in 2.x.
	/// An issue with no leading number ("Annual", unset) wins only when the series has no numbered issue.
	/// </summary>
	public BookRecord? Last(SeriesKey key) => series.TryGetValue(key, out Issues? issues) ? issues.Last : null;

	/// <summary>Distinct trimmed values of an Items field across the series, ignoring case, sorted, for series-scope multi-value questions.</summary>
	public IReadOnlyList<string> DistinctItems(SeriesKey key, Field itemsField)
		=> series.TryGetValue(key, out Issues? issues)
			? issues.Books
				.SelectMany(book => book[itemsField] is FieldValue.Items items ? items.Values : [])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
				.ToList()
			: [];

	public int IssueCount(SeriesKey key) => series.TryGetValue(key, out Issues? issues) ? issues.Books.Count : 0;

	private static BookRecord FindFirst(IEnumerable<BookRecord> books)
		=> books
			.OrderBy(book => IntOrNull(book, Fields.Year) is null)
			.ThenBy(book => IntOrNull(book, Fields.Year))
			.ThenBy(book => IntOrNull(book, Fields.Month) is null)
			.ThenBy(book => IntOrNull(book, Fields.Month))
			.ThenBy(NumberOf)
			.First();

	private static BookRecord FindLast(IEnumerable<BookRecord> books)
	{
		List<BookRecord> numbered = books.Where(book => NumberOf(book).IsNumeric).ToList();
		return (numbered.Count > 0 ? numbered : books).Aggregate((last, book) => NumberOf(book).CompareTo(NumberOf(last)) > 0 ? book : last);
	}

	private static int? IntOrNull(BookRecord book, Field field) => book[field] is FieldValue.Int value ? value.Value : null;

	private static IssueNumber NumberOf(BookRecord book) => book[Fields.Number] is FieldValue.Issue issue ? issue.Value : new IssueNumber("");

	private sealed record Issues(IReadOnlyList<BookRecord> Books, BookRecord First, BookRecord Last);
}

/// <summary>
/// The one read path for templates, rules and required fields. Book fields come from the record; Series fields from the index.
/// </summary>
internal static class FieldReader
{
	public static FieldValue Read(Field field, BookRecord book, SeriesIndex index) => field.Source switch
	{
		FieldSource.Book => book[field],
		FieldSource.Series => ((field.SeriesEnd == SeriesEnd.Last ? index.Last(book.SeriesKey) : index.First(book.SeriesKey)) ?? book)[field.DerivedFrom!],
		FieldSource.Run => FieldValue.Unset.Value,
		_ => throw new ArgumentOutOfRangeException(nameof(field)),
	};
}
