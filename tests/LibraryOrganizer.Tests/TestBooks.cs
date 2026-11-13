using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer.Tests;

internal static class TestBooks
{
	public static BookRecord Book(
		string series = "X-Men", int? volume = null, string? number = null, string publisher = "Marvel", int? year = null, int? month = null,
		string? writer = null, string? path = null)
	{
		FieldValue[] values = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		values[Fields.Series.Ordinal] = new FieldValue.Text(series);
		values[Fields.Publisher.Ordinal] = new FieldValue.Text(publisher);
		if (volume is { } v)
		{
			values[Fields.Volume.Ordinal] = new FieldValue.Int(v);
		}
		if (number is not null)
		{
			values[Fields.Number.Ordinal] = new FieldValue.Issue(new IssueNumber(number));
		}
		if (year is { } y)
		{
			values[Fields.Year.Ordinal] = new FieldValue.Int(y);
		}
		if (month is { } m)
		{
			values[Fields.Month.Ordinal] = new FieldValue.Int(m);
		}
		values[Fields.Writer.Ordinal] = FieldValue.ParseItems(writer);

		string file = path ?? $@"D:\Incoming\{series} {number}.cbz";
		return new BookRecord(
			new BookId(Guid.NewGuid()),
			new BookLocation.File(AbsPath.TryParse(file)!.Value, Exists: true),
			$"{series} #{number}",
			values,
			new Dictionary<string, string>());
	}
}
