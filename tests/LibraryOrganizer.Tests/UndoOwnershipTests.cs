using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public sealed class UndoOwnershipTests : IDisposable
{
	private const string Seven = @"Incoming\seven.cbz";
	private const string Other = @"Incoming\other.cbz";
	private const string SevenOrganized = @"Comics\Marvel\X-Men\X-Men #7.cbz";

	private readonly RealShelf shelf = new();

	public void Dispose() => shelf.Dispose();

	[Test]
	public async Task Undo_leaves_another_books_file_when_the_first_book_was_moved_away_outside_ComicRack()
	{
		BookRecord other = shelf.Add(Other, "7");
		shelf.Selection.Clear();
		BookRecord seven = shelf.Add(Seven, "7");
		RunReport first = shelf.Run(shelf.Plan(shelf.Profile()));
		System.IO.File.Move(shelf.P(SevenOrganized).Value, shelf.P("Elsewhere.cbz").Value);
		shelf.Selection.Clear();
		shelf.Selection.Add(other);
		RunReport second = shelf.Run(shelf.Plan(shelf.Profile()));
		await Assert.That(second.Results.Values.All(result => result is OpResult.Done)).IsTrue();
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Other));

		Plan undo = shelf.PlanUndo(first.Run);

		await Assert.That(undo.Operations.Script()).IsEqualTo("");
	}

	[Test]
	public async Task Undo_leaves_the_file_when_a_second_library_entry_names_the_same_path()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Library.Paths[new BookId(Guid.NewGuid())] = shelf.P(SevenOrganized);
		RunReport first = shelf.Run(shelf.Plan(shelf.Profile()));
		await Assert.That(first.Results.Values.All(result => result is OpResult.Done)).IsTrue();
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));

		Plan undo = shelf.PlanUndo(first.Run);

		await Assert.That(undo.Operations.Script()).IsEqualTo("");
		await Assert.That(undo.Entries.Select(e => e.Outcome).OfType<EntryOutcome.Skipped>().Single().Reason).Contains("Another library book");
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	public async Task Resume_of_a_replace_killed_after_n_changes_ends_like_a_clean_run(int changes)
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Put(SevenOrganized, "the old copy");
		RunId run = shelf.RunUntilKilled(shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), shelf.Profile()), changes);

		RunReport report = shelf.Executor().Resume(run, null, default);

		await Assert.That(report.Results.Values.Any(result => result is OpResult.Failed)).IsFalse();
		await Assert.That(shelf.Files()).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(SevenOrganized);
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	public async Task Undo_of_a_replace_killed_after_n_changes_puts_the_book_back(int changes)
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Put(SevenOrganized, "the old copy");
		RunId run = shelf.RunUntilKilled(shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), shelf.Profile()), changes);

		shelf.Run(shelf.PlanUndo(run));

		await Assert.That(shelf.Files()).IsEqualTo(Seven);
		await Assert.That(shelf.Read(Seven)).IsEqualTo(RealShelf.ContentOf(Seven));
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	public async Task Resume_of_a_copy_replace_killed_after_n_changes_ends_like_a_clean_run(int changes)
	{
		const string Backup = @"Backup\Marvel\X-Men\X-Men #7.cbz";
		shelf.Add(Seven, "7");
		shelf.Put(Backup, RealShelf.ContentOf(Seven).ToUpperInvariant());
		Profile backup = shelf.Profile("Backup", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: true) });
		RunId run = shelf.RunUntilKilled(shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), backup), changes);

		RunReport report = shelf.Executor().Resume(run, null, default);

		await Assert.That(report.Results.Values.Any(result => result is OpResult.Failed)).IsFalse();
		await Assert.That(shelf.Read(Backup)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.LibraryFiles().Split('\n').Length).IsEqualTo(2);
	}

	[Test]
	public async Task A_journal_written_before_the_change_reads_a_case_rename()
	{
		const string LowerCase = @"Comics\Marvel\X-Men\x-men #7.cbz";
		shelf.Add(LowerCase, "7");
		RunReport report = shelf.Run(shelf.Plan(shelf.Profile()));

		await Assert.That(shelf.JournalText(report.Run)).DoesNotContain("lo-rename");
		await Assert.That(shelf.Journals.Read(report.Run).Operations.OfType<Operation.RenameCase>().Single().Temporary.Name).IsEqualTo("X-Men #7.cbz.lo-rename");
	}
}
