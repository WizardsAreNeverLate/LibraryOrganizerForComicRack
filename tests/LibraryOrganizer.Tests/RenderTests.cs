using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static LibraryOrganizer.Tests.TemplateFixture;

namespace LibraryOrganizer.Tests;

public class RenderTests
{
	private static RenderContext InXMenSeries(BookRecord book) => DefaultContext with { Series = XMenSeries(book) };

	[Test]
	public async Task Vector_default_file_template()
	{
		await Assert.That(FileName(PathDefaults.FileTemplate, XMen7())).IsEqualTo("X-Men Vol.2 #07 (of 12) (March, 1992)");
	}

	[Test]
	public async Task Vector_default_folder_template_drops_the_empty_imprint_level()
	{
		BookRecord book = XMen7();

		await Assert.That(Folder(PathDefaults.FolderTemplate, book, InXMenSeries(book))).IsEqualTo(@"Marvel\X-Men (1991)");
	}

	[Test]
	public async Task Vector_automatic_padding_is_as_wide_as_the_last_issue()
	{
		BookRecord book = XMen7();

		await Assert.That(FileName("{<number0>}", book, InXMenSeries(book))).IsEqualTo("007");
	}

	[Test]
	[Arguments("{<number3>}", "7.1", "007.1")]
	[Arguments("{<number3>}", "-1.5", "-001.5")]
	[Arguments("{<number3>}", "7", "007")]
	[Arguments("{<number2>}", "-1.5", "-01.5")]
	[Arguments("{<number2>}", "123", "123")]
	[Arguments("{<number3>}", "½", "½")]
	[Arguments("{<number3>}", "7b", "7b")]
	public async Task Vector_padding(string template, string number, string expected)
	{
		await Assert.That(FileName(template, XMen7((Fields.Number, number)))).IsEqualTo(expected);
	}

	[Test]
	public async Task Vector_padded_month_number()
	{
		await Assert.That(FileName("{<month#2>}", XMen7())).IsEqualTo("03");
	}

	[Test]
	public async Task Vector_first_letter_skips_the_article()
	{
		await Assert.That(FileName("{<first(Series)>}", XMen7((Fields.Series, "The Avengers")))).IsEqualTo("A");
	}

	[Test]
	public async Task Vector_manga_text_for_yes_writes_nothing_on_no()
	{
		await Assert.That(FileName("{[<manga(Manga)>]}", XMen7())).IsEqualTo("");
	}

	[Test]
	public async Task Vector_manga_text_for_no()
	{
		await Assert.That(FileName("{[<manga(Western)(!)>]}", XMen7())).IsEqualTo("[Western]");
	}

	[Test]
	public async Task Vector_conditional_on_an_empty_format_writes_nothing()
	{
		await Assert.That(FileName("{ (Annual)<?format(Annual)>}", XMen7())).IsEqualTo("");
	}

	[Test]
	public async Task Vector_inversion_on_an_empty_format_writes_its_text()
	{
		await Assert.That(FileName("{No Format<!format>}", XMen7())).IsEqualTo("No Format");
	}

	[Test]
	public async Task Vector_a_colon_in_literal_text_is_cleaned()
	{
		await Assert.That(FileName("{<series>}: Part 1", XMen7())).IsEqualTo("X-Men - Part 1");
	}

	[Test]
	[Arguments("X{ (Annual)<?format(Annual)>}", "Annual", "X (Annual)")]
	[Arguments("X{ (Annual)<?format(Annual)>}", "Annual 2", "X")]
	[Arguments("X{[TPB]<?format(!Trade.*)>}", "Trade Paperback", "X[TPB]")]
	[Arguments("X{[TPB]<?format(!Trade.*)>}", "Hardcover", "X")]
	[Arguments("X{[TPB]<?format(!Trade.*)>}", "A Trade", "X")]
	[Arguments("X{[TPB]<?format(!Trade.*)>}", null, "X")]
	[Arguments("X{ (Ongoing)<!format(Limited Series)>}", "Annual", "X (Ongoing)")]
	[Arguments("X{ (Ongoing)<!format(Limited Series)>}", null, "X (Ongoing)")]
	[Arguments("X{ (Ongoing)<!format(Limited Series)>}", "Limited Series", "X")]
	[Arguments("X{[Other]<!format(!Trade.*)>}", "Hardcover", "X[Other]")]
	[Arguments("X{[Other]<!format(!Trade.*)>}", "Trade Paperback", "X")]
	[Arguments("X{[Never]<?format(!)>}", "Annual", "X")]
	[Arguments("X{[Never]<?format(!)>}", null, "X")]
	[Arguments("X{ has format<!format>}", "Annual", "X")]
	public async Task Tests_on_format(string template, string? format, string expected)
	{
		await Assert.That(FileName(template, XMen7((Fields.Format, format)))).IsEqualTo(expected);
	}

