using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;

namespace LibraryOrganizer.Plugin.Tests;

public class LibrarySnapshotTests
{
	[Test]
	public async Task The_series_index_sees_library_issues_outside_the_selection()
	{
		ComicBook selected = Issue("7", 1992, @"D:\In\x-men 7.cbz");
		ComicBook first = Issue("1", 1991, @"D:\Lib\x-men 1.cbz");
		ComicBook last = Issue("120", 2001, @"D:\Lib\x-men 120.cbz");
		ComicBook other = new() { Series = "Avengers", Publisher = "Marvel", Number = "500", FilePath = @"D:\Lib\avengers 500.cbz" };

		LibrarySnapshot snapshot = LibrarySnapshot.Take([selected], [selected, first, last, other]);
		SeriesKey key = snapshot.Selection.Single().SeriesKey;

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Series.First(key)!.Id).IsEqualTo(new BookId(first.Id));
			await Assert.That(snapshot.Series.Last(key)!.Id).IsEqualTo(new BookId(last.Id));
			await Assert.That(snapshot.Series.IssueCount(key)).IsEqualTo(3);
			await Assert.That(snapshot.LibraryFiles[AbsPath.TryParse(@"d:\lib\AVENGERS 500.cbz")!.Value]).IsEqualTo(new BookId(other.Id));
		}
	}

	[Test]
	public async Task A_selected_book_with_an_unusable_path_is_reported_not_planned()
	{
		ComicBook broken = new() { Series = "X", FilePath = @"relative\x.cbz" };

		LibrarySnapshot snapshot = LibrarySnapshot.Take([broken], [broken]);

		await Assert.That(snapshot.Selection).IsEmpty();
		await Assert.That(snapshot.Unreadable).IsEquivalentTo(new[] { broken.Caption });
	}

	private static ComicBook Issue(string number, int year, string path)
		=> new() { Series = "X-Men", Publisher = "Marvel", Volume = 2, Number = number, Year = year, FilePath = path };
}
