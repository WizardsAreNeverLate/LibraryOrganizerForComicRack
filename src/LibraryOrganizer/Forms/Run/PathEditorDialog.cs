using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Run;

/// <summary>Edits the target path of a conflicting book. OK stays disabled until <see cref="PathCheck"/> accepts the text.</summary>
internal sealed class PathEditorDialog : Form
{
	private readonly int maxLength;
	private readonly TextBox box = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
	private readonly Label length = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 2) };
	private readonly Label problem = new() { AutoSize = true, ForeColor = Ui.ErrorColor, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 4) };
	private readonly Button ok = new() { Text = "OK", Width = 90, UseVisualStyleBackColor = true };

	public PathEditorDialog(string reason, string initial, int maxLength)
	{
		this.maxLength = maxLength;
		Text = "Use another path";
		Font = SystemFonts.MessageBoxFont;
		AutoScaleMode = AutoScaleMode.Font;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(600, 190);

		var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
		ok.Click += (_, _) => DialogResult = DialogResult.OK;
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		buttons.Controls.AddRange([cancel, ok]);
		AcceptButton = ok;
		CancelButton = cancel;

		var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
		for (int i = 0; i < 4; i++)
		{
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		}
		table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		var intro = Ui.Label(reason);
		intro.MaximumSize = new Size(560, 0);
		table.Controls.Add(intro, 0, 0);
		table.Controls.Add(box, 0, 1);
		table.Controls.Add(length, 0, 2);
		table.Controls.Add(problem, 0, 3);
		table.Controls.Add(buttons, 0, 4);
		Controls.Add(table);

		box.TextChanged += (_, _) => Check();
		box.Text = initial;
		box.SelectionStart = box.TextLength;
	}

	public AbsPath? Result { get; private set; }

	public static AbsPath? Edit(IWin32Window? owner, string reason, string initial, int maxLength)
	{
		using var dialog = new PathEditorDialog(reason, initial, maxLength);
		return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		box.Focus();
	}

	private void Check()
	{
		string? error = PathCheck.Validate(box.Text, maxLength, out AbsPath? path);
		Result = path;
		length.Text = $"{box.Text.Trim().Length} of {maxLength} characters";
		problem.Text = error ?? "";
		ok.Enabled = error is null;
	}
}
