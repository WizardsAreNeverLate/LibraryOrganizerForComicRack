using System;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using LibraryOrganizer.Plugin;

namespace LibraryOrganizer.Plugin.Tests;

public class BookSnapshotTests
{
	[Test]
	public async Task Every_registry_field_with_a_host_property_binds_to_ComicBook()
	{
		string[] unbound = [.. Fields.All.Where(f => f.HostProperty is not null).Where(f => !Binds(f)).Select(f => f.Key)];

		await Assert.That(unbound).IsEmpty();
	}

	[Test]
	public async Task A_new_book_snapshots_its_unset_numbers_enums_and_dates_as_Unset()
	{
		BookRecord record = BookSnapshot.Record(new ComicBook())!;

		Field[] notUnset = [.. new[] { Fields.Year, Fields.Month, Fields.Volume, Fields.Count, Fields.Number, Fields.Manga, Fields.SeriesComplete, Fields.Series, Fields.Writer, Fields.Rating }
			.Where(f => !record[f].IsUnset)];
		await Assert.That(notUnset).IsEmpty();
	}

	[Test]
	public async Task Metadata_becomes_typed_values()
	{
		var book = new ComicBook
		{
			Series = "X-Men", Number = "7", Volume = 2, Year = 1992, Month = 3, Publisher = "Marvel",
			Writer = "Chris Claremont, Jim Lee", Manga = MangaYesNo.YesAndRightToLeft, SeriesComplete = YesNo.No,
		};

		BookRecord record = BookSnapshot.Record(book)!;

		using (Assert.Multiple())
		{
			await Assert.That(record[Fields.Series]).IsEqualTo(new FieldValue.Text("X-Men"));
			await Assert.That(record[Fields.Number].ToRawText()).IsEqualTo("7");
			await Assert.That(record[Fields.Year]).IsEqualTo(new FieldValue.Int(1992));
			await Assert.That(record[Fields.Month]).IsEqualTo(new FieldValue.Int(3));
			await Assert.That(((FieldValue.Items)record[Fields.Writer]).Values).IsEquivalentTo(new[] { "Chris Claremont", "Jim Lee" });
			await Assert.That(record[Fields.Manga]).IsEqualTo(new FieldValue.YesNo(YesNoValue.YesRightToLeft));
			await Assert.That(record[Fields.SeriesComplete]).IsEqualTo(new FieldValue.YesNo(YesNoValue.No));
			await Assert.That(record.SeriesKey).IsEqualTo(new SeriesKey("marvel", "x-men", 2));
			await Assert.That(BookSnapshot.KeyOf(book)).IsEqualTo(record.SeriesKey);
		}
	}

	[Test]
	public async Task A_linked_book_records_its_path_and_a_fileless_book_its_cover()
	{
		var linked = new ComicBook { FilePath = @"Z:\nowhere\x.cbz" };
		var fileless = new ComicBook { CustomThumbnailKey = "cover" };

		await Assert.That(BookSnapshot.Record(linked)!.Location).IsEqualTo(new BookLocation.File(AbsPath.TryParse(@"Z:\nowhere\x.cbz")!.Value, false));
		await Assert.That(BookSnapshot.Record(fileless)!.Location).IsEqualTo(new BookLocation.Fileless(true));
	}

	private static bool Binds(Field field)
	{
		try
		{
			return BookSnapshot.ReaderFor(field) is not null;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}
}
