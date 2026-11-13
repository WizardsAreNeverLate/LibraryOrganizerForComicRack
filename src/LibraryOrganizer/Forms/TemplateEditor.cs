using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>
/// One template box with its parse errors and preview. Errors come from <see cref="Template.Parse"/> as the user types:
/// the message goes under the box and each span is coloured in the text.
/// </summary>
internal sealed class TemplateEditor : TableLayoutPanel
{
	private const int WmSetRedraw = 0x000B;
	private const int MaxErrorsShown = 3;

	private static readonly Color SpanBack = Color.FromArgb(255, 205, 205);
	private static readonly Color SpanFore = Color.FromArgb(140, 0, 0);

	private readonly TemplateTarget target;
	private readonly Label title;
	private readonly RichTextBox box;
	private readonly Label errors = new() { AutoSize = true, ForeColor = Ui.ErrorColor, Margin = new Padding(0, 2, 0, 0), MaximumSize = new Size(900, 0) };
	private readonly Label preview = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 6), MaximumSize = new Size(900, 0), Text = "" };
	private int highlighting;

	public TemplateEditor(string titleText, TemplateTarget target, Control? headerButton = null)
	{
		this.target = target;
		ColumnCount = 1;
		AutoSize = true;
		Dock = DockStyle.Top;
		Margin = new Padding(0, 0, 0, 4);
		ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

		title = Ui.Label(titleText);
		title.Font = new Font(Font, FontStyle.Bold);
		var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		header.Controls.Add(title, 0, 0);
		if (headerButton is not null)
		{
			headerButton.Anchor = AnchorStyles.Right;
			header.Controls.Add(headerButton, 1, 0);
		}

		box = new RichTextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			WordWrap = true,
			ScrollBars = RichTextBoxScrollBars.Vertical,
			DetectUrls = false,
			AcceptsTab = false,
			EnableAutoDragDrop = false,
			Font = new Font("Consolas", 10f),
			Height = 52,
			Margin = new Padding(0, 0, 0, 0),
		};
		box.TextChanged += (_, _) => OnBoxChanged();
		box.KeyDown += OnBoxKeyDown;

		RowCount = 4;
		foreach (Control control in new Control[] { header, box, errors, preview })
		{
			RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(control, 0, Controls.Count);
		}
	}

	public event EventHandler? ValueChanged;

	/// <summary>The box, for focus tracking.</summary>
	public RichTextBox Box => box;

	public string Value
	{
		get => box.Text;
		set => box.Text = value;
	}

	/// <summary>The current text parsed. Always fresh, so callers never read stale errors.</summary>
	public TemplateParse Parse => Template.Parse(box.Text, target);

	public bool Active => box.Enabled;

	public void SetPreview(string text) => preview.Text = text;

	public void SetActive(bool active)
	{
		box.Enabled = active;
		title.Enabled = active;
		if (!active)
		{
			errors.Text = "";
			preview.Text = "";
		}
	}

	public void InsertAtCaret(string text)
	{
		box.Focus();
		box.SelectedText = text;
		box.Focus();
	}

	private void OnBoxChanged()
	{
		if (highlighting > 0)
		{
			return;
		}
		Revalidate();
		ValueChanged?.Invoke(this, EventArgs.Empty);
	}

	private void Revalidate()
	{
		IReadOnlyList<TemplateError> found = Parse.Errors;
		errors.Text = Describe(found);
		Highlight(found);
	}

	private static string Describe(IReadOnlyList<TemplateError> found)
	{
		if (found.Count == 0)
		{
			return "";
		}
		IEnumerable<string> lines = found.Take(MaxErrorsShown).Select(error => $"Character {error.Start + 1}: {error.Message}");
		string text = string.Join(Environment.NewLine, lines);
		return found.Count > MaxErrorsShown ? text + Environment.NewLine + $"...and {found.Count - MaxErrorsShown} more." : text;
	}

	private void Highlight(IReadOnlyList<TemplateError> found)
	{
		int length = box.TextLength;
		if (length == 0)
		{
			return;
		}
		int selectionStart = box.SelectionStart;
		int selectionLength = box.SelectionLength;
		highlighting++;
		SendMessage(box.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
		try
		{
			box.SelectAll();
			box.SelectionBackColor = box.BackColor;
			box.SelectionColor = box.ForeColor;
			foreach (TemplateError error in found)
			{
				int start = Math.Min(Math.Max(error.Start, 0), length - 1);
				int span = Math.Min(Math.Max(error.Length, 1), length - start);
				box.Select(start, span);
				box.SelectionBackColor = SpanBack;
				box.SelectionColor = SpanFore;
			}
			box.Select(selectionStart, selectionLength);
		}
		finally
		{
			SendMessage(box.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
			box.Invalidate();
			highlighting--;
		}
	}

	private void OnBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Enter)
		{
			e.SuppressKeyPress = true;
		}
		else if (e.Control && e.KeyCode == Keys.V)
		{
			e.SuppressKeyPress = true;
			if (Clipboard.ContainsText())
			{
				box.SelectedText = Clipboard.GetText().Replace("\r", "").Replace("\n", "");
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
}
