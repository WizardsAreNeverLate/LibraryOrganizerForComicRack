using System;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public sealed class UndoTests : IDisposable
{
	private const string Seven = @"Incoming\seven.cbz";
	private const string Eight = @"Incoming\eight.cbz";
	private const string SevenOrganized = @"Comics\Marvel\X-Men\X-Men #7.cbz";
	private const string SevenBackup = @"Backup\Marvel\X-Men\X-Men #7.cbz";

	private readonly RealShelf shelf = new();

	public void Dispose() => shelf.Dispose();

	private static string Lines(params string[] lines) => string.Join("\n", lines);

	private Plan BackupAndMove()
	{
		shelf.Add(Seven, "7");
		shelf.Add(Eight, "8");
		Profile main = shelf.Profile(edit: settings => settings with { PruneEmptyFolders = true });
		Profile backup = shelf.Profile("Backup", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: true) });
		return shelf.Plan(main, backup);
	}

	[Test]
	public async Task Undo_of_a_full_run_restores_every_path_and_removes_the_copies_it_made()
	{
		RunReport report = shelf.Run(BackupAndMove());

		Plan undo = shelf.PlanUndo(report.Run);

		await Assert.That(undo.Kind).IsEqualTo(new PlanKind.Undo(report.Run));
		await Assert.That(undo.Conflicts.Count).IsEqualTo(0);
		await Assert.That(undo.Entries.All(entry => entry.Outcome is EntryOutcome.Planned)).IsTrue();
		await Assert.That(string.Join("|", undo.Entries.Select(entry => $"{entry.BookCaption} / {entry.ProfileName}").OrderBy(text => text, StringComparer.Ordinal)))
			.IsEqualTo("X-Men #7 / Backup|X-Men #7 / Main|X-Men #8 / Backup|X-Men #8 / Main");

		RunReport undone = shelf.Run(undo);

		await Assert.That(undone.Results.Values.All(result => result is OpResult.Done)).IsTrue();
		await Assert.That(shelf.Files()).IsEqualTo(Lines(Eight, Seven));
		await Assert.That(shelf.Read(Seven)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.Read(Eight)).IsEqualTo(RealShelf.ContentOf(Eight));
		await Assert.That(shelf.LibraryFiles()).IsEqualTo(Lines(Eight, Seven));
		await Assert.That(shelf.Folders().Split('\n').Contains(@"Comics\Marvel")).IsFalse();
		await Assert.That(shelf.Journals.List().First().Kind).IsEqualTo(new PlanKind.Undo(report.Run));
	}

	[Test]
	public async Task Undo_of_a_run_that_was_killed_undoes_exactly_what_it_did()
	{
		RunId run = shelf.RunUntilKilled(BackupAndMove(), afterChanges: 2);

		Plan undo = shelf.PlanUndo(run);

		await Assert.That(undo.Operations.Script()).IsEqualTo(Lines(
			$"move {shelf.P(SevenOrganized)} -> {shelf.P(Seven)}",
			$"recycle {shelf.P(SevenBackup)}",
			$@"prune {shelf.P(@"Comics\Marvel\X-Men")}"));
		await Assert.That(undo.Entries.Count(entry => entry.Outcome is EntryOutcome.Planned)).IsEqualTo(2);
		await Assert.That(undo.Entries.Count(entry => entry.Outcome is EntryOutcome.Skipped)).IsEqualTo(2);

		shelf.Run(undo);

		await Assert.That(shelf.Files()).IsEqualTo(Lines(Eight, Seven));
		await Assert.That(shelf.Read(Seven)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.LibraryFiles()).IsEqualTo(Lines(Eight, Seven));
	}

	[Test]
	public async Task Undo_goes_by_the_disk_when_the_journal_marks_say_more_than_happened()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Add(Eight, "8");
		RunReport report = shelf.Run(shelf.Plan(shelf.Profile()));
		System.IO.File.Move(shelf.P(SevenOrganized).Value, shelf.P(@"Elsewhere.cbz").Value);

		Plan undo = shelf.PlanUndo(report.Run);

		await Assert.That(undo.Operations.Script()).IsEqualTo($@"move {shelf.P(@"Comics\Marvel\X-Men\X-Men #8.cbz")} -> {shelf.P(Eight)}");
		await Assert.That(undo.Entries.Single(entry => entry.Key.Book == seven.Id).Outcome).IsTypeOf<EntryOutcome.Skipped>();
	}

	[Test]
	public async Task Undo_into_a_path_that_is_taken_again_is_a_conflict_that_allows_skip_and_keep_both()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		RunReport report = shelf.Run(shelf.Plan(shelf.Profile()));
		shelf.Put(Seven, "a newcomer");

		Plan undo = shelf.PlanUndo(report.Run);

		Conflict conflict = undo.Conflicts.Single();
		await Assert.That(conflict.Key).IsEqualTo(new ConflictKey(undo.Entries.Single().Key, shelf.P(Seven)));
		await Assert.That(conflict.Kind).IsTypeOf<ConflictKind.TargetExists>();
		await Assert.That(string.Join(",", conflict.Allowed)).IsEqualTo("Skip,KeepBoth");
		await Assert.That(conflict.KeepBothPath).IsEqualTo(shelf.P(@"Incoming\seven (1).cbz"));
		await Assert.That(conflict.IsOpen).IsTrue();
		await Assert.That(undo.Entries.Single().Outcome).IsTypeOf<EntryOutcome.Blocked>();
		await Assert.That(undo.Approve()).IsNull();
		await Assert.That(undo.Operations.Count).IsEqualTo(0);

		Plan replace = shelf.PlanUndo(report.Run, Decisions.None.WithDefault(new Resolution.Replace()));
		await Assert.That(replace.Conflicts.Single().IsOpen).IsTrue();

		Plan skip = shelf.PlanUndo(report.Run, Decisions.None.WithResolution(conflict.Key, new Resolution.Skip()));
		await Assert.That(skip.Entries.Single().Outcome).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(skip.Operations.Count).IsEqualTo(0);
		await Assert.That(skip.Approve()).IsNotNull();

		shelf.Run(shelf.PlanUndo(report.Run, Decisions.None.WithDefault(new Resolution.KeepBoth())));

		await Assert.That(shelf.Files()).IsEqualTo(Lines(@"Incoming\seven (1).cbz", Seven));
		await Assert.That(shelf.Read(Seven)).IsEqualTo("a newcomer");
		await Assert.That(shelf.Read(@"Incoming\seven (1).cbz")).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(@"Incoming\seven (1).cbz");
	}

	[Test]
	public async Task Undo_of_a_replace_moves_the_book_back_and_reports_that_the_recycled_file_cannot_be_undone()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Put(SevenOrganized, "the old copy");
		RunReport report = shelf.Run(shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), shelf.Profile()));

		Plan undo = shelf.PlanUndo(report.Run);

		await Assert.That(undo.Operations.Script()).IsEqualTo($"move {shelf.P(SevenOrganized)} -> {shelf.P(Seven)}");
		var outcome = (EntryOutcome.PlannedAsFailed)undo.Entries.Single().Outcome;
		await Assert.That(outcome.Reason).Contains("cannot be undone");
		await Assert.That(undo.Tally().Single().Failed).IsEqualTo(1);

		shelf.Run(undo);

		await Assert.That(shelf.Files()).IsEqualTo(Seven);
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(Seven);
	}

	[Test]
	public async Task An_undo_can_itself_be_undone()
	{
		shelf.Add(Seven, "7");
		RunReport report = shelf.Run(shelf.Plan(shelf.Profile()));
		RunReport undone = shelf.Run(shelf.PlanUndo(report.Run));

		shelf.Run(shelf.PlanUndo(undone.Run));

		await Assert.That(shelf.Files()).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.LibraryFiles()).IsEqualTo(SevenOrganized);
	}
}
