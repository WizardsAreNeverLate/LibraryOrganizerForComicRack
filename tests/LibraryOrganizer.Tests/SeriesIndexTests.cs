using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class SeriesIndexTests
{
	private static string? Number(BookRecord? book) => book is null ? null : book[Fields.Number].ToRawText();

	[Test]
	public async Task First_orders_issue_numbers_naturally_within_a_month()
	{
		BookRecord ten = TestBooks.Book(number: "10", year: 1991, month: 10);
		BookRecord nine = TestBooks.Book(number: "9", year: 1991, month: 10);
		SeriesIndex index = SeriesIndex.Build([ten, nine]);

		await Assert.That(Number(index.First(ten.SeriesKey))).IsEqualTo("9");
	}

	[Test]
	public async Task First_prefers_a_known_year_over_an_unknown_one()
	{
		BookRecord unknown = TestBooks.Book(number: "1");
		BookRecord known = TestBooks.Book(number: "5", year: 1991);
		SeriesIndex index = SeriesIndex.Build([unknown, known]);

		await Assert.That(Number(index.First(unknown.SeriesKey))).IsEqualTo("5");
	}

	[Test]
	public async Task First_accepts_year_1_over_an_unknown_year()
	{
		BookRecord unknown = TestBooks.Book(number: "1");
		BookRecord yearOne = TestBooks.Book(number: "2", year: 1);
		SeriesIndex index = SeriesIndex.Build([unknown, yearOne]);

		await Assert.That(Number(index.First(unknown.SeriesKey))).IsEqualTo("2");
	}

	[Test]
	public async Task First_orders_by_year_then_month_then_number()
	{
		BookRecord[] books =
		[
			TestBooks.Book(number: "1", year: 1992, month: 1),
			TestBooks.Book(number: "30", year: 1991, month: 12),
			TestBooks.Book(number: "20", year: 1991),
			TestBooks.Book(number: "40", year: 1991, month: 3),
			TestBooks.Book(number: "41", year: 1991, month: 3),
		];
		SeriesIndex index = SeriesIndex.Build(books);

		await Assert.That(Number(index.First(books[0].SeriesKey))).IsEqualTo("40");
	}

	[Test]
	public async Task First_prefers_a_numbered_issue_over_an_unnumbered_one_in_the_same_month()
	{
		BookRecord preview = TestBooks.Book(number: "Preview", year: 1991, month: 10);
		BookRecord one = TestBooks.Book(number: "1", year: 1991, month: 10);
		SeriesIndex index = SeriesIndex.Build([preview, one]);

		await Assert.That(Number(index.First(one.SeriesKey))).IsEqualTo("1");
	}

	[Test]
	[Arguments("12.1")]
	[Arguments("12a")]
	public async Task Last_lets_a_point_or_lettered_issue_beat_its_whole_number(string variant)
	{
		BookRecord twelve = TestBooks.Book(number: "12", year: 1993);
		BookRecord later = TestBooks.Book(number: variant, year: 1992);
		SeriesIndex index = SeriesIndex.Build([later, twelve, TestBooks.Book(number: "2")]);

		await Assert.That(Number(index.Last(twelve.SeriesKey))).IsEqualTo(variant);
	}

	[Test]
	public async Task Last_compares_numbers_not_text()
	{
		BookRecord[] books = new[] { "9", "100", "10", "99" }.Select(n => TestBooks.Book(number: n)).ToArray();
		SeriesIndex index = SeriesIndex.Build(books);

		await Assert.That(Number(index.Last(books[0].SeriesKey))).IsEqualTo("100");
	}

	[Test]
	public async Task Last_ignores_unnumbered_issues_when_a_numbered_one_exists()
	{
		BookRecord[] books = [TestBooks.Book(number: "Annual"), TestBooks.Book(number: "120"), TestBooks.Book(number: null)];
		SeriesIndex index = SeriesIndex.Build(books);

		await Assert.That(Number(index.Last(books[0].SeriesKey))).IsEqualTo("120");
	}

	[Test]
	public async Task Last_ignores_the_release_date()
	{
		BookRecord newestByDate = TestBooks.Book(number: "3", year: 2020);
		BookRecord highest = TestBooks.Book(number: "50", year: 1995);
		SeriesIndex index = SeriesIndex.Build([newestByDate, highest]);

		await Assert.That(Number(index.Last(highest.SeriesKey))).IsEqualTo("50");
	}

	[Test]
	public async Task Series_are_keyed_by_publisher_series_and_volume_ignoring_case()
	{
		BookRecord one = TestBooks.Book(series: "X-Men", volume: 2, number: "1", publisher: "Marvel");
		BookRecord two = TestBooks.Book(series: "x-men", volume: 2, number: "2", publisher: "MARVEL");
		BookRecord otherVolume = TestBooks.Book(series: "X-Men", volume: 1, number: "300");
		BookRecord otherPublisher = TestBooks.Book(series: "X-Men", volume: 2, number: "400", publisher: "Panini");
		SeriesIndex index = SeriesIndex.Build([one, two, otherVolume, otherPublisher]);

		await Assert.That(one.SeriesKey).IsEqualTo(two.SeriesKey);
		await Assert.That(index.IssueCount(one.SeriesKey)).IsEqualTo(2);
		await Assert.That(Number(index.Last(one.SeriesKey))).IsEqualTo("2");
		await Assert.That(index.IssueCount(otherVolume.SeriesKey)).IsEqualTo(1);
	}

	[Test]
	public async Task A_series_the_index_does_not_hold_has_no_issues()
	{
		SeriesKey key = TestBooks.Book().SeriesKey;

		await Assert.That(SeriesIndex.Empty.First(key)).IsNull();
		await Assert.That(SeriesIndex.Empty.Last(key)).IsNull();
		await Assert.That(SeriesIndex.Empty.IssueCount(key)).IsEqualTo(0);
		await Assert.That(SeriesIndex.Empty.DistinctItems(key, Fields.Writer).Count).IsEqualTo(0);
	}

	[Test]
	public async Task DistinctItems_lists_each_value_once_sorted()
	{
		BookRecord[] books =
		[
			TestBooks.Book(number: "1", writer: "Jim Lee, Chris Claremont"),
			TestBooks.Book(number: "2", writer: " chris claremont ,Scott Lobdell,"),
			TestBooks.Book(number: "3"),
		];
		SeriesIndex index = SeriesIndex.Build(books);

		await Assert.That(string.Join("|", index.DistinctItems(books[0].SeriesKey, Fields.Writer)))
			.IsEqualTo("Chris Claremont|Jim Lee|Scott Lobdell");
	}

	[Test]
	public async Task Series_fields_read_from_the_first_and_last_issue()
	{
		BookRecord seven = TestBooks.Book(number: "7", year: 1992, month: 3);
		SeriesIndex index = SeriesIndex.Build(
		[
			seven,
			TestBooks.Book(number: "1", year: 1991, month: 10),
			TestBooks.Book(number: "120", year: 2001, month: 12),
		]);

		await Assert.That(FieldReader.Read(Fields.StartYear, seven, index).ToRawText()).IsEqualTo("1991");
		await Assert.That(FieldReader.Read(Fields.StartMonth, seven, index).ToRawText()).IsEqualTo("10");
		await Assert.That(FieldReader.Read(Fields.EndYear, seven, index).ToRawText()).IsEqualTo("2001");
		await Assert.That(FieldReader.Read(Fields.EndMonth, seven, index).ToRawText()).IsEqualTo("12");
		await Assert.That(FieldReader.Read(Fields.FirstIssueNumber, seven, index).ToRawText()).IsEqualTo("1");
		await Assert.That(FieldReader.Read(Fields.LastIssueNumber, seven, index).ToRawText()).IsEqualTo("120");
		await Assert.That(FieldReader.Read(Fields.Year, seven, index).ToRawText()).IsEqualTo("1992");
		await Assert.That(FieldReader.Read(Fields.Counter, seven, index).IsUnset).IsTrue();
	}

	[Test]
	public async Task Series_fields_fall_back_to_the_book_when_the_index_lacks_its_series()
	{
		BookRecord seven = TestBooks.Book(number: "7", year: 1992);

		await Assert.That(FieldReader.Read(Fields.StartYear, seven, SeriesIndex.Empty).ToRawText()).IsEqualTo("1992");
		await Assert.That(FieldReader.Read(Fields.LastIssueNumber, seven, SeriesIndex.Empty).ToRawText()).IsEqualTo("7");
		await Assert.That(FieldReader.Read(Fields.StartMonth, seven, SeriesIndex.Empty).IsUnset).IsTrue();
	}
}
