using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Execution;

/// <summary>What a run did, one row per book and profile. Failed rows are red. The log can be saved as tab-separated text.</summary>
internal sealed class ReportForm : Form
{
	private readonly IReadOnlyList<ReportRow> rows;

	public ReportForm(string title, RunReport report)
	{
		rows = ReportRows.From(report);
		Text = title;
		StartPosition = FormStartPosition.CenterParent;
		ShowInTaskbar = false;
		MinimizeBox = false;
		Size = new Size(1000, 560);
		Padding = new Padding(8);

		var summary = new Label { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Text = ReportRows.Summary(rows, report.Cancelled) };
		var grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
		};
		grid.Columns.Add("Book", "Book");
		grid.Columns.Add("Profile", "Profile");
		grid.Columns.Add("Result", "Result");
		grid.Columns.Add("Detail", "Detail");
		grid.Columns[3].FillWeight = 250;
		foreach (ReportRow row in rows.OrderBy(r => r.Status == ReportStatus.Failed ? 0 : 1))
		{
			int index = grid.Rows.Add(row.Book, row.Profile, ReportRows.Text(row.Status), row.Detail);
			if (row.Status == ReportStatus.Failed)
			{
				grid.Rows[index].DefaultCellStyle.ForeColor = Ui.ErrorColor;
			}
		}

		var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
		var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
		buttons.Controls.Add(close);
		buttons.Controls.Add(Ui.Button("Save log...", (_, _) => SaveLog()));
		AcceptButton = CancelButton = close;
		Controls.Add(grid);
		Controls.Add(summary);
		Controls.Add(buttons);
	}

	public static void Show(IWin32Window owner, string title, RunReport report)
	{
		using var form = new ReportForm(title, report);
		form.ShowDialog(owner);
	}

	private void SaveLog()
	{
		using var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "Library Organizer log.txt" };
		if (dialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		File.WriteAllLines(dialog.FileName, new[] { "Book\tProfile\tResult\tDetail" }
			.Concat(rows.Select(r => string.Join("\t", r.Book, r.Profile, ReportRows.Text(r.Status), r.Detail))));
	}
}
