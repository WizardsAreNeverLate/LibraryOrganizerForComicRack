using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LibraryOrganizer.Tests;

public sealed class JournalTests : IDisposable
{
	private const string Seven = @"Incoming\seven.cbz";
	private const string Eight = @"Incoming\eight.cbz";
	private const string SevenOrganized = @"Comics\Marvel\X-Men\X-Men #7.cbz";
	private const string EightOrganized = @"Comics\Marvel\X-Men\X-Men #8.cbz";

	private readonly RealShelf shelf = new(keepRuns: 2);

	public void Dispose() => shelf.Dispose();

	[Test]
	public async Task The_journal_of_a_run_has_one_element_per_line_in_the_shipped_format()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		Profile main = shelf.Profile();
		ApprovedPlan plan = shelf.Plan(main).Approve()!;
		DateTime started = shelf.NextStart;

		shelf.Executor().Run(plan, null, default);

		await Assert.That(shelf.JournalText(plan.Id)).IsEqualTo(string.Join("\n",
			$"<run schema=\"1\" id=\"{plan.Id.Value}\" started=\"2026-01-01T00:00:01.0000000Z\" kind=\"organize\" entries=\"1\" ops=\"1\" />",
			$"<entry book=\"{seven.Id.Value}\" profile=\"{main.Id.Value}\" caption=\"X-Men #7\" profileName=\"Main\" outcome=\"planned\" ops=\"1\" />",
			$"<op id=\"1\" kind=\"move\" book=\"{seven.Id.Value}\" profile=\"{main.Id.Value}\" from=\"{shelf.P(Seven)}\" to=\"{shelf.P(SevenOrganized)}\" />",
			"<file op=\"1\" />",
			"<applied op=\"1\" />",
			"<end cancelled=\"false\" />",
			""));
		await Assert.That(Path.GetFileName(shelf.JournalPath(plan.Id))).IsEqualTo($"20260101-000001000-{plan.Id.Value:N}.journal");
		RunSummary summary = shelf.Journals.List().Single();
		await Assert.That(summary)
			.IsEqualTo(summary with { Id = plan.Id, Started = started, Operations = 1, Done = 1, Failed = 0, Finished = true, Cancelled = false });
		await Assert.That(summary.Kind).IsEqualTo(new PlanKind.Organize());
		await Assert.That(string.Join("|", summary.Profiles)).IsEqualTo("Main");
	}

	[Test]
	public async Task The_whole_plan_is_in_the_journal_before_the_first_file_changes()
	{
		shelf.Add(Seven, "7");
		shelf.Add(Eight, "8");
		ApprovedPlan plan = shelf.Plan(shelf.Profile(edit: settings => settings with { PruneEmptyFolders = true })).Approve()!;
		string? atFirstChange = null;
		var watching = new HookedFileSystem(shelf.Disk) { Before = _ => atFirstChange ??= shelf.JournalText(plan.Id) };

		shelf.Executor(watching).Run(plan, null, default);

		string[] lines = atFirstChange!.Split('\n');
		await Assert.That(string.Join(" ", lines.Select(line => line.Length == 0 ? "" : XElement.Parse(line).Name.LocalName))).IsEqualTo("run entry entry op op op ");
		await Assert.That(string.Join(" ", lines.Take(6).Skip(3).Select(line => XElement.Parse(line).Attribute("kind")!.Value))).IsEqualTo("move move prune");
	}

	[Test]
	public async Task A_journal_reads_back_as_the_plan_that_was_approved()
	{
		shelf.Add(Seven, "7");
		shelf.Put(SevenOrganized, "the old copy");
		var replaced = new BookId(Guid.NewGuid());
		shelf.Library.Paths[replaced] = shelf.P(SevenOrganized);
		shelf.Add(Eight, "8");
		Profile main = shelf.Profile(edit: settings => settings with { PruneEmptyFolders = true, KeepFolders = [shelf.P("Keep").Value] });
		Profile backup = shelf.Profile("Backup", "Backup", settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: true) });
		Profile dc = shelf.Profile("DC", "DC", settings => settings with
		{
			RuleMode = RuleMode.Only,
			Rules = new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition("publisher", Operator.Is, "DC")]),
		});
		ApprovedPlan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), dc, main, backup).Approve()!;

		shelf.Executor().Run(plan, null, default);
		RunJournal journal = shelf.Journals.Read(plan.Id);

		await Assert.That(journal.Operations.Script()).IsEqualTo(plan.Operations.Script());
		await Assert.That(journal.Operations.Select(operation => operation.Id).SequenceEqual(plan.Operations.Select(operation => operation.Id))).IsTrue();
		await Assert.That(journal.Operations.OfType<Operation.RecycleFile>().Single()).IsEqualTo(plan.Operations.OfType<Operation.RecycleFile>().Single());
		Operation.PruneFolders prune = journal.Operations.OfType<Operation.PruneFolders>().Single();
		Operation.PruneFolders planned = plan.Operations.OfType<Operation.PruneFolders>().Single();
		await Assert.That(prune.StopAt.SequenceEqual(planned.StopAt)).IsTrue();
		await Assert.That(string.Join("|", prune.Keep)).IsEqualTo(shelf.P("Keep").Value);
		await Assert.That(string.Join("\n", journal.Entries.Select(Describe))).IsEqualTo(string.Join("\n", plan.Entries.Select(Describe)));
		await Assert.That(journal.Entries.Count(entry => entry.Outcome is EntryOutcome.Skipped)).IsEqualTo(2);
	}

	private static string Describe(PlanEntry entry)
		=> $"{entry.Key.Book.Value} {entry.Key.Profile?.Value} {entry.BookCaption} {entry.ProfileName} " + entry.Outcome switch
		{
			EntryOutcome.Planned planned => "planned " + string.Join(",", planned.Operations.Select(operation => operation.Id.Value)),
			EntryOutcome.Skipped skipped => "skipped " + skipped.Reason,
			EntryOutcome other => other.ToString(),
		};

	[Test]
	public async Task A_last_line_cut_short_by_a_crash_is_ignored_and_resume_writes_after_it()
	{
		shelf.Add(Seven, "7");
		shelf.Add(Eight, "8");
		RunId run = shelf.RunUntilKilled(shelf.Plan(shelf.Profile()), afterChanges: 1);
		File.AppendAllText(shelf.JournalPath(run), "<file op=\"2");

		RunJournal torn = shelf.Journals.Read(run);

		await Assert.That(torn.Operations.Count).IsEqualTo(2);
		await Assert.That(torn.Marks.Count).IsEqualTo(0);
		await Assert.That(torn.Summary.Finished).IsFalse();

		shelf.Executor().Resume(run, null, default);

		string[] lines = shelf.JournalText(run).Split('\n');
		await Assert.That(string.Join(" ", lines.Select(line => line.Length == 0 ? "" : XElement.Parse(line).Name.LocalName)))
			.IsEqualTo("run entry entry op op file applied file applied end ");
		await Assert.That(shelf.Files()).IsEqualTo(SevenOrganized + "\n" + EightOrganized);
		await Assert.That(shelf.Journals.Read(run).Summary.Done).IsEqualTo(2);
	}

	[Test]
	public async Task Trim_keeps_the_newest_finished_runs_and_every_interrupted_one()
	{
		Profile main = shelf.Profile();
		shelf.Add(@"Incoming\one.cbz", "1");
		RunId interrupted = shelf.RunUntilKilled(shelf.Plan(main), afterChanges: 1);
		var finished = new System.Collections.Generic.List<RunId>();
		foreach (string number in new[] { "2", "3", "4" })
		{
			shelf.Selection.Clear();
			shelf.Add($@"Incoming\{number}.cbz", number);
			finished.Add(shelf.Run(shelf.Plan(main)).Run);
		}

		await Assert.That(shelf.Journals.List().Select(summary => summary.Id).SequenceEqual([finished[2], finished[1], interrupted])).IsTrue();
		await Assert.That(shelf.Journals.Interrupted().Single().Id).IsEqualTo(interrupted);
		await Assert.That(() => shelf.Journals.Read(finished[0])).Throws<FileNotFoundException>();
	}

	[Test]
	public async Task A_journal_whose_plan_was_not_written_in_full_is_not_a_run_and_trim_deletes_it()
	{
		shelf.Add(Seven, "7");
		RunReport report = shelf.Run(shelf.Plan(shelf.Profile()));
		string[] lines = shelf.JournalText(report.Run).Split('\n');
		var unfinished = new RunId(Guid.NewGuid());
		string path = Path.Combine(shelf.RunsFolder, $"20270101-000000000-{unfinished.Value:N}.journal");
		File.WriteAllText(path, lines[0].Replace(report.Run.Value.ToString(), unfinished.Value.ToString()) + "\n" + lines[1] + "\n");

		await Assert.That(shelf.Journals.List().Single().Id).IsEqualTo(report.Run);
		await Assert.That(shelf.Journals.Interrupted().Count).IsEqualTo(0);

		shelf.Journals.Trim();

		await Assert.That(File.Exists(path)).IsFalse();
		await Assert.That(File.Exists(shelf.JournalPath(report.Run))).IsTrue();
	}
}
