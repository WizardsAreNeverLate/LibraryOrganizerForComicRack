using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class AbsPathTests
{
	private static AbsPath Parse(string raw) => AbsPath.TryParse(raw) ?? throw new ArgumentException($"'{raw}' did not parse.");

	[Test]
	[Arguments(@"D:\Comics\Marvel\", @"D:\Comics\Marvel")]
	[Arguments("D:/Comics/Marvel", @"D:\Comics\Marvel")]
	[Arguments(@"d:\Comics", @"D:\Comics")]
	[Arguments(@"D:\", @"D:\")]
	[Arguments(@"D:\Comics\.\Marvel\..\DC", @"D:\Comics\DC")]
	[Arguments(@"\\nas\comics\Marvel\", @"\\nas\comics\Marvel")]
	[Arguments(@"\\nas\comics", @"\\nas\comics")]
	public async Task Parse_normalizes(string raw, string expected)
		=> await Assert.That(Parse(raw).Value).IsEqualTo(expected);

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments(@"Comics\Marvel")]
	[Arguments(@"\Comics")]
	[Arguments("D:Comics")]
	[Arguments(@"D:\Comics\X-Men: Legacy")]
	[Arguments(@"D:\Comics\What If?")]
	[Arguments(@"D:\Comics\a|b")]
	[Arguments(@"D:\..\Comics")]
	[Arguments(@"\\nas")]
	public async Task Parse_rejects_relative_and_invalid_paths(string raw)
		=> await Assert.That(AbsPath.TryParse(raw)).IsNull();

	[Test]
	public async Task IsUnder_matches_whole_segments_only()
	{
		AbsPath manga = Parse(@"D:\Manga");

		await Assert.That(Parse(@"D:\Manga Extra\x.cbz").IsUnder(manga)).IsFalse();
		await Assert.That(Parse(@"D:\Manga\x.cbz").IsUnder(manga)).IsTrue();
		await Assert.That(Parse(@"D:\Manga\Deep\er\x.cbz").IsUnder(manga)).IsTrue();
		await Assert.That(manga.IsUnder(manga)).IsTrue();
		await Assert.That(manga.IsUnder(Parse(@"D:\Manga\x.cbz"))).IsFalse();
	}

	[Test]
	public async Task IsUnder_ignores_case_and_handles_roots()
	{
		await Assert.That(Parse(@"D:\Comics\Manga\x.cbz").IsUnder(Parse(@"d:\comics\manga"))).IsTrue();
		await Assert.That(Parse(@"D:\Manga\x.cbz").IsUnder(Parse(@"D:\"))).IsTrue();
		await Assert.That(Parse(@"E:\Manga\x.cbz").IsUnder(Parse(@"D:\"))).IsFalse();
		await Assert.That(Parse(@"\\nas\comics\Manga\x.cbz").IsUnder(Parse(@"\\NAS\Comics"))).IsTrue();
		await Assert.That(Parse(@"\\nas\comics2\x.cbz").IsUnder(Parse(@"\\nas\comics"))).IsFalse();
	}

	[Test]
	public async Task Equality_ignores_case()
	{
		AbsPath upper = Parse(@"D:\Comics\X-MEN.cbz");
		AbsPath lower = Parse(@"d:\comics\x-men.cbz");

		await Assert.That(upper == lower).IsTrue();
		await Assert.That(upper.GetHashCode()).IsEqualTo(lower.GetHashCode());
		await Assert.That(new HashSet<AbsPath> { upper }.Contains(lower)).IsTrue();
		await Assert.That(upper == Parse(@"D:\Comics\X-Men 2.cbz")).IsFalse();
	}

	[Test]
	public async Task DiffersOnlyByCase_is_true_only_for_a_respelling_of_the_same_path()
	{
		AbsPath path = Parse(@"D:\Comics\X-Men.cbz");

		await Assert.That(path.DiffersOnlyByCase(Parse(@"D:\Comics\x-men.cbz"))).IsTrue();
		await Assert.That(path.DiffersOnlyByCase(Parse(@"D:\Comics\X-Men.cbz"))).IsFalse();
		await Assert.That(path.DiffersOnlyByCase(Parse(@"d:\Comics\X-Men.cbz"))).IsFalse();
		await Assert.That(path.DiffersOnlyByCase(Parse(@"D:\Comics\X-Men 2.cbz"))).IsFalse();
	}

	[Test]
	public async Task Parent_Name_and_Extension_split_the_path()
	{
		AbsPath path = Parse(@"D:\Comics\Marvel\X-Men #007.cbz");

		await Assert.That(path.Name).IsEqualTo("X-Men #007.cbz");
		await Assert.That(path.Extension).IsEqualTo(".cbz");
		await Assert.That(path.Parent.Value).IsEqualTo(@"D:\Comics\Marvel");
		await Assert.That(Parse(@"D:\Comics").Parent.Value).IsEqualTo(@"D:\");
		await Assert.That(Parse(@"D:\Comics").Extension).IsEqualTo("");
		await Assert.That(Parse(@"\\nas\comics\Marvel").Parent.Value).IsEqualTo(@"\\nas\comics");
	}

	[Test]
	public async Task A_root_is_its_own_parent()
	{
		AbsPath drive = Parse(@"D:\");
		AbsPath share = Parse(@"\\nas\comics");

		await Assert.That(drive.IsRoot).IsTrue();
		await Assert.That(drive.Parent.Value).IsEqualTo(@"D:\");
		await Assert.That(drive.Name).IsEqualTo("");
		await Assert.That(share.IsRoot).IsTrue();
		await Assert.That(share.Parent.Value).IsEqualTo(@"\\nas\comics");
		await Assert.That(Parse(@"D:\Comics").IsRoot).IsFalse();
	}

	[Test]
	public async Task Combine_appends_one_segment()
	{
		await Assert.That(Parse(@"D:\Comics").Combine("Marvel").Value).IsEqualTo(@"D:\Comics\Marvel");
		await Assert.That(Parse(@"D:\").Combine("Marvel").Value).IsEqualTo(@"D:\Marvel");
		await Assert.That(Parse(@"\\nas\comics").Combine("Marvel").Value).IsEqualTo(@"\\nas\comics\Marvel");
	}

	[Test]
	[Arguments("")]
	[Arguments(@"Marvel\X-Men")]
	[Arguments("..")]
	[Arguments("What If?")]
	public async Task Combine_rejects_anything_but_a_single_valid_name(string name)
		=> await Assert.That(() => Parse(@"D:\Comics").Combine(name)).Throws<ArgumentException>();
}
