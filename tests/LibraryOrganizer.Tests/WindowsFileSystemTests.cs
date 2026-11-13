using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public sealed class WindowsFileSystemTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "lo-fs-" + Guid.NewGuid().ToString("N"));
	private readonly WindowsFileSystem disk = new();

	public WindowsFileSystemTests() => Directory.CreateDirectory(root);

	public void Dispose() => Directory.Delete(root, true);

	private AbsPath P(string relative) => AbsPath.TryParse(Path.Combine(root, relative))!.Value;

	private void Touch(string relative, string content = "x")
	{
		Directory.CreateDirectory(Path.GetDirectoryName(P(relative).Value)!);
		File.WriteAllText(P(relative).Value, content);
	}

	[Test]
	public async Task Move_creates_nothing_extra_and_refuses_to_overwrite()
	{
		Touch("a.cbz", "a");
		Touch("b.cbz", "b");
		disk.CreateFolder(P(@"new\deep"));

		disk.Move(P("a.cbz"), P(@"new\deep\a.cbz"));

		await Assert.That(File.ReadAllText(P(@"new\deep\a.cbz").Value)).IsEqualTo("a");
		await Assert.That(disk.FileExists(P("a.cbz"))).IsFalse();
		await Assert.That(() => disk.Move(P("b.cbz"), P(@"new\deep\a.cbz"))).Throws<IOException>();
		await Assert.That(File.ReadAllText(P(@"new\deep\a.cbz").Value)).IsEqualTo("a");
	}

	[Test]
	public async Task Copy_refuses_to_overwrite_and_leaves_no_partial_file()
	{
		Touch("a.cbz", "a");
		Touch("b.cbz", "b");

		disk.Copy(P("a.cbz"), P("c.cbz"));

		await Assert.That(File.ReadAllText(P("c.cbz").Value)).IsEqualTo("a");
		await Assert.That(() => disk.Copy(P("a.cbz"), P("b.cbz"))).Throws<IOException>();
		await Assert.That(File.ReadAllText(P("b.cbz").Value)).IsEqualTo("b");
		await Assert.That(Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(n => n)).IsEquivalentTo(new[] { "a.cbz", "b.cbz", "c.cbz" });
	}

	[Test]
	public async Task ActualName_reports_the_on_disk_spelling()
	{
		Touch("X-Men 01.cbz");

		await Assert.That(disk.ActualName(P("x-men 01.CBZ"))).IsEqualTo("X-Men 01.cbz");
		await Assert.That(disk.ActualName(P("missing.cbz"))).IsNull();
	}

	[Test]
	public async Task DeleteEmptyFolder_keeps_a_folder_that_has_content()
	{
		Touch(@"full\a.cbz");
		Directory.CreateDirectory(P("empty").Value);

		disk.DeleteEmptyFolder(P("full"));
		disk.DeleteEmptyFolder(P("empty"));

		await Assert.That(disk.FolderExists(P("full"))).IsTrue();
		await Assert.That(disk.FolderExists(P("empty"))).IsFalse();
	}

	[Test]
	public async Task Recycle_removes_the_file()
	{
		Touch("gone.cbz");

		disk.Recycle(P("gone.cbz"));

		await Assert.That(disk.FileExists(P("gone.cbz"))).IsFalse();
	}

	[Test]
	public async Task WriteNew_refuses_to_overwrite()
	{
		disk.WriteNew(P("cover.jpg"), [1, 2, 3]);

		await Assert.That(File.ReadAllBytes(P("cover.jpg").Value)).IsEquivalentTo(new byte[] { 1, 2, 3 });
		await Assert.That(() => disk.WriteNew(P("cover.jpg"), [9])).Throws<IOException>();
	}
}
