using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer.FormPreview;

internal sealed class RunSamples
{
	private readonly Dictionary<AbsPath, BookId> libraryFiles = [];

	private RunSamples(IReadOnlyList<Profile> runOrder, List<BookRecord> selection, SampleDisk disk)
	{
		RunOrder = runOrder;
		Selection = selection;
		Disk = disk;
	}

	public IReadOnlyList<Profile> RunOrder { get; }
	public List<BookRecord> Selection { get; }
	public SampleDisk Disk { get; }

	public static IReadOnlyList<ProfileSettings> ProfileSettingsList()
		=>
		[
			new ProfileSettings { Name = "Library", BaseFolder = @"D:\Comics", FolderTemplate = Folder, FileTemplate = FileName },
			new ProfileSettings
			{
				Name = "Image titles by publisher",
				BaseFolder = @"D:\Comics",
				FolderTemplate = "{<publisher>}",
				UseFileName = false,
				RuleMode = RuleMode.Only,
				Rules = new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition(Fields.Publisher.Key, Operator.Is, "Image")]),
			},
			new ProfileSettings { Name = "Backup copy", Action = new ProfileAction.Copy(AddToLibrary: false), BaseFolder = @"E:\Backup", FolderTemplate = "{<publisher>}", UseFileName = false },
			new ProfileSettings { Name = "Manga shelf", BaseFolder = @"D:\Manga", FolderTemplate = "{<series>}" },
		];

	private const string Folder = @"{<publisher>}\{<series>}";
	private const string FileName = "{<series>}{ #<number3>}{ (<writer(, )(issue)>)}";

	/// <summary>Two Move profiles: the second takes only Image books, so Saga is Overridden under the first.</summary>
	public static RunSamples Make()
	{
		IReadOnlyList<ProfileSettings> all = ProfileSettingsList();
		Profile[] order = [Compile(all[0]), Compile(all[1])];
		var disk = new SampleDisk();
		var samples = new RunSamples(order, [], disk);

		samples.Add(@"D:\Incoming\Sandman 1.cbz", "Sandman", "1", "Vertigo", "Neil Gaiman");
		samples.Add(@"D:\Incoming\Sandman 2.cbz", "Sandman", "2", "Vertigo", "Neil Gaiman");
		samples.Add(@"D:\Comics\Vertigo\Hellblazer\Hellblazer #001 (Jamie Delano).cbz", "Hellblazer", "1", "Vertigo", "Jamie Delano");
		samples.Add(@"D:\Incoming\Uncanny X-Men 1.cbz", "Uncanny X-Men", "1", "Marvel", "Chris Claremont, Len Wein");
		samples.Add(@"D:\Incoming\Saga 1.cbz", "Saga", "1", "Image", "Brian K. Vaughan");
		samples.Add(@"D:\Incoming\Preacher 1.cbz", "Preacher", "1", "Vertigo", "Garth Ennis");
		disk.With(@"D:\Comics\Vertigo\Preacher\Preacher #001 (Garth Ennis).cbz");
		samples.Add(@"D:\Incoming\Transmetropolitan 1.cbz", "Transmetropolitan", "1", "Vertigo", "Warren Ellis");
		samples.Add(@"D:\Incoming\More\Transmetropolitan 1 (scan 2).cbz", "Transmetropolitan", "1", "Vertigo", "Warren Ellis");
		samples.AddMissing(@"D:\Incoming\Lost Issue.cbz", "Y: The Last Man", "1", "Vertigo", "Brian K. Vaughan");
		return samples;
	}

	/// <summary>
	/// A large selection for the grid's responsiveness: many series, a few existing files for conflicts, no multi-value
	/// questions.
	/// </summary>
	public static RunSamples Large(int books)
	{
		Profile main = Compile(new ProfileSettings { Name = "Library", BaseFolder = @"D:\Comics", FolderTemplate = Folder, FileTemplate = "{<series>}{ #<number3>}" });
		var disk = new SampleDisk();
		var samples = new RunSamples([main], [], disk);
		for (int i = 0; i < books; i++)
		{
			string series = $"Series {i % 400:D3}";
			int number = i / 400 + 1;
			samples.Add($@"D:\Incoming\Box {i % 40}\{series} {number}.cbz", series, number.ToString(), i % 3 == 0 ? "Marvel" : "DC Comics", null);
			if (i % 997 == 0)
			{
				disk.With($@"D:\Comics\{(i % 3 == 0 ? "Marvel" : "DC Comics")}\{series}\{series} #{number:D3}.cbz");
			}
		}
		return samples;
	}

	public Plan Plan(Decisions decisions)
		=> Organizer.Plan(new OrganizeInput(RunOrder, Selection, SeriesIndex.Build(Selection), Disk, libraryFiles), decisions);

	private static Profile Compile(ProfileSettings settings)
	{
		CompileResult result = settings.Compile();
		return result.Profile ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Message)));
	}

	private void Add(string path, string series, string number, string publisher, string? writer)
	{
		BookRecord book = Book(path, series, number, publisher, writer);
		Disk.With(path);
		libraryFiles[AbsPath.TryParse(path)!.Value] = book.Id;
		Selection.Add(book);
	}

	private void AddMissing(string path, string series, string number, string publisher, string? writer)
		=> Selection.Add(Book(path, series, number, publisher, writer));

	private static BookRecord Book(string path, string series, string number, string publisher, string? writer)
	{
		FieldValue[] values = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		values[Fields.Series.Ordinal] = new FieldValue.Text(series);
		values[Fields.Publisher.Ordinal] = new FieldValue.Text(publisher);
		values[Fields.Number.Ordinal] = new FieldValue.Issue(new IssueNumber(number));
		values[Fields.Writer.Ordinal] = FieldValue.ParseItems(writer);
		return new BookRecord(
			new BookId(Guid.NewGuid()), new BookLocation.File(AbsPath.TryParse(path)!.Value, Exists: true), $"{series} #{number}", values, new Dictionary<string, string>());
	}
}

internal sealed class SampleDisk : IFileProbe
{
	private readonly HashSet<AbsPath> files = [];

	public void With(string path) => files.Add(AbsPath.TryParse(path)!.Value);

	public bool FileExists(AbsPath path) => files.Contains(path);

	public bool FolderExists(AbsPath path) => true;

	public string? ActualName(AbsPath path) => files.Contains(path) ? path.Name : null;

	public long? FileSize(AbsPath path) => files.Contains(path) ? 1 : null;

	public IReadOnlyList<AbsPath> Children(AbsPath folder) => [];
}
