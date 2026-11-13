using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibraryOrganizer.Plugin.Forms.Run;

namespace LibraryOrganizer.FormPreview;

internal static class RunScreenshots
{
	public static void Render(string stem, Action<Form, string> capture)
	{
		Profiles(stem, capture);
		RunSamples samples = RunSamples.Make();
		Plan first = samples.Plan(Decisions.None);
		Questions(stem, capture, first);
		Review(stem, capture, samples, first);
		Large(stem, capture);
	}

	public static void Interactive()
	{
		RunSamples samples = RunSamples.Make();
		using var form = new ReviewForm(samples.Plan(Decisions.None), samples.Plan);
		form.ShowDialog();
	}

	private static void Profiles(string stem, Action<Form, string> capture)
	{
		IReadOnlyList<ProfileSettings> all = RunSamples.ProfileSettingsList();
		using var picker = new ProfilePicker(all, [all[2].Id, all[0].Id]);
		Place(picker);
		capture(picker, stem + "-run-profiles.png");
	}

	private static void Questions(string stem, Action<Form, string> capture, Plan plan)
	{
		Question issue = plan.Questions.Single();
		using (var dialog = new QuestionDialog(issue))
		{
			Place(dialog);
			capture(dialog, stem + "-run-question-issue.png");
		}
		var series = new Question(
			new QuestionKey(Fields.Writer, new AnswerScope.Series(new SeriesKey("Vertigo", "Sandman", null))),
			"Writer",
			["Neil Gaiman", "Mike Dringenberg", "Sam Kieth", "Chris Bachalo", "Mark Hempel"],
			["Neil Gaiman"],
			"Sandman #1",
			75);
		using (var dialog = new QuestionDialog(series))
		{
			Place(dialog);
			capture(dialog, stem + "-run-question-series.png");
		}
		using (var editor = new PathEditorDialog("A file already exists. Type the full path to use for this book.", @"D:\Comics\Vertigo\Preacher\Preacher #001 (Garth Ennis).cbz", 259))
		{
			Place(editor);
			capture(editor, stem + "-run-path.png");
		}
		using (var editor = new PathEditorDialog("The path has 301 characters, and the limit is 259.", @"D:\Comics\Vertigo\Preacher\Preacher: #001 (Garth Ennis).cbz", 40))
		{
			Place(editor);
			capture(editor, stem + "-run-path-error.png");
		}
	}

	private static void Review(string stem, Action<Form, string> capture, RunSamples samples, Plan first)
	{
		using var form = new ReviewForm(first, samples.Plan);
		Place(form);
		capture(form, stem + "-run-review.png");

		Question question = first.Questions.Single();
		Decisions answered = form.Decisions
			.WithAnswer(question.Key, new Answer(["Chris Claremont", "Len Wein"], AsFolders: false, EveryIssue: false, SaveAsPreset: false, PresetAskWhenMore: false));
		Wait(form.ApplyAsync(answered));
		capture(form, stem + "-run-review-answered.png");

		Wait(form.ApplyAsync(form.Decisions.WithDefault(new Resolution.KeepBoth())));
		capture(form, stem + "-run-review-resolved.png");

		form.ToggleFilter(ReviewOutcome.Skipped, true);
		form.ToggleFilter(ReviewOutcome.Failed, true);
		form.SetSearchText("image");
		capture(form, stem + "-run-review-filtered.png");
	}

	private static void Large(string stem, Action<Form, string> capture)
	{
		RunSamples samples = RunSamples.Large(10_000);
		var clock = Stopwatch.StartNew();
		Plan plan = samples.Plan(Decisions.None);
		Console.Out.WriteLine($"10k books planned in {clock.ElapsedMilliseconds} ms, {plan.Entries.Count} entries, {plan.Conflicts.Count} conflicts");

		clock.Restart();
		using var form = new ReviewForm(plan, samples.Plan);
		Console.Out.WriteLine($"10k rows: form built and rows shown in {clock.ElapsedMilliseconds} ms");
		Place(form);
		capture(form, stem + "-run-review-10k.png");

		clock.Restart();
		form.SetSearchText("series 07");
		Console.Out.WriteLine($"10k rows: text filter in {clock.ElapsedMilliseconds} ms");
		clock.Restart();
		Plan again = samples.Plan(form.Decisions.WithDefault(new Resolution.Skip()));
		Console.Out.WriteLine($"10k rows: planning alone took {clock.ElapsedMilliseconds} ms ({again.Entries.Count} entries)");
		clock.Restart();
		Wait(form.ApplyAsync(form.Decisions.WithDefault(new Resolution.Skip())));
		Console.Out.WriteLine($"10k rows: re-plan and redisplay in {clock.ElapsedMilliseconds} ms");
	}

	private static void Place(Form form)
	{
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(-32000, 0);
		form.Show();
		Application.DoEvents();
	}

	private static void Wait(Task task)
	{
		while (!task.IsCompleted)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
		task.GetAwaiter().GetResult();
	}
}
