using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibraryOrganizer.Tests;

/// <summary>A library in memory. It follows the gateway's contract: unknown ids do nothing, and AddCopy is keyed by path.</summary>
internal sealed class FakeLibrary : ILibraryGateway
{
	/// <summary>The file of every library book that has one.</summary>
	public Dictionary<BookId, AbsPath> Paths { get; } = [];
	public Dictionary<BookId, byte[]> Covers { get; } = [];
	public List<(BookId From, BookId To)> Transfers { get; } = [];
	public List<string> Calls { get; } = [];

	/// <summary>Runs at the start of every call, with the method name. A test cancels or throws from here.</summary>
	public Action<string>? Before { get; set; }

	public void FileMoved(BookId book, AbsPath newPath)
	{
		Enter(nameof(FileMoved));
		if (Paths.ContainsKey(book))
		{
			Paths[book] = newPath;
		}
	}

	public BookId AddCopy(BookId source, AbsPath copy)
	{
		Enter(nameof(AddCopy));
		foreach (KeyValuePair<BookId, AbsPath> book in Paths)
		{
			if (book.Value.Equals(copy))
			{
				return book.Key;
			}
		}
		var id = new BookId(Guid.NewGuid());
		Paths[id] = copy;
		return id;
	}

	public void Remove(BookId book)
	{
		Enter(nameof(Remove));
		Paths.Remove(book);
	}

	public void TransferReadProgress(BookId from, BookId to)
	{
		Enter(nameof(TransferReadProgress));
		Transfers.Add((from, to));
	}

	public byte[]? Cover(BookId book, CoverFormat format)
	{
		Enter(nameof(Cover));
		return Covers.TryGetValue(book, out byte[]? cover) ? cover : null;
	}

	private void Enter(string call)
	{
		Calls.Add(call);
		Before?.Invoke(call);
	}
}

/// <summary>The real disk, with a hook before and after every file change. The hook gets the method name.</summary>
internal sealed class HookedFileSystem(IFileSystem inner) : IFileSystem
{
	public Action<string>? Before { get; set; }
	public Action<string>? After { get; set; }

	/// <summary>
	/// Stops the run the way a killed process does: makes file change number <paramref name="changes"/>, then throws
	/// an exception the executor does not treat as a refused operation.
	/// </summary>
	public static HookedFileSystem DiesAfter(IFileSystem inner, int changes)
	{
		int made = 0;
		return new HookedFileSystem(inner)
		{
			After = _ =>
			{
				if (++made == changes)
				{
					throw new InvalidOperationException("The process was killed.");
				}
			},
		};
	}

	public bool FileExists(AbsPath path) => inner.FileExists(path);
	public bool FolderExists(AbsPath path) => inner.FolderExists(path);
	public string? ActualName(AbsPath path) => inner.ActualName(path);
	public long? FileSize(AbsPath path) => inner.FileSize(path);
	public IReadOnlyList<AbsPath> Children(AbsPath folder) => inner.Children(folder);
	public void CreateFolder(AbsPath folder) => inner.CreateFolder(folder);
	public void DeleteEmptyFolder(AbsPath folder) => inner.DeleteEmptyFolder(folder);

	public void Move(AbsPath from, AbsPath to) => Change(nameof(Move), () => inner.Move(from, to));
	public void Copy(AbsPath from, AbsPath to) => Change(nameof(Copy), () => inner.Copy(from, to));
	public void WriteNew(AbsPath to, byte[] content) => Change(nameof(WriteNew), () => inner.WriteNew(to, content));
	public void Recycle(AbsPath file) => Change(nameof(Recycle), () => inner.Recycle(file));

	private void Change(string name, Action change)
	{
		Before?.Invoke(name);
		change();
		After?.Invoke(name);
	}
}

