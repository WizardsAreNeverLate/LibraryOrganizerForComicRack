namespace LibraryOrganizer;

/// <summary>
/// The library side of operations. The executor calls it synchronously from its own thread, one call at a time, and
/// waits for each call. The plugin's implementation marshals every call to the UI thread; tests use an in-memory one.
/// </summary>
/// <remarks>
/// Two rules make a resumed run safe, because a crash can fall between a library write and its journal mark, so the
/// executor repeats the write:
/// <list type="bullet">
/// <item>An id the library does not know is not an error. The call does nothing (a book the user removed, a file that
/// was never in the library, a book already removed by the first attempt). <see cref="AddCopy"/> then adds the copy
/// with no metadata, and <see cref="Cover"/> returns null.</item>
/// <item>Every call can be repeated with the same arguments and leave the library as one call does.</item>
/// </list>
/// An exception from a call ends the run where it is, with the journal left open for a resume.
/// </remarks>
public interface ILibraryGateway
{
	/// <summary>The book's file is now at <paramref name="newPath"/>, with that exact spelling.</summary>
	void FileMoved(BookId book, AbsPath newPath);

	/// <summary>
	/// Adds a library book for a copy of <paramref name="source"/>'s file, with the source's metadata, a new id and
	/// <paramref name="copy"/> as its path. Returns the new id. When the library already has a book at that path,
	/// returns that book's id and adds nothing.
	/// </summary>
	BookId AddCopy(BookId source, AbsPath copy);

	/// <summary>Takes the book out of the library. Its file is not touched.</summary>
	void Remove(BookId book);

	/// <summary>Gives <paramref name="to"/> the read progress of <paramref name="from"/>.</summary>
	void TransferReadProgress(BookId from, BookId to);

	/// <summary>The book's custom cover encoded in the format. Null when the book has none.</summary>
	byte[]? Cover(BookId book, CoverFormat format);
}
