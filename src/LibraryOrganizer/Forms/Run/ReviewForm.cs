using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Run;

/// <summary>
/// The plan as a grid, before anything runs. The plan is the simulation: Close does nothing, and Run returns the plan
/// the user approved. The form never plans: every answer and conflict choice becomes <see cref="Decisions"/> and the
/// caller's <c>replan</c> function builds the next plan, on a worker thread.
/// </summary>
public sealed class ReviewForm : Form
{
	private static readonly Color Attention = Color.FromArgb(150, 90, 0);

	private sealed class Grid : DataGridView
	{
		public Grid() => DoubleBuffered = true;
	}

	private readonly Func<Decisions, Plan> replan;
	private readonly int maxPathLength;
	private readonly Grid grid = new();
	private readonly DataGridViewColumn resolutionColumn;
	private readonly Dictionary<ReviewOutcome, CheckBox> filterButtons = [];
	private readonly TextBox search = new() { Width = 220, Margin = new Padding(0, 2, 6, 0) };
	private readonly Timer searchDelay = new() { Interval = 250 };
	private readonly ComboBox defaultChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(0, 2, 0, 0) };
	private readonly Label shown = new() { AutoSize = false, Size = new Size(140, 20), ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 0, 0) };
	private readonly Label summary = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
	private readonly Label blockers = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Attention };
	private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Width = 120, Height = 14, Visible = false, Margin = new Padding(0, 8, 8, 0) };
	private readonly ListBox questionList = new() { Dock = DockStyle.Fill, IntegralHeight = false, Height = 56, FormattingEnabled = true };
	private readonly Button answer;
	private readonly Control questionPanel;
	private readonly Control body;
	private readonly Button run;
	private readonly ToolTip tips = new();

	private Decisions decisions;
	private IReadOnlyList<ReviewRow> rows = [];
	private int[] visible = [];
	private ApprovedPlan? approvable;
	private bool busy;
	private bool closed;
	private bool syncing;

	/// <param name="initial">The plan to show first.</param>
	/// <param name="replan">Plans again with more decisions. Called on a worker thread, so it must not touch controls.</param>
	/// <param name="initialDecisions">The decisions <paramref name="initial"/> was planned with. Null means its answers and nothing else.</param>
	/// <param name="maxPathLength">The limit the path editor checks a typed path against, unless the conflict names its own.</param>
	public ReviewForm(Plan initial, Func<Decisions, Plan> replan, Decisions? initialDecisions = null, int maxPathLength = 259)
	{
		this.replan = replan;
		this.maxPathLength = maxPathLength;
		decisions = initialDecisions ?? Decisions.None with { Answers = initial.Answers };

		Text = "Library Organizer: review";
		Font = SystemFonts.MessageBoxFont;
		AutoScaleMode = AutoScaleMode.Font;
		FormBorderStyle = FormBorderStyle.Sizable;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(1240, 740);
		MinimumSize = new Size(900, 520);

		answer = Ui.Button("Answer...", (_, _) => AnswerSelected());
		resolutionColumn = ConfigureGrid();
		questionPanel = BuildQuestionPanel();
		Control filters = BuildFilterBar();

		run = new Button { Text = "Run", AutoSize = true, MinimumSize = new Size(100, 0), UseVisualStyleBackColor = true, Enabled = false };
		var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 0), DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
		run.Click += (_, _) => Approve();
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
		buttons.Controls.AddRange([close, run, progress]);
		CancelButton = close;

		var status = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true };
		status.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		status.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		status.Controls.Add(summary, 0, 0);
		status.Controls.Add(blockers, 0, 1);

		var content = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill };
		content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		content.Controls.Add(questionPanel, 0, 0);
		content.Controls.Add(filters, 0, 1);
		content.Controls.Add(grid, 0, 2);
		content.Controls.Add(status, 0, 3);
		body = content;

		var main = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Padding = new Padding(12) };
		main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.Controls.Add(content, 0, 0);
		main.Controls.Add(buttons, 0, 1);
		Controls.Add(main);

		searchDelay.Tick += (_, _) =>
		{
			searchDelay.Stop();
			ApplyFilter();
		};
		FormClosed += (_, _) =>
		{
			closed = true;
			searchDelay.Dispose();
		};
		Display(initial);
	}

	/// <summary>The plan the user approved, set when the form closes with Run. Null after Close.</summary>
	public ApprovedPlan? Approved { get; private set; }

	/// <summary>Everything the user has decided so far, including answers: the host keeps the answers for the session.</summary>
	public Decisions Decisions => decisions;

	/// <summary>Plans again with <paramref name="next"/> and shows the result. Ignored while another re-plan runs.</summary>
	internal async Task ApplyAsync(Decisions next)
	{
		if (busy)
		{
			return;
		}
		SetBusy(true);
		try
		{
			Plan result = await Task.Run(() => replan(next));
			if (!closed)
			{
				decisions = next;
				Display(result);
			}
		}
		catch (Exception error)
		{
			if (!closed)
			{
				SyncDefaultChoice();
				Ui.Warn(this, "Planning failed", error.Message);
			}
		}
		finally
		{
			if (!closed)
			{
				SetBusy(false);
			}
		}
	}

	internal void ToggleFilter(ReviewOutcome outcome, bool on) => filterButtons[outcome].Checked = on;

	internal void SetSearchText(string text)
	{
		search.Text = text;
		searchDelay.Stop();
		ApplyFilter();
	}

	private DataGridViewColumn ConfigureGrid()
	{
		grid.Dock = DockStyle.Fill;
		grid.VirtualMode = true;
		grid.ReadOnly = true;
		grid.AllowUserToAddRows = false;
		grid.AllowUserToDeleteRows = false;
		grid.AllowUserToResizeRows = false;
		grid.RowHeadersVisible = false;
		grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		grid.MultiSelect = false;
		grid.BackgroundColor = SystemColors.Window;
		grid.BorderStyle = BorderStyle.FixedSingle;
		grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
		grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
		grid.EnableHeadersVisualStyles = true;
		grid.Margin = new Padding(0, 6, 0, 0);
		grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
		grid.FitsFont();

		DataGridViewTextBoxColumn Column(string name, string header, int width, bool fill = false)
		{
			var column = new DataGridViewTextBoxColumn
			{
				Name = name,
				HeaderText = header,
				Width = width,
				MinimumWidth = 60,
				SortMode = DataGridViewColumnSortMode.NotSortable,
				AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
				FillWeight = 100,
			};
			grid.Columns.Add(column);
			return column;
		}

		Column("Book", "Book", 190);
		Column("Profile", "Profile", 100);
		Column("Outcome", "Outcome", 250);
		DataGridViewTextBoxColumn resolution = Column("Resolution", "Resolution", 250);
		Column("Action", "Action", 120);
		Column("From", "From", 200, fill: true);
		Column("To", "To", 200, fill: true);

		grid.CellValueNeeded += (_, e) => e.Value = RowAt(e.RowIndex) is { } item ? CellText(item, e.ColumnIndex) : "";
		grid.CellFormatting += FormatCell;
		grid.CellToolTipTextNeeded += (_, e) =>
		{
			if (e.RowIndex >= 0 && e.RowIndex < visible.Length)
			{
				e.ToolTipText = RowAt(e.RowIndex) is { } item ? CellText(item, e.ColumnIndex) : "";
			}
		};
		grid.CellMouseClick += (_, e) =>
		{
			if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex == resolution.Index)
			{
				ShowResolutionMenu(e.RowIndex);
			}
		};
		grid.CellMouseDoubleClick += (_, e) =>
		{
			if (e.RowIndex >= 0 && e.ColumnIndex != resolution.Index)
			{
				ShowResolutionMenu(e.RowIndex);
			}
		};
		grid.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Enter && grid.CurrentRow is { } current)
			{
				ShowResolutionMenu(current.Index);
				e.Handled = true;
			}
		};
		return resolution;
	}

	private Control BuildQuestionPanel()
	{
		questionList.Format += (_, e) => e.Value = e.ListItem is Question question ? DescribeQuestion(question) : e.Value;
		questionList.DoubleClick += (_, _) => AnswerSelected();
		answer.Margin = new Padding(8, 0, 0, 0);
		var panel = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
		var title = Ui.Label("These books wait for a choice of items. Answer each question to see where they go.");
		title.ForeColor = Attention;
		panel.Controls.Add(title, 0, 0);
		panel.SetColumnSpan(title, 2);
		panel.Controls.Add(questionList, 0, 1);
		panel.Controls.Add(answer, 1, 1);
		answer.Anchor = AnchorStyles.Top | AnchorStyles.Left;
		return panel;
	}

	private Control BuildFilterBar()
	{
		var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
		var second = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0) };
		foreach (ReviewOutcome outcome in Enum.GetValues(typeof(ReviewOutcome)))
		{
			var button = new CheckBox
			{
				Appearance = Appearance.Button,
				AutoSize = true,
				MinimumSize = new Size(132, 0),
				TextAlign = ContentAlignment.MiddleCenter,
				Text = ReviewRows.Label(outcome),
				Margin = new Padding(0, 0, 4, 0),
				Padding = new Padding(4, 1, 4, 1),
				UseVisualStyleBackColor = true,
			};
			button.CheckedChanged += (_, _) => ApplyFilter();
			filterButtons[outcome] = button;
			bar.Controls.Add(button);
		}
		search.TextChanged += (_, _) =>
		{
			searchDelay.Stop();
			searchDelay.Start();
		};
		second.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
		second.Controls.Add(search);
		second.Controls.Add(new Label { Text = "For all conflicts:", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
		defaultChoice.Items.AddRange(["Ask for each", "Skip", "Replace", "Keep both"]);
		defaultChoice.FitsItems();
		defaultChoice.SelectedIndexChanged += (_, _) =>
		{
			if (!syncing)
			{
				_ = ApplyAsync(ReviewDecisions.Default(decisions, (ConflictDefault)defaultChoice.SelectedIndex));
			}
		};
		second.Controls.Add(defaultChoice);
		second.Controls.Add(shown);
		tips.SetToolTip(search, "Show only rows whose book, profile, outcome, path or reason contain this text.");
		tips.SetToolTip(defaultChoice, "Applies to every conflict that allows it and has no choice of its own.");
		var column = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
		column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		column.Controls.Add(bar, 0, 0);
		column.Controls.Add(second, 0, 1);
		return column;
	}

	private void Display(Plan next)
	{
		EntryKey? top = TopKey();
		body.SuspendLayout();
		approvable = next.Approve();
		rows = ReviewRows.Build(next);
		IReadOnlyDictionary<ReviewOutcome, int> counts = ReviewRows.Counts(rows);
		foreach (KeyValuePair<ReviewOutcome, CheckBox> pair in filterButtons)
		{
			pair.Value.Text = $"{ReviewRows.Label(pair.Key)}  {counts[pair.Key]}";
		}
		resolutionColumn.Visible = rows.Any(row => row.Conflict is not null);

		questionList.BeginUpdate();
		questionList.Items.Clear();
		questionList.Items.AddRange([.. next.Questions.Cast<object>()]);
		questionList.EndUpdate();
		if (questionList.Items.Count > 0)
		{
			questionList.SelectedIndex = 0;
		}
		questionPanel.Visible = next.Questions.Count > 0;

		SyncDefaultChoice();
		summary.Text = string.Join("\n", next.Tally().Select(Describe));
		tips.SetToolTip(summary, summary.Text);
		int openConflicts = next.Conflicts.Count(conflict => conflict.IsOpen);
		blockers.Text = approvable is null
			? $"{Plural(next.Questions.Count, "question")} and {Plural(openConflicts, "conflict")} still need a choice before the run."
			: "";
		run.Enabled = approvable is not null && !busy;
		body.ResumeLayout(true);
		ApplyFilter(top);
	}

	private void ApplyFilter() => ApplyFilter(TopKey());

	private EntryKey? TopKey() => grid.RowCount > 0 && RowAt(grid.FirstDisplayedScrollingRowIndex) is { } row ? row.Key : null;

	/// <summary>Null when the grid row is out of range, which the grid asks about while it clears its rows.</summary>
	private ReviewRow? RowAt(int gridRow) => gridRow >= 0 && gridRow < visible.Length && visible[gridRow] < rows.Count ? rows[visible[gridRow]] : null;

	private void ApplyFilter(EntryKey? top)
	{
		var outcomes = new HashSet<ReviewOutcome>(filterButtons.Where(pair => pair.Value.Checked).Select(pair => pair.Key));
		ReviewFilter filter = new(outcomes, search.Text.Trim());
		visible = [];
		grid.RowCount = 0;
		visible = ReviewRows.Filter(rows, filter);
		grid.RowCount = visible.Length;
		shown.Text = visible.Length == rows.Count ? $"{rows.Count} rows" : $"{visible.Length} of {rows.Count} rows";
		if (top is { } key)
		{
			int index = Array.FindIndex(visible, position => rows[position].Key == key);
			if (index >= 0)
			{
				grid.FirstDisplayedScrollingRowIndex = index;
			}
		}
		grid.Invalidate();
	}

	private void SyncDefaultChoice()
	{
		syncing = true;
		defaultChoice.SelectedIndex = (int)ReviewDecisions.DefaultOf(decisions);
		syncing = false;
	}

	private string CellText(ReviewRow item, int column)
	{
		return grid.Columns[column].Name switch
		{
			"Book" => item.Book,
			"Profile" => item.Profile,
			"Outcome" => item.OutcomeText,
			"Resolution" => item.Resolution.Length == 0 ? "" : item.Resolution + "  ▾",
			"Action" => item.Action,
			"From" => item.From,
			_ => item.To,
		};
	}

	private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
	{
		if (RowAt(e.RowIndex) is not { } row || e.CellStyle is not { } style)
		{
			return;
		}
		bool selected = grid.Rows.GetRowState(e.RowIndex).HasFlag(DataGridViewElementStates.Selected);
		if (selected)
		{
			return;
		}
		style.ForeColor = row.Outcome switch
		{
			ReviewOutcome.Failed => Ui.ErrorColor,
			ReviewOutcome.Waiting or ReviewOutcome.Blocked => Attention,
			ReviewOutcome.Skipped or ReviewOutcome.Overridden or ReviewOutcome.AlreadyInPlace => SystemColors.GrayText,
			_ => SystemColors.WindowText,
		};
		if (e.ColumnIndex == resolutionColumn.Index && row.Conflict is { } conflict)
		{
			style.ForeColor = conflict.IsOpen ? Attention : SystemColors.WindowText;
			style.BackColor = conflict.IsOpen ? Color.FromArgb(255, 244, 214) : SystemColors.Window;
		}
	}

	private void ShowResolutionMenu(int gridRow)
	{
		if (busy || RowAt(gridRow)?.Conflict is not { } conflict)
		{
			return;
		}
		var menu = new ContextMenuStrip();
		menu.Closed += (_, _) => BeginInvoke((Action)menu.Dispose);
		foreach (ResolutionChoice choice in conflict.Allowed)
		{
			var item = new ToolStripMenuItem(ReviewRows.ChoiceLabel(choice, conflict)) { Checked = conflict.Chosen?.Choice == choice };
			item.Click += (_, _) => Choose(conflict, choice);
			menu.Items.Add(item);
		}
		Rectangle cell = grid.GetCellDisplayRectangle(resolutionColumn.Visible ? resolutionColumn.Index : 0, gridRow, cutOverflow: false);
		menu.Show(grid, new Point(cell.Left, cell.Bottom));
	}

	private void Choose(Conflict conflict, ResolutionChoice choice)
	{
		AbsPath? typed = null;
		if (choice == ResolutionChoice.UsePath)
		{
			int limit = conflict.Kind is ConflictKind.PathTooLong tooLong ? tooLong.Max : maxPathLength;
			string initial = conflict.Chosen is Resolution.UsePath current ? current.Path.Value : conflict.Key.Target.Value;
			typed = PathEditorDialog.Edit(this, $"{conflict.Kind.Describe()}. Type the full path to use for this book.", initial, limit);
			if (typed is null)
			{
				return;
			}
		}
		_ = ApplyAsync(ReviewDecisions.Resolve(decisions, conflict, choice, typed));
	}

	private void AnswerSelected()
	{
		if (busy || (questionList.SelectedItem ?? questionList.Items.Cast<object>().FirstOrDefault()) is not Question question)
		{
			return;
		}
		if (QuestionDialog.Ask(this, question) is { } given)
		{
			_ = ApplyAsync(decisions.WithAnswer(question.Key, given));
		}
	}

	private void Approve()
	{
		if (approvable is null)
		{
			return;
		}
		Approved = approvable;
		DialogResult = DialogResult.OK;
	}

	private void SetBusy(bool on)
	{
		busy = on;
		body.Enabled = !on;
		progress.Visible = on;
		run.Enabled = !on && approvable is not null;
		UseWaitCursor = on;
	}

	private static string DescribeQuestion(Question question)
	{
		string scope = question.Key.Scope is AnswerScope.Series ? "the whole series" : "this issue";
		return $"{question.FieldName} for {question.ExampleCaption} ({scope}, {question.Waiting} waiting)";
	}

	private static string Describe(ProfileTally tally)
	{
		var parts = new List<string>();
		void Add(int count, string label)
		{
			if (count > 0)
			{
				parts.Add($"{count} {label}");
			}
		}
		Add(tally.Planned, "planned");
		Add(tally.InPlace, "already in place");
		Add(tally.Skipped, "skipped");
		Add(tally.Failed, "failed");
		Add(tally.Waiting, "waiting");
		Add(tally.Blocked, "blocked");
		return $"{tally.Profile}: {(parts.Count == 0 ? "nothing to do" : string.Join(", ", parts))}";
	}

	private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