	[Test]
	public async Task Inversion_with_no_argument_tests_for_an_empty_value()
	{
		await Assert.That(FileName("{No Number<!number>}", XMen7((Fields.Number, null)))).IsEqualTo("No Number");
		await Assert.That(FileName("{No Number<!number>}", XMen7())).IsEqualTo("");
	}

	[Test]
	[Arguments(95, "[Read]")]
	[Arguments(90, "")]
	public async Task A_test_takes_the_last_argument_and_the_format_keeps_the_rest(int percent, string expected)
	{
		await Assert.That(FileName("{[Read]<?read(x)(>)(90)(x)>}", XMen7((Fields.ReadPercentage, percent)))).IsEqualTo(expected);
	}

	[Test]
	public async Task A_test_compares_the_raw_value_not_the_cleaned_one()
	{
		await Assert.That(FileName("{X<?format(Trade: Paperback)>}", XMen7((Fields.Format, "Trade: Paperback")))).IsEqualTo("X");
	}

	[Test]
	public async Task A_test_on_a_month_compares_its_name()
	{
		await Assert.That(FileName("{Spring issue<?month(March)>}", XMen7())).IsEqualTo("Spring issue");
	}

	[Test]
	public async Task A_nested_group_shows_only_when_the_outer_field_has_a_value()
	{
		const string template = "X{ ({<month>, }<year>)}";

		await Assert.That(FileName(template, XMen7())).IsEqualTo("X (March, 1992)");
		await Assert.That(FileName(template, XMen7((Fields.Month, null)))).IsEqualTo("X (1992)");
		await Assert.That(FileName(template, XMen7((Fields.Year, null)))).IsEqualTo("X");
	}

	[Test]
	[Arguments("<series>", "[series]")]
	[Arguments("{<number>}", "{[number]}")]
	[Arguments("{", "{")]
	public async Task A_value_that_looks_like_template_syntax_is_written_as_text(string series, string expected)
	{
		await Assert.That(FileName("{<series>}", XMen7((Fields.Series, series)))).IsEqualTo(expected);
	}

	[Test]
	public async Task An_empty_value_writes_its_substitute_without_the_surrounding_text()
	{
		RenderContext context = DefaultContext with
		{
			Style = PathStyle.Default with { EmptySubstitutes = new Dictionary<Field, string> { [Fields.Imprint] = "No Imprint" } },
		};

		await Assert.That(FileName("{[<imprint>] }{<series>}", XMen7(), context)).IsEqualTo("No ImprintX-Men");
		await Assert.That(FileName("{[<imprint>] }{<series>}", XMen7((Fields.Imprint, "Epic")), context)).IsEqualTo("[Epic] X-Men");
		await Assert.That(FileName("{[<imprint>] }{<series>}", XMen7())).IsEqualTo("X-Men");
	}

	[Test]
	[Arguments("{<month>}", 3, "March")]
	[Arguments("{<month>}", 13, "Spring")]
	[Arguments("{<month>}", 16, "Winter")]
	[Arguments("{<month#>}", 3, "3")]
	[Arguments("{<month#2>}", 11, "11")]
	[Arguments("{<month2>}", 3, "03")]
	public async Task Month_forms(string template, int month, string expected)
	{
		await Assert.That(FileName(template, XMen7((Fields.Month, month)))).IsEqualTo(expected);
	}

	[Test]
	public async Task An_unset_number_pads_to_nothing()
	{
		await Assert.That(FileName("X{ #<number3>}{ <month#2>}", XMen7((Fields.Number, null), (Fields.Month, null)))).IsEqualTo("X");
	}

	[Test]
	public async Task Series_fields_read_the_first_and_last_issue()
	{
		BookRecord book = XMen7();

		await Assert.That(FileName("{<startyear>}{ <firstissuenumber3>}{ to <lastissuenumber>}", book, InXMenSeries(book))).IsEqualTo("1991 001 to 120");
	}

	[Test]
	public async Task Automatic_padding_without_a_series_uses_the_book_itself()
	{
		await Assert.That(FileName("{<number0>}", XMen7((Fields.Number, "15")))).IsEqualTo("15");
	}

