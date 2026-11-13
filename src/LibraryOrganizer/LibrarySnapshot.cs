using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine;

namespace LibraryOrganizer.Plugin;

/// <summary>Everything a plan reads from ComicRack, copied on the UI thread so planning can run on a worker.</summary>
/// <param name="Unreadable">Captions of selected books whose file path is not a usable absolute path.</param>
internal sealed record LibrarySnapshot(
	IReadOnlyList<BookRecord> Selection,
	SeriesIndex Series,
	IReadOnlyDictionary<AbsPath, BookId> LibraryFiles,
	ILookup<AbsPath, BookId> PathOwners,
	IReadOnlyList<string> Unreadable)
{
	public static LibrarySnapshot Take(IEnumerable<ComicBook> selection, IEnumerable<ComicBook> library)
	{
		var records = new List<BookRecord>();
		var unreadable = new List<string>();
		foreach (ComicBook book in selection)
		{
			if (BookSnapshot.Record(book) is BookRecord record)
			{
				records.Add(record);
			}
			else
			{
				unreadable.Add(book.Caption);
			}
		}

		var wanted = new HashSet<SeriesKey>(records.Select(record => record.SeriesKey));
		var selected = new HashSet<BookId>(records.Select(record => record.Id));
		var members = new List<BookRecord>(records);
		var files = new Dictionary<AbsPath, BookId>();
		var owners = new List<(AbsPath Path, BookId Book)>();
		foreach (ComicBook book in library)
		{
			if (book.IsLinked && AbsPath.TryParse(book.FilePath) is AbsPath path)
			{
				files[path] = new BookId(book.Id);
				owners.Add((path, new BookId(book.Id)));
			}
			if (!selected.Contains(new BookId(book.Id)) && wanted.Contains(BookSnapshot.KeyOf(book)) && BookSnapshot.Record(book) is BookRecord member)
			{
				members.Add(member);
			}
		}
		return new LibrarySnapshot(records, SeriesIndex.Build(members), files, owners.ToLookup(owner => owner.Path, owner => owner.Book), unreadable);
	}
}
