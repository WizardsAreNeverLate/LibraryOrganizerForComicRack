using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using LibraryOrganizer.Plugin.Forms.Execution;

namespace LibraryOrganizer.Plugin.Tests;

public sealed class PipelineTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "lo-pipe-" + Guid.NewGuid().ToString("N"));

	public PipelineTests() => Directory.CreateDirectory(Path.Combine(root, "In"));

	public void Dispose() => Directory.Delete(root, true);

	[Test]
	public async Task ComicBooks_are_planned_moved_on_disk_and_reported()
	{
		ComicBook seven = Book("x-men 7.cbz", "7", 1992, 3);
		ComicBook first = Book("x-men 1.cbz", "1", 1991, 10);
		var app = new FakeApplication(seven, first);
		SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
		Profile profile = new ProfileSettings { Name = "Main", BaseFolder = Path.Combine(root, "Comics") }.Compile().Profile!;

		LibrarySnapshot snapshot = LibrarySnapshot.Take([seven], app.Library);
		var disk = new WindowsFileSystem();
		Plan plan = Organizer.Plan(new OrganizeInput([profile], snapshot.Selection, snapshot.Series, disk, snapshot.LibraryFiles), Decisions.None);
		RunReport report = new Executor(disk, new ComicRackGateway(app), new JournalStore(Path.Combine(root, "data"), 5))
			.Run(plan.Approve()!, null, CancellationToken.None);

		string expected = Path.Combine(root, @"Comics\Marvel\X-Men (1991)\X-Men Vol.2 #07 (of 12) (March, 1992).cbz");
		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(expected)).IsTrue();
			await Assert.That(File.Exists(Path.Combine(root, @"In\x-men 7.cbz"))).IsFalse();
			await Assert.That(seven.FilePath).IsEqualTo(expected);
			await Assert.That(first.FilePath).IsEqualTo(Path.Combine(root, @"In\x-men 1.cbz"));
			await Assert.That(ReportRows.From(report).Select(r => (r.Status, r.Detail))).IsEquivalentTo(new[] { (ReportStatus.Done, expected) });
		}
	}

	private ComicBook Book(string name, string number, int year, int month)
	{
		string path = Path.Combine(root, "In", name);
		File.WriteAllText(path, number);
		return new ComicBook { Series = "X-Men", Publisher = "Marvel", Volume = 2, Number = number, Count = 12, Year = year, Month = month, FilePath = path };
	}
}
