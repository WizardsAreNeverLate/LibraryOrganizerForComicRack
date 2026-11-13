using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using cYo.Projects.ComicRack.Engine;

namespace LibraryOrganizer.Plugin;

/// <summary>
/// The one place a live ComicBook becomes a BookRecord. Runs on the UI thread. ComicRack's "unset" encodings (-1, "",
/// Unknown, DateTime.MinValue) end here and never reach the engine.
/// </summary>
internal static class BookSnapshot
{
	private static readonly Func<ComicBook, FieldValue>?[] Readers = [.. Fields.All.Select(ReaderFor)];

	/// <summary>Null when the book's file path is not a usable absolute path.</summary>
	public static BookRecord? Record(ComicBook book)
	{
		BookLocation location;
		if (book.IsLinked)
		{
			if (AbsPath.TryParse(book.FilePath) is not AbsPath path)
			{
				return null;
			}
			location = new BookLocation.File(path, File.Exists(path.Value));
		}
		else
		{
			location = new BookLocation.Fileless(!string.IsNullOrEmpty(book.CustomThumbnailKey));
		}
		var values = new FieldValue[Readers.Length];
		for (int i = 0; i < Readers.Length; i++)
		{
			values[i] = Readers[i]?.Invoke(book) ?? FieldValue.Unset.Value;
		}
		var custom = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var pair in book.GetCustomValues())
		{
			custom[pair.Key] = pair.Value;
		}
		return new BookRecord(new BookId(book.Id), location, book.Caption, values, custom);
	}

	/// <summary>The series key from three property reads, so the whole library can be filtered without a full snapshot.</summary>
	public static SeriesKey KeyOf(ComicBook book)
		=> new(book.Publisher ?? "", book.ShadowSeries ?? "", book.ShadowVolume < 0 ? null : book.ShadowVolume);

	internal static Func<ComicBook, FieldValue>? ReaderFor(Field field)
	{
		if (field.HostProperty is null)
		{
			return null;
		}
		PropertyInfo property = typeof(ComicBook).GetProperty(field.HostProperty, BindingFlags.Public | BindingFlags.Instance)
			?? throw new InvalidOperationException($"ComicBook has no property '{field.HostProperty}' for field '{field.Key}'.");
		ParameterExpression book = Expression.Parameter(typeof(ComicBook));
		Func<ComicBook, object?> get = Expression.Lambda<Func<ComicBook, object?>>(Expression.Convert(Expression.Property(book, property), typeof(object)), book).Compile();
		Func<object?, FieldValue> convert = ConverterFor(field.Kind);
		return comic => convert(get(comic));
	}

	private static Func<object?, FieldValue> ConverterFor(FieldKind kind) => kind switch
	{
		FieldKind.Text => value => value is string { Length: > 0 } text ? new FieldValue.Text(text) : FieldValue.Unset.Value,
		FieldKind.Int => value => value is int number and >= 0 ? new FieldValue.Int(number) : FieldValue.Unset.Value,
		FieldKind.Month => value => value is int month and > 0 ? new FieldValue.Int(month) : FieldValue.Unset.Value,
		// ComicRack stores "no rating" as 0.
		FieldKind.Decimal => value => value is float number and > 0 ? new FieldValue.Decimal((decimal)number) : FieldValue.Unset.Value,
		FieldKind.Issue => value => value is string { Length: > 0 } text ? new FieldValue.Issue(new IssueNumber(text)) : FieldValue.Unset.Value,
		FieldKind.YesNo or FieldKind.MangaYesNo => value => value?.ToString() switch
		{
			"Yes" => new FieldValue.YesNo(YesNoValue.Yes),
			"YesAndRightToLeft" => new FieldValue.YesNo(YesNoValue.YesRightToLeft),
			"No" => new FieldValue.YesNo(YesNoValue.No),
			_ => FieldValue.Unset.Value,
		},
		FieldKind.Date => value => value is DateTime date && date != DateTime.MinValue ? new FieldValue.Date(date) : FieldValue.Unset.Value,
		FieldKind.Items => value => FieldValue.ParseItems(value as string),
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A field of this kind has no host property."),
	};
}
