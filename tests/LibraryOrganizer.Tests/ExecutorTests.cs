using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public sealed class ExecutorTests : IDisposable
{
	private const string Seven = @"Incoming\seven.cbz";
	private const string Eight = @"Incoming\eight.cbz";
	private const string SevenOrganized = @"Comics\Marvel\X-Men\X-Men #7.cbz";
	private const string EightOrganized = @"Comics\Marvel\X-Men\X-Men #8.cbz";
	private const string SevenBackup = @"Backup\Marvel\X-Men\X-Men #7.cbz";
	private const string EightBackup = @"Backup\Marvel\X-Men\X-Men #8.cbz";

	private readonly RealShelf shelf = new();

	public void Dispose() => shelf.Dispose();

	private static string Lines(params string[] lines) => string.Join("\n", lines);

	/// <summary>Two incoming books under a Move profile that prunes and a Copy profile that adds its copies to the library.</summary>
	private (BookRecord Seven, BookRecord Eight, Plan Plan) BackupAndMove()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		BookRecord eight = shelf.Add(Eight, "8");
		Profile main = shelf.Profile(edit: settings => settings with { PruneEmptyFolders = true });
		Profile backup = shelf.Profile("Backup", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: true) });
		return (seven, eight, shelf.Plan(main, backup));
	}

	[Test]
	public async Task A_run_moves_the_files_and_tells_the_library_where_they_went()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		BookRecord eight = shelf.Add(Eight, "8");

		RunReport report = shelf.Run(shelf.Plan(shelf.Profile()));

		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenOrganized, EightOrganized));
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.Read(EightOrganized)).IsEqualTo(RealShelf.ContentOf(Eight));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.PathOf(eight)).IsEqualTo(EightOrganized);
		await Assert.That(report.Cancelled).IsFalse();
		await Assert.That(report.Results.Values.All(result => result is OpResult.Done)).IsTrue();
		await Assert.That(report.Tally().Single()).IsEqualTo(new ProfileTally("Main", Planned: 2, InPlace: 0, Skipped: 0, Failed: 0, Waiting: 0, Blocked: 0));
	}

	[Test]
	public async Task A_copy_profile_leaves_the_source_and_adds_the_copy_to_the_library()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		Profile backup = shelf.Profile("Backup", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: true) });

		RunReport report = shelf.Run(shelf.Plan(backup));

		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenBackup, Seven));
		await Assert.That(shelf.Read(SevenBackup)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.LibraryFiles()).IsEqualTo(Lines(SevenBackup, Seven));
		BookId created = shelf.Journals.Read(report.Run).Marks.Values.Single().Created!.Value;
		await Assert.That(created).IsNotEqualTo(seven.Id);
		await Assert.That(shelf.Library.Paths[created]).IsEqualTo(shelf.P(SevenBackup));
	}

	[Test]
	public async Task A_copy_that_is_not_added_to_the_library_makes_no_library_call()
	{
		shelf.Add(Seven, "7");
		Profile mirror = shelf.Profile("Mirror", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: false) });

		shelf.Run(shelf.Plan(mirror));

		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenBackup, Seven));
		await Assert.That(shelf.Library.Calls.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_fileless_book_gets_its_cover_written_and_undo_removes_the_image()
	{
		BookRecord template = TestBooks.Book(number: "7");
		var fileless = new BookRecord(
			template.Id, new BookLocation.Fileless(HasCustomCover: true), template.Caption,
			Fields.All.Select(field => template[field]).ToArray(), new System.Collections.Generic.Dictionary<string, string>());
		shelf.Selection.Add(fileless);
		shelf.Library.Covers[fileless.Id] = [1, 2, 3];
		Profile covers = shelf.Profile(edit: settings => settings with { FilelessCovers = CoverFormat.Png });

		RunReport report = shelf.Run(shelf.Plan(covers));

		const string image = @"Comics\Marvel\X-Men\X-Men #7.png";
		await Assert.That(shelf.Files()).IsEqualTo(image);
		await Assert.That(File.ReadAllBytes(shelf.P(image).Value).SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
		await Assert.That(shelf.Library.Paths.Count).IsEqualTo(0);

		shelf.Run(shelf.PlanUndo(report.Run));

		await Assert.That(shelf.Files()).IsEqualTo("");
	}

	[Test]
	public async Task A_file_that_appears_at_the_target_after_planning_is_never_overwritten()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		BookRecord eight = shelf.Add(Eight, "8");
		Profile main = shelf.Profile();
		Plan plan = shelf.Plan(main);
		shelf.Put(SevenOrganized, "someone else's file");

		RunReport report = shelf.Run(plan);

		await Assert.That(report.Results[plan.OperationsOf(seven, main).Single().Id]).IsTypeOf<OpResult.Failed>();
		await Assert.That(report.Results[plan.OperationsOf(eight, main).Single().Id]).IsTypeOf<OpResult.Done>();
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo("someone else's file");
		await Assert.That(shelf.Read(Seven)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenOrganized, EightOrganized, Seven));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(Seven);
		await Assert.That(shelf.PathOf(eight)).IsEqualTo(EightOrganized);
		await Assert.That(report.Tally().Single()).IsEqualTo(new ProfileTally("Main", Planned: 1, InPlace: 0, Skipped: 0, Failed: 1, Waiting: 0, Blocked: 0));
	}

	[Test]
	public async Task An_operation_the_system_refuses_fails_with_its_message_and_the_run_goes_on()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		Profile main = shelf.Profile();
		Profile backup = shelf.Profile("Backup", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: false) });
		Plan plan = shelf.Plan(main, backup);
		const string message = "Access to the path is denied.\nIt is \"locked\" <by> another & process.";
		var locked = new HookedFileSystem(shelf.Disk)
		{
			Before = change =>
			{
				if (change == nameof(IFileSystem.Move))
				{
					throw new UnauthorizedAccessException(message);
				}
			},
		};

		RunReport report = shelf.Executor(locked).Run(plan.Approve()!, null, default);

		OpId move = plan.OperationsOf(seven, main).Single().Id;
		await Assert.That(report.Results[move]).IsEqualTo(new OpResult.Failed(move, message));
		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenBackup, Seven));
		RunJournal journal = shelf.Journals.Read(report.Run);
		await Assert.That(journal.Mark(move)).IsEqualTo(new OpMark(File: false, Applied: false, Created: null, Failed: message));
		await Assert.That(journal.Summary).IsEqualTo(journal.Summary with { Operations = 2, Done = 1, Failed = 1, Finished = true, Cancelled = false });
		await Assert.That(shelf.JournalText(report.Run).Split('\n').Count(line => line.StartsWith("<failed ", StringComparison.Ordinal))).IsEqualTo(1);
	}

	[Test]
	public async Task A_cancel_stops_between_operations_and_leaves_the_rest_not_run()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		BookRecord eight = shelf.Add(Eight, "8");
		BookRecord nine = shelf.Add(@"Incoming\nine.cbz", "9");
		Profile main = shelf.Profile();
		Plan plan = shelf.Plan(main);
		using var cancel = new CancellationTokenSource();
		shelf.Library.Before = _ => cancel.Cancel();

		RunReport report = shelf.Executor().Run(plan.Approve()!, null, cancel.Token);

		await Assert.That(report.Cancelled).IsTrue();
		await Assert.That(report.Results[plan.OperationsOf(seven, main).Single().Id]).IsTypeOf<OpResult.Done>();
		await Assert.That(report.Results[plan.OperationsOf(eight, main).Single().Id]).IsTypeOf<OpResult.NotRun>();
		await Assert.That(report.Results[plan.OperationsOf(nine, main).Single().Id]).IsTypeOf<OpResult.NotRun>();
		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenOrganized, Eight, @"Incoming\nine.cbz"));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.PathOf(eight)).IsEqualTo(Eight);
		await Assert.That(report.Tally().Single()).IsEqualTo(new ProfileTally("Main", Planned: 1, InPlace: 0, Skipped: 2, Failed: 0, Waiting: 0, Blocked: 0));
		RunSummary summary = shelf.Journals.List().Single();
		await Assert.That(summary.Finished).IsTrue();
		await Assert.That(summary.Cancelled).IsTrue();
		await Assert.That(shelf.Journals.Interrupted().Count).IsEqualTo(0);
	}

	[Test]
	public async Task Progress_counts_the_operations_and_names_the_file()
	{
		shelf.Add(Seven, "7");
		shelf.Add(Eight, "8");
		var seen = new System.Collections.Generic.List<RunProgress>();

		shelf.Executor().Run(shelf.Plan(shelf.Profile()).Approve()!, new Collector(seen), default);

		await Assert.That(string.Join("|", seen.Select(progress => $"{progress.Done}/{progress.Total} {progress.Current}")))
			.IsEqualTo($"0/2 {shelf.P(Seven)}|1/2 {shelf.P(Eight)}|2/2 ");
	}

	private sealed class Collector(System.Collections.Generic.List<RunProgress> seen) : IProgress<RunProgress>
	{
		public void Report(RunProgress value) => seen.Add(value);
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task A_run_killed_after_any_file_change_is_finished_by_resume_and_a_second_resume_changes_nothing(int changesBeforeKill)
	{
		(BookRecord seven, BookRecord eight, Plan plan) = BackupAndMove();

		RunId run = shelf.RunUntilKilled(plan, changesBeforeKill);

		await Assert.That(shelf.Journals.Interrupted().Single().Id).IsEqualTo(run);

		RunReport report = shelf.Executor().Resume(run, null, default);

		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenBackup, EightBackup, SevenOrganized, EightOrganized));
		await Assert.That(shelf.Folders().Split('\n').Contains("Incoming")).IsFalse();
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.Read(EightBackup)).IsEqualTo(RealShelf.ContentOf(Eight));
		await Assert.That(shelf.LibraryFiles()).IsEqualTo(Lines(SevenBackup, EightBackup, SevenOrganized, EightOrganized));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.PathOf(eight)).IsEqualTo(EightOrganized);
		await Assert.That(report.Results.Count).IsEqualTo(5);
		await Assert.That(report.Results.Values.Any(result => result is OpResult.Failed or OpResult.NotRun)).IsFalse();
		await Assert.That(report.Results.Values.Count(result => result is OpResult.AlreadyDone)).IsEqualTo(changesBeforeKill);
		await Assert.That(shelf.Journals.Interrupted().Count).IsEqualTo(0);

		int calls = shelf.Library.Calls.Count;
		string journal = shelf.JournalText(run);

		RunReport again = shelf.Executor().Resume(run, null, default);

		await Assert.That(shelf.Library.Calls.Count).IsEqualTo(calls);
		await Assert.That(shelf.JournalText(run)).IsEqualTo(journal);
		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenBackup, EightBackup, SevenOrganized, EightOrganized));
		await Assert.That(again.Results.Values.All(result => result is OpResult.AlreadyDone)).IsTrue();
	}

	[Test]
	public async Task A_run_that_dies_between_the_file_change_and_the_library_write_gets_only_the_library_write_on_resume()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		Profile main = shelf.Profile();
		Plan plan = shelf.Plan(main);
		ApprovedPlan approved = plan.Approve()!;
		shelf.Library.Before = _ => throw new InvalidOperationException("The process was killed.");

		await Assert.That(() => shelf.Executor().Run(approved, null, default)).Throws<InvalidOperationException>();

		OpId move = plan.OperationsOf(seven, main).Single().Id;
		await Assert.That(shelf.Journals.Read(approved.Id).Mark(move)).IsEqualTo(new OpMark(File: true, Applied: false, Created: null, Failed: null));
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(Seven);

		shelf.Library.Before = null;
		RunReport report = shelf.Executor().Resume(approved.Id, null, default);

		await Assert.That(report.Results[move]).IsTypeOf<OpResult.AlreadyDone>();
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(shelf.Journals.Read(approved.Id).Mark(move)).IsEqualTo(new OpMark(File: true, Applied: true, Created: null, Failed: null));
	}

	[Test]
	public async Task Resume_after_a_move_into_a_vacated_path_repeats_neither_move()
	{
		const string held = EightOrganized;
		const string nineOrganized = @"Comics\Marvel\X-Men\X-Men #9.cbz";
		shelf.Add(Eight, "8");
		shelf.Add(held, "9");
		Plan plan = shelf.Plan(shelf.Profile());

		RunId run = shelf.RunUntilKilled(plan, afterChanges: 2);
		RunReport report = shelf.Executor().Resume(run, null, default);

		await Assert.That(report.Results.Values.All(result => result is OpResult.AlreadyDone)).IsTrue();
		await Assert.That(shelf.Files()).IsEqualTo(Lines(EightOrganized, nineOrganized));
		await Assert.That(shelf.Read(EightOrganized)).IsEqualTo(RealShelf.ContentOf(Eight));
		await Assert.That(shelf.Read(nineOrganized)).IsEqualTo(RealShelf.ContentOf(held));

		shelf.Run(shelf.PlanUndo(run));

		await Assert.That(shelf.Files()).IsEqualTo(Lines(held, Eight));
		await Assert.That(shelf.Read(held)).IsEqualTo(RealShelf.ContentOf(held));
		await Assert.That(shelf.Read(Eight)).IsEqualTo(RealShelf.ContentOf(Eight));
	}

	[Test]
	public async Task Resume_after_a_replace_does_not_recycle_the_file_that_took_the_place()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		var replaced = new BookId(Guid.NewGuid());
		shelf.Put(SevenOrganized, "the old copy");
		shelf.Library.Paths[replaced] = shelf.P(SevenOrganized);
		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), shelf.Profile());

		RunId run = shelf.RunUntilKilled(plan, afterChanges: 2);
		RunReport report = shelf.Executor().Resume(run, null, default);

		await Assert.That(shelf.Files()).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(Seven));
		await Assert.That(report.Results.Values.All(result => result is OpResult.AlreadyDone)).IsTrue();
		await Assert.That(shelf.LibraryFiles()).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.Library.Transfers.Single()).IsEqualTo((replaced, seven.Id));
	}

	[Test]
	public async Task A_case_only_rename_changes_the_spelling_on_the_disk_and_nothing_else()
	{
		const string lowerCase = @"Comics\Marvel\X-Men\x-men #7.cbz";
		BookRecord seven = shelf.Add(lowerCase, "7");
		shelf.Put(@"Comics\Marvel\X-Men\other.cbz", "a neighbour");
		Plan plan = shelf.Plan(shelf.Profile());

		await Assert.That(plan.Operations.Single()).IsTypeOf<Operation.RenameCase>();

		RunReport report = shelf.Run(plan);

		await Assert.That(shelf.Files()).IsEqualTo(Lines(SevenOrganized, @"Comics\Marvel\X-Men\other.cbz"));
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(lowerCase));
		await Assert.That(shelf.Library.Paths[seven.Id].Value).IsEqualTo(shelf.P(SevenOrganized).Value);
		await Assert.That(report.Results.Values.Single()).IsTypeOf<OpResult.Done>();

		shelf.Run(shelf.PlanUndo(report.Run));

		await Assert.That(shelf.Files()).IsEqualTo(Lines(@"Comics\Marvel\X-Men\other.cbz", lowerCase));
		await Assert.That(shelf.Library.Paths[seven.Id].Value).IsEqualTo(shelf.P(lowerCase).Value);
	}

	[Test]
	public async Task A_case_only_rename_killed_between_its_two_steps_is_finished_by_resume()
	{
		const string lowerCase = @"Comics\Marvel\X-Men\x-men #7.cbz";
		shelf.Add(lowerCase, "7");

		RunId run = shelf.RunUntilKilled(shelf.Plan(shelf.Profile()), afterChanges: 1);
		RunReport report = shelf.Executor().Resume(run, null, default);

		await Assert.That(shelf.Files()).IsEqualTo(SevenOrganized);
		await Assert.That(shelf.Read(SevenOrganized)).IsEqualTo(RealShelf.ContentOf(lowerCase));
		await Assert.That(report.Results.Values.Single()).IsTypeOf<OpResult.Done>();
	}

	[Test]
	public async Task Prune_removes_empty_folders_upwards_and_stops_at_a_stop_folder_a_kept_folder_and_a_folder_with_content()
	{
		foreach (string folder in new[] { @"stop\a\b", @"keep\k1\k2", @"free\f1\f2", @"gone" })
		{
			Directory.CreateDirectory(shelf.P(folder).Value);
		}
		shelf.Put(@"full\x\y\book.cbz", "a book");
		Directory.CreateDirectory(shelf.P(@"full\x\y\empty").Value);
		var prune = new Operation.PruneFolders(
			new OpId(1),
			[shelf.P(@"stop\a\b"), shelf.P(@"keep\k1\k2"), shelf.P(@"free\f1\f2"), shelf.P(@"full\x\y\empty"), shelf.P(@"gone\was\here")],
			StopAt: [shelf.P("stop"), shelf.P(".")],
			Keep: [shelf.P(@"keep\k1")]);
		var plan = new ApprovedPlan(new RunId(Guid.NewGuid()), new PlanKind.Organize(), [prune], []);

		RunReport report = shelf.Executor().Run(plan, null, default);

		await Assert.That(shelf.Folders()).IsEqualTo(Lines("full", @"full\x", @"full\x\y", "keep", @"keep\k1", "stop"));
		await Assert.That(shelf.Files()).IsEqualTo(@"full\x\y\book.cbz");
		await Assert.That(Directory.Exists(shelf.Lib)).IsTrue();
		await Assert.That(report.Results[new OpId(1)]).IsTypeOf<OpResult.Done>();
	}
}