	[Test]
	[Arguments("{<manga(Manga)>}", YesNoValue.Yes, "Manga")]
	[Arguments("{<manga(Manga)>}", YesNoValue.YesRightToLeft, "Manga")]
	[Arguments("{<manga(Western)(!)>}", YesNoValue.Yes, "")]
	[Arguments("{<manga(Western)(!)>}", null, "")]
	[Arguments("{<manga(Manga)>}", null, "")]
	[Arguments("{<manga>}", YesNoValue.YesRightToLeft, "YesAndRightToLeft")]
	public async Task Yes_no_forms(string template, YesNoValue? manga, string expected)
	{
		await Assert.That(FileName(template, XMen7((Fields.Manga, manga)))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("{<read(Unread)(<)(10)>}", 9, "Unread")]
	[Arguments("{<read(Unread)(<)(10)>}", 10, "")]
	[Arguments("{<read(Done)(=)(100)>}", 100, "Done")]
	[Arguments("{<read(Done)(=)(100)>}", 99, "")]
	[Arguments("{<read>}", 42, "42")]
	public async Task Read_percentage_forms(string template, int percent, string expected)
	{
		await Assert.That(FileName(template, XMen7((Fields.ReadPercentage, percent)))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("X", "X")]
	[Arguments("x-men", "X")]
	[Arguments("L'Homme", "H")]
	[Arguments("D'Artagnan", "A")]
	[Arguments("Les Misérables", "M")]
	[Arguments("the  avengers", "A")]
	[Arguments("The", "T")]
	[Arguments("Theatre", "T")]
	[Arguments("An Apple", "A")]
	[Arguments("A Bug", "B")]
	[Arguments("ångström", "Å")]
	public async Task First_letter(string series, string expected)
	{
		await Assert.That(FileName("{<first(Series)>}", XMen7((Fields.Series, series)))).IsEqualTo(expected);
	}

	[Test]
	public async Task First_letter_of_an_empty_field_writes_nothing()
	{
		await Assert.That(FileName("X{ <first(Format)>}", XMen7())).IsEqualTo("X");
	}

	[Test]
	[Arguments(0, "005")]
	[Arguments(1, "015")]
	[Arguments(12, "125")]
	public async Task A_counter_is_start_plus_step_times_the_ordinal(int ordinal, string expected)
	{
		await Assert.That(FileName("{<counter(5)(10)(3)>}", XMen7(), DefaultContext with { Ordinal = ordinal })).IsEqualTo(expected);
	}

	[Test]
	public async Task Two_counters_in_one_template_do_not_share_state()
	{
		await Assert.That(FileName("{<counter(1)(1)(0)>}{-<counter(100)(-1)(0)>}", XMen7(), DefaultContext with { Ordinal = 3 })).IsEqualTo("4-97");
	}

	[Test]
	public async Task A_date_with_no_format_is_invariant_iso()
	{
		BookRecord book = XMen7((Fields.ReleasedDate, new DateTime(1992, 3, 5, 14, 30, 0)));

		await Assert.That(FileName("{<ReleasedDate>}", book)).IsEqualTo("1992-03-05");
		await Assert.That(FileName("{<ReleasedDate(yyyy-MM)>}", book)).IsEqualTo("1992-03");
		await Assert.That(FileName("X{ <AddedDate>}", book)).IsEqualTo("X");
	}

	[Test]
	public async Task A_custom_value_is_read_by_key_and_cannot_split_a_folder()
	{
		BookRecord book = Book([(Fields.Series, "X-Men")], new Dictionary<string, string> { ["Shelf"] = @"Top\Left" });

		await Assert.That(Folder(@"{<Custom(Shelf)>}\{<series>}", book)).IsEqualTo(@"TopLeft\X-Men");
		await Assert.That(Folder(@"{<Custom(Other)>}\{<series>}", book)).IsEqualTo("X-Men");
	}

	[Test]
	public async Task A_multi_value_field_with_no_arguments_writes_its_raw_text()
	{
		await Assert.That(FileName("{<writer>}", XMen7())).IsEqualTo("Chris Claremont, Jim Lee");
	}

	[Test]
	public async Task A_backslash_in_a_group_of_a_folder_template_starts_a_level_only_when_the_group_shows()
	{
		const string template = @"{<publisher>\}{<imprint>\}{<series>}";

		await Assert.That(Folder(template, XMen7((Fields.Imprint, "Epic")))).IsEqualTo(@"Marvel\Epic\X-Men");
		await Assert.That(Folder(template, XMen7(), DefaultContext with { Style = PathStyle.Default with { EmptyFolderName = "_" } })).IsEqualTo(@"Marvel\X-Men");
	}

	[Test]
	public async Task A_backslash_in_a_file_name_template_is_cleaned_away()
	{
		await Assert.That(FileName(@"{<series>}\{<number>}", XMen7())).IsEqualTo("X-Men7");
	}
}
