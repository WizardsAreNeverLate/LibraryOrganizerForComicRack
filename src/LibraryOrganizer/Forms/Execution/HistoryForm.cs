using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Execution;

internal enum HistoryAction { Undo, Finish }

/// <summary>Past runs, newest first. Undo plans the way back from what is on disk now; Finish resumes an interrupted run.</summary>
internal sealed class HistoryForm : Form
{
	private readonly IReadOnlyList<RunSummary> runs;
	private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
	private readonly Button undo;
	private readonly Button finish;

	private HistoryForm(IReadOnlyList<RunSummary> runs)
	{
		this.runs = runs;
		Text = "Library Organizer: history";
		StartPosition = FormStartPosition.CenterParent;
		ShowInTaskbar = false;
		MinimizeBox = false;
		Size = new Size(820, 420);
		Padding = new Padding(8);
		list.Columns.Add("Started", 140);
		list.Columns.Add("Kind", 70);
		list.Columns.Add("Profiles", 230);
		list.Columns.Add("Changes", 120);
		list.Columns.Add("State", 200);
		foreach (RunSummary run in runs)
		{
			list.Items.Add(new ListViewItem(
			[
				run.Started.ToLocalTime().ToString("g"),
				run.Kind is PlanKind.Undo ? "Undo" : "Organize",
				string.Join(", ", run.Profiles),
				$"{run.Done} of {run.Operations}" + (run.Failed > 0 ? $", {run.Failed} failed" : ""),
				!run.Finished ? "Interrupted" : run.Cancelled ? "Cancelled" : "Finished",
			]));
		}

		undo = Ui.Button("Undo...", (_, _) => Choose(HistoryAction.Undo));
		finish = Ui.Button("Finish", (_, _) => Choose(HistoryAction.Finish));
		var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
		buttons.Controls.AddRange([close, undo, finish]);
		CancelButton = close;
		var hint = new Label
		{
			Dock = DockStyle.Top,
			Height = 36,
			Text = "Undo shows what it would move back before it changes anything. A file that was replaced went to the Recycle Bin and is not restored.",
		};
		Controls.Add(list);
		Controls.Add(hint);
		Controls.Add(buttons);
		list.SelectedIndexChanged += (_, _) => UpdateButtons();
		list.DoubleClick += (_, _) => Choose(HistoryAction.Undo);
		if (list.Items.Count > 0)
		{
			list.Items[0].Selected = true;
		}
		UpdateButtons();
	}

	public RunId? Run { get; private set; }

	public HistoryAction Action { get; private set; }

	/// <summary>The run and what to do with it, or null when the user closes the window.</summary>
	public static (RunId Run, HistoryAction Action)? Pick(IWin32Window owner, IReadOnlyList<RunSummary> runs)
	{
		if (runs.Count == 0)
		{
			MessageBox.Show(owner, "Library Organizer has not run yet.", "Library Organizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return null;
		}
		using var form = new HistoryForm(runs);
		return form.ShowDialog(owner) == DialogResult.OK && form.Run is RunId run ? (run, form.Action) : null;
	}

	private RunSummary? Selected => list.SelectedIndices.Count == 1 ? runs[list.SelectedIndices[0]] : null;

	private void UpdateButtons()
	{
		undo.Enabled = Selected is not null;
		finish.Enabled = Selected is { Finished: false };
	}

	private void Choose(HistoryAction action)
	{
		if (Selected is not RunSummary run || (action == HistoryAction.Finish && run.Finished))
		{
			return;
		}
		Run = run.Id;
		Action = action;
		DialogResult = DialogResult.OK;
	}
}
