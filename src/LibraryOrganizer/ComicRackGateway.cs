using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace LibraryOrganizer.Plugin;

/// <summary>
/// Library writes for the executor. The executor runs on a worker; every call here is sent to the UI thread, the only
/// thread that touches a ComicBook. Create it on the UI thread.
/// </summary>
internal sealed class ComicRackGateway : ILibraryGateway
{
	private readonly IApplication app;
	private readonly SynchronizationContext ui;
	private readonly IReadOnlyList<ComicBook> outside;
	private Dictionary<BookId, ComicBook>? books;

	/// <param name="selection">Books the run was given. Books from the folder view are not in the library, and only this
	/// list lets them follow their files.</param>
	public ComicRackGateway(IApplication app, IEnumerable<ComicBook>? selection = null)
	{
		this.app = app;
		outside = [.. selection ?? []];
		ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the gateway on ComicRack's UI thread.");
	}

	public void FileMoved(BookId book, AbsPath newPath) => OnUi(() =>
	{
		if (Find(book) is ComicBook comic)
		{
			comic.FilePath = newPath.Value;
		}
	});

	public BookId AddCopy(BookId source, AbsPath copy) => OnUi(() =>
	{
		ComicBook? existing = Library().Values.FirstOrDefault(b => AbsPath.TryParse(b.FilePath) is AbsPath path && path.Equals(copy));
		if (existing is not null)
		{
			return new BookId(existing.Id);
		}
		ComicBook added = app.AddNewBook(false);
		if (Find(source) is ComicBook original)
		{
			// Not ComicBook.CopyFrom: it also copies Id, FilePath and read state, which belong to the original.
			added.SetInfo(original, onlyUpdateEmpty: false);
			added.Rating = original.Rating;
			added.SeriesComplete = original.SeriesComplete;
			added.ReleasedTime = original.ReleasedTime;
			foreach (var pair in original.GetCustomValues())
			{
				added.SetCustomValue(pair.Key, pair.Value);
			}
		}
		added.FilePath = copy.Value;
		Library()[new BookId(added.Id)] = added;
		return new BookId(added.Id);
	});

	public void Remove(BookId book) => OnUi(() =>
	{
		if (Find(book) is ComicBook comic)
		{
			app.RemoveBook(comic);
			Library().Remove(book);
		}
	});

	public void TransferReadProgress(BookId from, BookId to) => OnUi(() =>
	{
		if (Find(from) is ComicBook source && Find(to) is ComicBook target)
		{
			target.LastPageRead = source.LastPageRead;
		}
	});

	public byte[]? Cover(BookId book, CoverFormat format) => OnUi(() =>
	{
		if (Find(book) is not ComicBook comic)
		{
			return null;
		}
		using Bitmap? image = app.GetComicThumbnail(comic, 0);
		if (image is null)
		{
			return null;
		}
		using var stream = new MemoryStream();
		image.Save(stream, format switch
		{
			CoverFormat.Png => ImageFormat.Png,
			CoverFormat.Bmp => ImageFormat.Bmp,
			_ => ImageFormat.Jpeg,
		});
		return stream.ToArray();
	});

	private ComicBook? Find(BookId id) => Library().TryGetValue(id, out ComicBook? book) ? book : null;

	private Dictionary<BookId, ComicBook> Library() => books ??= app.GetLibraryBooks().Concat(outside).GroupBy(b => b.Id).ToDictionary(g => new BookId(g.Key), g => g.First());

	private void OnUi(Action action) => OnUi<object?>(() =>
	{
		action();
		return null;
	});

	private T OnUi<T>(Func<T> func)
	{
		if (SynchronizationContext.Current == ui)
		{
			return func();
		}
		T result = default!;
		Exception? error = null;
		ui.Send(_ =>
		{
			try
			{
				result = func();
			}
			catch (Exception e)
			{
				error = e;
			}
		}, null);
		return error is null ? result : throw new InvalidOperationException("ComicRack refused a library change: " + error.Message, error);
	}
}
