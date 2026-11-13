using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Run;

/// <summary>
/// Asks which items of a multi-value field a path uses, for one <see cref="Question"/>. <see cref="Result"/> is set when
/// the dialog closes with OK. Cancel leaves it null and means the user abandons the run.
/// </summary>
public sealed class QuestionDialog : Form
{
	private readonly Question question;
	private readonly ItemSelection items;
	private readonly ListBox available = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
	private readonly ListBox selected = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
	private readonly CheckBox asFolders = new() { Text = "Put each item in its own folder", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
	private readonly CheckBox everyIssue = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
	private readonly CheckBox saveAsPreset = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
	private readonly CheckBox askWhenMore = new() { Text = "Ask again when an issue has more items than these", AutoSize = true, Margin = new Padding(20, 2, 0, 0) };

	public QuestionDialog(Question question)
	{
		this.question = question;
		items = new ItemSelection(question.Choices, question.Preselected);
		bool series = question.Key.Scope is AnswerScope.Series;
		string field = question.FieldName;

		Text = $"Choose which {field} items to use";
		Font = SystemFonts.MessageBoxFont;
		AutoScaleMode = AutoScaleMode.Font;
		FormBorderStyle = FormBorderStyle.Sizable;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(660, 500);
		MinimumSize = new Size(560, 440);

		everyIssue.Text = "Use every selected item for this series, even on issues that do not have it";
		saveAsPreset.Text = "Remember this choice: use it for every issue that has these items";
		saveAsPreset.CheckedChanged += (_, _) => askWhenMore.Enabled = saveAsPreset.Checked;
		askWhenMore.Enabled = false;

		string waiting = question.Waiting == 1 ? "1 book waits for this answer." : $"{question.Waiting} books wait for this answer.";
		string scope = series ? "The answer applies to every issue of the series." : "The answer applies to this issue.";
		var intro = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
		intro.Controls.Add(new Label { Text = $"Which {field} items should the path use?", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
		intro.Controls.Add(new Label { Text = $"Example book: {question.ExampleCaption}", AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 2) });
		intro.Controls.Add(Ui.Hint($"{waiting} {scope}"));

		var moves = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(8, 0, 8, 0) };
		moves.Controls.Add(Ui.Button("Add >", (_, _) => Add()));
		moves.Controls.Add(Ui.Button("< Remove", (_, _) => Remove()));
		moves.Controls.Add(Ui.Button("Move up", (_, _) => Shift(-1)));
		moves.Controls.Add(Ui.Button("Move down", (_, _) => Shift(1)));
		foreach (Control button in moves.Controls)
		{
			button.Margin = new Padding(0, 0, 0, 6);
			button.AutoSize = false;
			button.Size = new Size(96, 28);
		}
		available.DoubleClick += (_, _) => Add();
		selected.DoubleClick += (_, _) => Remove();

		var lists = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill };
		lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		lists.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		lists.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		lists.Controls.Add(Ui.Label("Available items"), 0, 0);
		lists.Controls.Add(Ui.Label("Selected, in path order"), 2, 0);
		lists.Controls.Add(available, 0, 1);
		lists.Controls.Add(moves, 1, 1);
		lists.Controls.Add(selected, 2, 1);

		var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
		if (series)
		{
			options.Controls.Add(everyIssue);
		}
		else
		{
			options.Controls.Add(saveAsPreset);
			options.Controls.Add(askWhenMore);
		}
		options.Controls.Add(asFolders);
		options.Controls.Add(Ui.Hint("Selecting nothing writes nothing for this field."));

		var ok = new Button { Text = "OK", Width = 90, UseVisualStyleBackColor = true };
		var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
		ok.Click += (_, _) => Accept();
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
		buttons.Controls.AddRange([cancel, ok]);
		AcceptButton = ok;
		CancelButton = cancel;

		var main = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill, Padding = new Padding(12) };
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.Controls.Add(intro, 0, 0);
		main.Controls.Add(lists, 0, 1);
		main.Controls.Add(options, 0, 2);
		main.Controls.Add(buttons, 0, 3);
		Controls.Add(main);
		Refill();
	}

	/// <summary>The user's answer, set when the dialog closes with OK.</summary>
	public Answer? Result { get; private set; }

	/// <summary>Shows the dialog. Null when the user cancels, which abandons the run.</summary>
	public static Answer? Ask(IWin32Window? owner, Question question)
	{
		using var dialog = new QuestionDialog(question);
		return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
	}

	private void Accept()
	{
		Result = items.ToAnswer(question.Key.Scope, asFolders.Checked, everyIssue.Checked, saveAsPreset.Checked, askWhenMore.Checked);
		DialogResult = DialogResult.OK;
	}

	private void Add()
	{
		string[] picked = [.. available.SelectedItems.Cast<string>()];
		items.Add(picked);
		Refill(picked, inAvailable: false);
	}

	private void Remove()
	{
		string[] picked = [.. selected.SelectedItems.Cast<string>()];
		items.Remove(picked);
		Refill(picked, inAvailable: true);
	}

	private void Shift(int delta)
	{
		string[] picked = [.. selected.SelectedItems.Cast<string>()];
		items.Shift(picked, delta);
		Refill(picked, inAvailable: false);
	}

	private void Refill(IReadOnlyCollection<string>? keep = null, bool inAvailable = false)
	{
		ListBox target = inAvailable ? this.available : selected;
		this.available.BeginUpdate();
		selected.BeginUpdate();
		this.available.Items.Clear();
		this.available.Items.AddRange([.. items.Available.Cast<object>()]);
		selected.Items.Clear();
		selected.Items.AddRange([.. items.Selected.Cast<object>()]);
		foreach (string item in keep ?? [])
		{
			int index = target.Items.IndexOf(item);
			if (index >= 0)
			{
				target.SelectedIndices.Add(index);
			}
		}
		this.available.EndUpdate();
		selected.EndUpdate();
	}
}

