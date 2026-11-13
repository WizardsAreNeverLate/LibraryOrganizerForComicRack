using System;
using System.Collections.Generic;
using System.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace LibraryOrganizer.Plugin.Tests;

/// <summary>The library half of ComicRack's IApplication; the rest is never called by the plugin's library code.</summary>
internal sealed class FakeApplication(params ComicBook[] library) : IApplication
{
	public List<ComicBook> Library { get; } = [.. library];

	public string ProductVersion => "test";
	public IEnumerable<ComicBook> GetLibraryBooks() => Library;

	public ComicBook AddNewBook(bool showDialog)
	{
		var book = new ComicBook { AddedTime = DateTime.Now };
		Library.Add(book);
		return book;
	}

	public bool RemoveBook(ComicBook cb) => Library.Remove(cb);
	public Bitmap GetComicThumbnail(ComicBook cb, int page) => new(4, 6);

	public void Restart() => throw new NotSupportedException();
	public void SynchronizeDevices() => throw new NotSupportedException();
	public void ScanFolders() => throw new NotSupportedException();
	public IEnumerable<ComicBook> ReadDatabaseBooks(string file) => throw new NotSupportedException();
	public bool SetCustomBookThumbnail(ComicBook cb, Bitmap bitmap) => throw new NotSupportedException();
	public ComicBook GetBook(string file) => throw new NotSupportedException();
	public Bitmap GetComicPage(ComicBook cb, int page) => throw new NotSupportedException();
	public IDictionary<string, string> GetComicFields() => throw new NotSupportedException();
	public Bitmap GetComicPublisherIcon(ComicBook cb) => throw new NotSupportedException();
	public Bitmap GetComicImprintIcon(ComicBook cb) => throw new NotSupportedException();
	public Bitmap GetComicAgeRatingIcon(ComicBook cb) => throw new NotSupportedException();
	public Bitmap GetComicFormatIcon(ComicBook cb) => throw new NotSupportedException();
	public string ReadInternet(string url) => throw new NotSupportedException();
	public int AskQuestion(string question, string buttonText, string optionText) => throw new NotSupportedException();
	public void ShowComicInfo(IEnumerable<ComicBook> books) => throw new NotSupportedException();
}
