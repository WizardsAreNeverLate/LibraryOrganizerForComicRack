using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class PathStyleTests
{
	private static Piece[] Pieces(params string?[] parts) => parts.Select(part => part is null ? (Piece)new Piece.Break() : new Piece.Text(part)).ToArray();

	private static string Folder(PathStyle style, params string?[] parts) => string.Join(@"\", style.CleanFolder(Pieces(parts)));

	private static string FileName(PathStyle style, params string?[] parts) => style.CleanFileName(Pieces(parts));

	private static PathStyle WithTable(params (string Key, string Value)[] table)
		=> PathStyle.Default with { IllegalCharacters = table.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value)).ToList() };

	[Test]
	public async Task The_default_table_is_the_2x_table()
	{
		await Assert.That(FileName(PathStyle.Default, "a?b/c\\d*e:f<g>h|i\"j")).IsEqualTo("abcde - f[g]h!i'j");
	}

	[Test]
	public async Task Only_a_break_starts_a_folder_level()
	{
		await Assert.That(Folder(PathStyle.Default, "Marvel", null, @"AC/DC \ Live")).IsEqualTo(@"Marvel\ACDC Live");
	}

	[Test]
	public async Task A_table_without_the_separators_still_cannot_let_one_through()
	{
		await Assert.That(Folder(WithTable(), @"a\b/c:d")).IsEqualTo("abcd");
		await Assert.That(Folder(WithTable(("&", "/")), "Cloak & Dagger")).IsEqualTo("Cloak Dagger");
	}

	[Test]
	public async Task The_longest_key_wins()
	{
		PathStyle style = WithTable((":", "-"), ("::", "="), (" :: ", " to "));

		await Assert.That(FileName(style, "a:b::c :: d")).IsEqualTo("a-b=c to d");
	}

	[Test]
	public async Task A_replacement_is_not_replaced_again()
	{
		await Assert.That(FileName(WithTable(("a", "b"), ("b", "c")), "ab")).IsEqualTo("bc");
	}

	[Test]
	public async Task A_key_can_match_across_two_pieces()
	{
		await Assert.That(FileName(WithTable(("--", "~")), "a-", "-b")).IsEqualTo("a~b");
	}

	[Test]
	public async Task Whitespace_runs_collapse_only_when_the_option_is_on()
	{
		await Assert.That(FileName(PathStyle.Default, "X-Men", ": Part  1")).IsEqualTo("X-Men - Part 1");
		await Assert.That(FileName(PathStyle.Default with { CollapseSpaces = false }, "X-Men", ": Part  1")).IsEqualTo("X-Men -  Part  1");
	}

	[Test]
	public async Task Whitespace_and_dots_are_trimmed_from_each_level_and_the_file_name()
	{
		await Assert.That(Folder(PathStyle.Default, " Marvel ", null, "Dr. ", null, "..etc. .")).IsEqualTo(@"Marvel\Dr\etc");
		await Assert.That(FileName(PathStyle.Default, " S.H.I.E.L.D. ")).IsEqualTo("S.H.I.E.L.D");
	}

	[Test]
	public async Task A_blank_level_is_dropped_when_there_is_no_empty_folder_name()
	{
		await Assert.That(Folder(PathStyle.Default, "Marvel", null, " . ", null, null, "X-Men", null)).IsEqualTo(@"Marvel\X-Men");
		await Assert.That(Folder(PathStyle.Default).Length).IsEqualTo(0);
	}

	[Test]
	public async Task A_blank_level_takes_the_empty_folder_name_cleaned_like_any_other()
	{
		await Assert.That(Folder(PathStyle.Default with { EmptyFolderName = "_Unknown" }, "Marvel", null, "", null, "X-Men")).IsEqualTo(@"Marvel\_Unknown\X-Men");
		await Assert.That(Folder(PathStyle.Default with { EmptyFolderName = @"No\Name?" }, "Marvel", null, null, "X-Men")).IsEqualTo(@"Marvel\NoName\X-Men");
		await Assert.That(Folder(PathStyle.Default with { EmptyFolderName = "?" }, "Marvel", null, null, "X-Men")).IsEqualTo(@"Marvel\X-Men");
	}

	[Test]
	public async Task A_blank_file_name_stays_blank()
	{
		await Assert.That(FileName(PathStyle.Default with { EmptyFolderName = "_Unknown" }, " ?. ")).IsEqualTo("");
	}

	[Test]
	public async Task Breaks_vanish_from_a_file_name()
	{
		await Assert.That(FileName(PathStyle.Default, "X-Men", null, " 7")).IsEqualTo("X-Men 7");
	}

	[Test]
	[Arguments("CON", "CON_")]
	[Arguments("nul", "nul_")]
	[Arguments("Aux", "Aux_")]
	[Arguments("PRN", "PRN_")]
	[Arguments("COM1", "COM1_")]
	[Arguments("COM9", "COM9_")]
	[Arguments("LPT1", "LPT1_")]
	[Arguments("lpt9", "lpt9_")]
	[Arguments("NUL.2", "NUL_.2")]
	[Arguments("CON: The Series", "CON - The Series")]
	[Arguments("CONSOLE", "CONSOLE")]
	[Arguments("COM0", "COM0")]
	[Arguments("COM10", "COM10")]
	[Arguments("LPT", "LPT")]
	public async Task A_reserved_windows_name_gets_an_underscore(string value, string expected)
	{
		await Assert.That(Folder(PathStyle.Default, "Marvel", null, value)).IsEqualTo(@"Marvel\" + expected);
		await Assert.That(FileName(PathStyle.Default, value)).IsEqualTo(expected);
	}

	[Test]
	public async Task A_name_that_cleans_down_to_a_reserved_name_is_guarded_too()
	{
		await Assert.That(FileName(PathStyle.Default, " N?U*L. ")).IsEqualTo("NUL_");
	}

	[Test]
	public async Task The_default_month_names_are_the_twelve_months_and_four_seasons()
	{
		await Assert.That(PathStyle.Default.MonthNames.Count).IsEqualTo(16);
		await Assert.That(PathStyle.Default.MonthNames[0]).IsEqualTo("January");
		await Assert.That(PathStyle.Default.MonthNames[15]).IsEqualTo("Winter");
	}
}
