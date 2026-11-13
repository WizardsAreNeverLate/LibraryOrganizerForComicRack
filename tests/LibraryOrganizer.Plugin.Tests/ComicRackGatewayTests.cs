using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;

namespace LibraryOrganizer.Plugin.Tests;

public class ComicRackGatewayTests
{
	private static AbsPath P(string path) => AbsPath.TryParse(path)!.Value;

	private static ComicRackGateway Gateway(FakeApplication app)
	{
		SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
		return new ComicRackGateway(app);
	}

	[Test]
	public async Task A_moved_file_updates_the_library_book_and_an_unknown_id_is_ignored()
	{
		var book = new ComicBook { Series = "X-Men", FilePath = @"D:\In\x.cbz" };
		ComicRackGateway gateway = Gateway(new FakeApplication(book));

		gateway.FileMoved(new BookId(book.Id), P(@"D:\Comics\X-Men\x.cbz"));
		gateway.FileMoved(new BookId(Guid.NewGuid()), P(@"D:\Elsewhere\y.cbz"));

		await Assert.That(book.FilePath).IsEqualTo(@"D:\Comics\X-Men\x.cbz");
	}

	[Test]
	public async Task A_copy_gets_the_metadata_but_its_own_id_path_and_read_state()
	{
		var original = new ComicBook { Series = "X-Men", Number = "7", Writer = "Chris Claremont", Rating = 4, FilePath = @"D:\In\x.cbz", LastPageRead = 12 };
		original.SetCustomValue("Source", "shop");
		var app = new FakeApplication(original);
		ComicRackGateway gateway = Gateway(app);

		BookId copyId = gateway.AddCopy(new BookId(original.Id), P(@"E:\Backup\x.cbz"));

		ComicBook copy = app.Library.Single(b => b.Id == copyId.Value);
		using (Assert.Multiple())
		{
			await Assert.That(copy.Id).IsNotEqualTo(original.Id);
			await Assert.That(copy.FilePath).IsEqualTo(@"E:\Backup\x.cbz");
			await Assert.That(copy.Series).IsEqualTo("X-Men");
			await Assert.That(copy.Writer).IsEqualTo("Chris Claremont");
			await Assert.That(copy.Rating).IsEqualTo(4f);
			await Assert.That(copy.GetCustomValue("Source")).IsEqualTo("shop");
			await Assert.That(copy.LastPageRead).IsNotEqualTo(12);
			await Assert.That(original.FilePath).IsEqualTo(@"D:\In\x.cbz");
		}
	}

	[Test]
	public async Task Adding_a_copy_twice_returns_the_first_one()
	{
		var original = new ComicBook { Series = "X-Men", FilePath = @"D:\In\x.cbz" };
		var app = new FakeApplication(original);
		ComicRackGateway gateway = Gateway(app);

		BookId first = gateway.AddCopy(new BookId(original.Id), P(@"E:\Backup\x.cbz"));
		BookId again = gateway.AddCopy(new BookId(original.Id), P(@"e:\backup\X.cbz"));

		await Assert.That(again).IsEqualTo(first);
		await Assert.That(app.Library.Count).IsEqualTo(2);
	}

	[Test]
	public async Task Remove_and_read_progress_act_on_library_books()
	{
		var old = new ComicBook { FilePath = @"D:\C\x.cbz", LastPageRead = 20 };
		var incoming = new ComicBook { FilePath = @"D:\In\x.cbz" };
		var app = new FakeApplication(old, incoming);
		ComicRackGateway gateway = Gateway(app);

		gateway.TransferReadProgress(new BookId(old.Id), new BookId(incoming.Id));
		gateway.Remove(new BookId(old.Id));

		await Assert.That(incoming.LastPageRead).IsEqualTo(20);
		await Assert.That(app.Library).DoesNotContain(old);
	}

	[Test]
	public async Task A_cover_is_encoded_in_the_requested_format()
	{
		var book = new ComicBook { CustomThumbnailKey = "k" };
		ComicRackGateway gateway = Gateway(new FakeApplication(book));

		byte[]? png = gateway.Cover(new BookId(book.Id), CoverFormat.Png);

		await Assert.That(png!.Take(4).ToArray()).IsEquivalentTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
		await Assert.That(gateway.Cover(new BookId(Guid.NewGuid()), CoverFormat.Png)).IsNull();
	}
}
