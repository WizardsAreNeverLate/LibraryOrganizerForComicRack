using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using LibraryOrganizer.Plugin.Forms.Execution;
using LibraryOrganizer.Plugin.Forms.Run;

namespace LibraryOrganizer.Plugin;

internal sealed partial class Session
{
	private readonly WindowsFileSystem disk = new();

	/// <summary>
	/// Plans the books under the chosen profiles, shows the plan for review, and runs it when the user approves.
	/// A quick run skips the review when nothing needs a decision and nothing failed.
	/// </summary>
	public void Organize(ComicBook[] books, bool quick) => Guarded(() =>
	{
		if (books.Length == 0)
		{
			return;
		}
		OfferToFinishInterruptedRuns();
		Settings settings = LoadSettings();
		if (Compile(settings) is not Dictionary<ProfileId, Profile> compiled)
		{
			return;
		}
		IReadOnlyList<ProfileId>? order = settings.Profiles.Count == 1
			? [settings.Profiles[0].Id]
			: ProfilePicker.Pick(Owner, settings.Profiles, settings.LastRunOrder);
		if (order is null || order.Count == 0)
		{
			return;
		}
		if (!order.SequenceEqual(settings.LastRunOrder))
		{
			Settings.Save(settings with { LastRunOrder = order });
		}

		LibrarySnapshot snapshot = LibrarySnapshot.Take(books, env.App.GetLibraryBooks());
		if (snapshot.Unreadable.Count > 0)
		{
			ShowWarnings("Books left out", [.. snapshot.Unreadable.Select(caption => $"{caption}: the file path is not a full path.")]);
		}
		Profile[] runOrder = [.. order.Select(id => compiled[id])];
		Plan Replan(Decisions decisions) => Organizer.Plan(new OrganizeInput(runOrder, snapshot.Selection, snapshot.Series, disk, snapshot.LibraryFiles), decisions);

		var decisions = new Decisions(Answers, Decisions.None.Resolutions, settings.Preferences.DefaultResolution.ToResolution());
		Plan plan = WithWaitCursor(() => Replan(decisions));
		bool needsReview = !quick
			|| plan.Approve() is null
			|| plan.Entries.Any(e => e.Outcome is EntryOutcome.Failed or EntryOutcome.PlannedAsFailed);
		ApprovedPlan? approved = needsReview ? Review(plan, Replan, decisions) : plan.Approve();
		if (approved is not null)
		{
			Execute("Library Organizer", approved, books);
		}
	});

	public void History() => Guarded(() =>
	{
		while (HistoryForm.Pick(Owner, Journals.List()) is (RunId run, HistoryAction action))
		{
			if (action == HistoryAction.Finish)
			{
				Resume(run);
				continue;
			}
			RunJournal journal = Journals.Read(run);
			IReadOnlyDictionary<AbsPath, BookId> libraryFiles = LibrarySnapshot.Take([], env.App.GetLibraryBooks()).LibraryFiles;
			Decisions decisions = Decisions.None with { Answers = Answers };
			Plan Replan(Decisions decisions) => Organizer.PlanUndo(journal, disk, libraryFiles, decisions);
			if (Review(WithWaitCursor(() => Replan(decisions)), Replan, decisions) is ApprovedPlan undo)
			{
				Execute("Library Organizer: undo", undo);
			}
		}
	});

	private ApprovedPlan? Review(Plan plan, Func<Decisions, Plan> replan, Decisions decisions)
	{
		using var review = new ReviewForm(plan, replan, decisions);
		DialogResult result = review.ShowDialog(Owner);
		Answers = review.Decisions.Answers;
		return result == DialogResult.OK ? review.Approved : null;
	}

	private void Execute(string title, ApprovedPlan plan, IEnumerable<ComicBook>? books = null)
	{
		Executor executor = NewExecutor(books);
		ShowReport(title, RunWindow.Show(Owner, title, (progress, cancel) => executor.Run(plan, progress, cancel)));
	}

	private void Resume(RunId run)
	{
		Executor executor = NewExecutor();
		ShowReport("Library Organizer: finished run", RunWindow.Show(Owner, "Finishing the run", (progress, cancel) => executor.Resume(run, progress, cancel)));
	}

	/// <summary>
	/// Call on the UI thread, never inside a RunWindow body: the gateway captures this thread to send library writes back to.
	/// <paramref name="books"/> lets books outside the library, such as those from the folder view, follow their files.
	/// </summary>
	private Executor NewExecutor(IEnumerable<ComicBook>? books = null) => new(disk, new ComicRackGateway(env.App, books), Journals);

	private void ShowReport(string title, RunReport report) => ReportForm.Show(Owner, title, report);

	private void OfferToFinishInterruptedRuns()
	{
		foreach (RunSummary run in Journals.Interrupted())
		{
			string question = $"A Library Organizer run from {run.Started.ToLocalTime():g} did not finish ({run.Done} of {run.Operations} changes done).\n\nFinish it now? Choose No to leave it; you can finish or undo it later from Library Organizer: History.";
			if (MessageBox.Show(Owner, question, "Library Organizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Resume(run.Id);
			}
		}
	}

	private Dictionary<ProfileId, Profile>? Compile(Settings settings)
	{
		var compiled = new Dictionary<ProfileId, Profile>();
		var problems = new List<string>();
		foreach (ProfileSettings profile in settings.Profiles)
		{
			CompileResult result = profile.Compile();
			if (result.Profile is Profile ok)
			{
				compiled[profile.Id] = ok;
			}
			else
			{
				problems.AddRange(result.Errors.Select(error => $"{profile.Name}: {error.Message}"));
			}
		}
		if (problems.Count == 0)
		{
			return compiled;
		}
		string message = "These profiles need fixing before a run:\n\n" + string.Join("\n", problems) + "\n\nOpen the settings now?";
		if (MessageBox.Show(Owner, message, "Library Organizer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
		{
			ConfigureCore(null);
		}
		return null;
	}

	private static T WithWaitCursor<T>(Func<T> work)
	{
		Cursor previous = Cursor.Current;
		Cursor.Current = Cursors.WaitCursor;
		try
		{
			return work();
		}
		finally
		{
			Cursor.Current = previous;
		}
	}
}