/// <summary>The two lists of <see cref="QuestionDialog"/> and the answer they make, apart from the controls.</summary>
internal sealed class ItemSelection
{
	private readonly List<string> choices;
	private readonly List<string> chosen = [];

	/// <param name="choices">Every item the user may pick.</param>
	/// <param name="preselected">Items to start selected, in order. Items that are not choices are dropped.</param>
	public ItemSelection(IReadOnlyList<string> choices, IReadOnlyList<string> preselected)
	{
		this.choices = [.. choices.Distinct(StringComparer.OrdinalIgnoreCase)];
		Add(preselected);
	}

	public IReadOnlyList<string> Selected => chosen;

	/// <summary>The choices not selected, sorted.</summary>
	public IReadOnlyList<string> Available
		=> [.. choices.Where(choice => !chosen.Contains(choice, StringComparer.OrdinalIgnoreCase)).OrderBy(choice => choice, StringComparer.OrdinalIgnoreCase)];

	/// <summary>Appends each item that is a choice and not yet selected.</summary>
	public void Add(IEnumerable<string> picked)
	{
		foreach (string item in picked)
		{
			string? choice = choices.FirstOrDefault(known => string.Equals(known, item, StringComparison.OrdinalIgnoreCase));
			if (choice is not null && !chosen.Contains(choice, StringComparer.OrdinalIgnoreCase))
			{
				chosen.Add(choice);
			}
		}
	}

	public void Remove(IEnumerable<string> picked)
		=> chosen.RemoveAll(item => picked.Contains(item, StringComparer.OrdinalIgnoreCase));

	/// <summary>Moves the picked selected items one place up (negative) or down.</summary>
	public void Shift(IEnumerable<string> picked, int delta)
	{
		int[] indexes = [.. picked.Select(item => chosen.FindIndex(known => string.Equals(known, item, StringComparison.OrdinalIgnoreCase))).Where(index => index >= 0).OrderBy(index => index)];
		ProfileOrder.Shift(chosen, indexes, delta);
	}

	/// <summary>
	/// The answer for the checkbox states. A flag that does not apply to the scope is dropped, so the answer never says
	/// more than the dialog showed.
	/// </summary>
	public Answer ToAnswer(AnswerScope scope, bool asFolders, bool everyIssue, bool saveAsPreset, bool askWhenMore)
	{
		bool preset = scope is AnswerScope.Issue && saveAsPreset && chosen.Count > 0;
		return new Answer([.. chosen], asFolders, scope is AnswerScope.Series && everyIssue, preset, preset && askWhenMore);
	}
}
