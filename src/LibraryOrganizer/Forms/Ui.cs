using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

internal static class Ui
{
	public static readonly Color ErrorColor = Color.FromArgb(192, 32, 32);

	public static Label Label(string text, bool grow = false)
		=> new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 6, 4), Dock = grow ? DockStyle.Fill : DockStyle.None };

	public static Label Hint(string text)
		=> new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 6), MaximumSize = new Size(640, 0) };

	/// <summary>
	/// Grows the list to its longest item in its own font, keeping its current width as a minimum. A fixed pixel width
	/// truncates items under display scaling, where the font grows and the width does not. Call <see cref="Fit"/> again
	/// after replacing the items.
	/// </summary>
	public static ComboBox FitsItems(this ComboBox combo)
	{
		combo.MinimumSize = new Size(combo.Width, 0);
		combo.FontChanged += (_, _) => combo.Fit();
		combo.Fit();
		return combo;
	}

	public static void Fit(this ComboBox combo)
	{
		int longest = 0;
		foreach (object item in combo.Items)
		{
			longest = Math.Max(longest, TextRenderer.MeasureText(combo.GetItemText(item), combo.Font).Width);
		}
		combo.Width = Math.Max(combo.MinimumSize.Width, longest + SystemInformation.VerticalScrollBarWidth + 8);
	}

	/// <summary>
	/// Sizes the header row and the rows to the grid's font, now and whenever the font changes. The default heights are
	/// fixed pixels, so display scaling cuts off the bottom of the text.
	/// </summary>
	public static DataGridView FitsFont(this DataGridView grid)
	{
		grid.FontChanged += (_, _) => FitRows(grid);
		FitRows(grid);
		return grid;
	}

	private static void FitRows(DataGridView grid)
	{
		int text = TextRenderer.MeasureText("Ag", grid.Font).Height;
		grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, text + 8);
		int row = TextRenderer.MeasureText("Ag", grid.Font).Height + 6;
		grid.RowTemplate.Height = row;
		if (!grid.VirtualMode)
		{
			foreach (DataGridViewRow existing in grid.Rows)
			{
				existing.Height = row;
			}
		}
	}

	public static Button Button(string text, EventHandler onClick)
	{
		var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1), Margin = new Padding(0, 0, 6, 0), UseVisualStyleBackColor = true };
		button.Click += onClick;
		return button;
	}

	public static TableLayoutPanel Column()
	{
		var table = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		return table;
	}

	public static void AddRow(this TableLayoutPanel table, Control control)
	{
		control.Dock = DockStyle.Top;
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.Controls.Add(control, 0, table.RowCount++);
	}

	public static FlowLayoutPanel Row(params Control[] controls)
	{
		var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
		flow.Controls.AddRange(controls);
		return flow;
	}

	public static GroupBox Section(string title, Control body)
	{
		var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 8) };
		// A docked child makes an auto-sized GroupBox leave out its padding, which cuts off the bottom of the body.
		// Anchored at the display rectangle, the body counts in full.
		body.Dock = DockStyle.None;
		body.Location = box.DisplayRectangle.Location;
		body.Width = box.DisplayRectangle.Width;
		body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		box.Controls.Add(body);
		return box;
	}

	public static string? PickFolder(IWin32Window owner, string current, string description)
	{
		using var dialog = new FolderBrowserDialog { Description = description, SelectedPath = current, ShowNewFolderButton = true };
		return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
	}

	public static void Warn(IWin32Window? owner, string title, string message)
		=> MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

internal sealed class FolderBox : TableLayoutPanel
{
	private readonly TextBox box = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 2) };
	private readonly string description;

	public FolderBox(string description)
	{
		this.description = description;
		ColumnCount = 2;
		RowCount = 1;
		AutoSize = true;
		ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Controls.Add(box, 0, 0);
		Controls.Add(Ui.Button("Browse...", (_, _) => Browse()), 1, 0);
		box.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? Changed;

	public string Value
	{
		get => box.Text;
		set => box.Text = value;
	}

	private void Browse()
	{
		if (Ui.PickFolder(this, box.Text, description) is { } folder)
		{
			box.Text = folder;
		}
	}
}

/// <summary>Asks for a profile name. Validates against the names already taken.</summary>
internal sealed class NameDialog : Form
{
	private readonly TextBox box = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
	private readonly Label problem = new() { AutoSize = true, ForeColor = Ui.ErrorColor, Margin = new Padding(0, 0, 0, 4) };
	private readonly Func<string, string?> check;

	public NameDialog(string title, string prompt, string initial, Func<string, string?> check)
	{
		this.check = check;
		Text = title;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowInTaskbar = false;
		AutoScaleMode = AutoScaleMode.Font;
		ClientSize = new Size(380, 130);
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowOnly;
		var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(75, 0), DialogResult = DialogResult.None, UseVisualStyleBackColor = true };
		var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(75, 0), DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
		ok.Click += (_, _) => Accept();
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
		buttons.Controls.AddRange([cancel, ok]);
		var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12), AutoSize = true };
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.Controls.Add(Ui.Label(prompt), 0, 0);
		table.Controls.Add(box, 0, 1);
		table.Controls.Add(problem, 0, 2);
		table.Controls.Add(buttons, 0, 3);
		Controls.Add(table);
		AcceptButton = ok;
		CancelButton = cancel;
		box.Text = initial;
		box.SelectAll();
		box.TextChanged += (_, _) => problem.Text = "";
	}

	public string Value => box.Text.Trim();

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		box.Focus();
	}

	private void Accept()
	{
		string? error = check(box.Text.Trim());
		if (error is null)
		{
			DialogResult = DialogResult.OK;
		}
		else
		{
			problem.Text = error;
		}
	}
}