/// <summary>
/// A real folder with a library that agrees with it. Comic files live under "lib", journals under "data". Paths are
/// given relative to "lib".
/// </summary>
internal sealed class RealShelf : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "lo-run-" + Guid.NewGuid().ToString("N"));
	private DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public RealShelf(int keepRuns = 10)
	{
		Directory.CreateDirectory(Lib);
		Journals = new JournalStore(Path.Combine(root, "data"), keepRuns, () => now = now.AddSeconds(1));
	}

	public WindowsFileSystem Disk { get; } = new();
	public FakeLibrary Library { get; } = new();
	public JournalStore Journals { get; }
	public List<BookRecord> Selection { get; } = [];

	public string Lib => Path.Combine(root, "lib");
	public string RunsFolder => Path.Combine(root, "data", "runs");

	/// <summary>The time the next journal will carry.</summary>
	public DateTime NextStart => now.AddSeconds(1);

	public void Dispose() => Directory.Delete(root, true);

	public AbsPath P(string relative) => AbsPath.TryParse(Path.Combine(Lib, relative))!.Value;

	/// <summary>A profile whose base folder is "lib\folder".</summary>
	public Profile Profile(string name = "Main", string folder = "Comics", Func<ProfileSettings, ProfileSettings>? edit = null)
		=> TestProfiles.Make(name, Path.Combine(Lib, folder), edit);

	/// <summary>A selected library book with a file whose content names it.</summary>
	public BookRecord Add(string relative, string number, string series = "X-Men")
	{
		BookRecord book = TestBooks.Book(series: series, number: number, path: P(relative).Value);
		Put(relative, ContentOf(relative));
		Library.Paths[book.Id] = P(relative);
		Selection.Add(book);
		return book;
	}

	public static string ContentOf(string relative) => "content of " + relative;

	public void Put(string relative, string content)
	{
		Directory.CreateDirectory(P(relative).Parent.Value);
		File.WriteAllText(P(relative).Value, content);
	}

	public string Read(string relative) => File.ReadAllText(P(relative).Value);

	/// <summary>Every file under "lib" with its spelling on the disk, relative, sorted, one per line.</summary>
	public string Files() => Listing(Directory.EnumerateFiles(Lib, "*", SearchOption.AllDirectories));

	public string Folders() => Listing(Directory.EnumerateDirectories(Lib, "*", SearchOption.AllDirectories));

	/// <summary>The path of every library book, relative, sorted, one per line.</summary>
	public string LibraryFiles() => Listing(Library.Paths.Values.Select(path => path.Value));

	public string PathOf(BookRecord book) => Library.Paths[book.Id].Value.Substring(Lib.Length + 1);

	public Plan Plan(Decisions decisions, params Profile[] runOrder)
		=> Organizer.Plan(
			new OrganizeInput(runOrder, Selection, SeriesIndex.Build(Selection), Disk, Library.Paths.ToDictionary(pair => pair.Value, pair => pair.Key)),
			decisions);

	public Plan Plan(params Profile[] runOrder) => Plan(Decisions.None, runOrder);

	public Plan PlanUndo(RunId run, Decisions? decisions = null)
		=> Organizer.PlanUndo(Journals.Read(run), Disk, Library.Paths.ToDictionary(pair => pair.Value, pair => pair.Key), decisions ?? Decisions.None);

	public Executor Executor(IFileSystem? disk = null) => new(disk ?? Disk, Library, Journals);

	public RunReport Run(Plan plan) => Executor().Run(plan.Approve()!, null, default);

	/// <summary>Runs the plan on a disk that dies after the given number of file changes. Returns the run to resume.</summary>
	public RunId RunUntilKilled(Plan plan, int afterChanges)
	{
		ApprovedPlan approved = plan.Approve()!;
		try
		{
			Executor(HookedFileSystem.DiesAfter(Disk, afterChanges)).Run(approved, null, default);
		}
		catch (InvalidOperationException)
		{
			return approved.Id;
		}
		throw new InvalidOperationException($"The run made fewer than {afterChanges} file changes.");
	}

	/// <summary>Readable while the run is still writing it.</summary>
	public string JournalText(RunId run)
	{
		using var stream = new FileStream(JournalPath(run), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	public string JournalPath(RunId run) => Directory.GetFiles(RunsFolder, $"*-{run.Value:N}.journal").Single();

	private string Listing(IEnumerable<string> paths)
		=> string.Join("\n", paths.Select(path => path.Substring(Lib.Length + 1)).OrderBy(path => path, StringComparer.Ordinal));
}
