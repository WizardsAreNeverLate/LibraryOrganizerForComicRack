using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>
/// The inputs for one field's token: text before and after, then whatever its kind needs (padding, separator, date format...).
/// Rebuilds when the field changes and shows the token it will insert, or why it cannot.
/// </summary>
internal sealed class InsertPanel : TableLayoutPanel
{
	private static readonly (string Text, AnswerScopeKind Scope)[] ScopeChoices =
	[
		("For each issue (ask per book)", AnswerScopeKind.Issue),
		("Once for the whole series", AnswerScopeKind.Series),
	];

	private readonly Label heading = Ui.Label("");
	private readonly Label tokenText = new() { AutoSize = true, Font = new Font("Consolas", 10f), Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(520, 0) };
	private readonly Label tokenError = new() { AutoSize = true, ForeColor = Ui.ErrorColor, MaximumSize = new Size(520, 0) };
	private readonly Button insert = Ui.Button("Insert", (_, _) => { });
	private readonly TableLayoutPanel inputs = new() { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
	private readonly IReadOnlyList<string> customKeys;

	private Field? shown;
	private bool autoSpace = true;
	private TextBox prefix = new();
	private TextBox postfix = new();
	private ComboBox? pad;
	private ComboBox? monthShow;
	private TextBox? separator;
	private ComboBox? scope;
	private TextBox? yesNoText;
	private CheckBox? invert;
	private TextBox? readText;
	private ComboBox? readOperator;
	private NumericUpDown? readPercent;
	private NumericUpDown? counterStart;
	private NumericUpDown? counterStep;
	private NumericUpDown? counterPad;
	private ComboBox? dateFormat;
	private Label? dateSample;
	private ComboBox? custom;
	private ComboBox? firstLetter;

	public InsertPanel(IReadOnlyList<string> customKeys)
	{
		this.customKeys = customKeys;
		ColumnCount = 1;
		AutoSize = true;
		Dock = DockStyle.Fill;
		ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

		heading.Font = new Font(Font, FontStyle.Bold);
		insert.Click += (_, _) => InsertRequested?.Invoke(this, EventArgs.Empty);
		insert.Margin = new Padding(0, 6, 0, 0);
		foreach (Control control in new Control[] { heading, inputs, tokenText, tokenError, insert })
		{
			RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(control, 0, Controls.Count);
		}
		ShowField(null, "", "", "", true);
	}

	public event EventHandler? InsertRequested;

	/// <summary>The token to insert, or null when no field is chosen or the inputs do not make a valid token.</summary>
	public string? Token { get; private set; }

	public Field? Field => shown;

	public string Prefix => prefix.Text;

	public string Postfix => postfix.Text;

	public string? Separator => separator?.Text;

	public bool AutoSpace
	{
		get => autoSpace;
		set
		{
			autoSpace = value;
			Recompute();
		}
	}

	public void ShowField(Field? chosen, string lastPrefix, string lastPostfix, string lastSeparator, bool spaceFields)
	{
		shown = chosen;
		autoSpace = spaceFields;
		SuspendLayout();
		inputs.SuspendLayout();
		inputs.Controls.Clear();
		inputs.RowStyles.Clear();
		inputs.RowCount = 0;
		pad = monthShow = scope = readOperator = dateFormat = custom = firstLetter = null;
		separator = null;
		yesNoText = readText = null;
		invert = null;
		readPercent = counterStart = counterStep = counterPad = null;
		dateSample = null;

		heading.Text = chosen is null ? "Pick a field on the left" : $"Insert {chosen.DisplayName}";
		if (chosen is not null)
		{
			prefix = AddText("Text before", lastPrefix, "Written only when the field has a value.");
			BuildKindInputs(chosen, lastSeparator);
			postfix = AddText("Text after", lastPostfix, "Written only when the field has a value.");
		}
		inputs.ResumeLayout();
		ResumeLayout();
		Recompute();
	}

	private void BuildKindInputs(Field f, string lastSeparator)
	{
		if (f == Fields.ReadPercentage)
		{
			readText = AddText("Text", "", "Written when the book is read at least this far. Leave empty to insert the percentage itself.");
			readOperator = AddCombo("Read percentage is", ["greater than", "equal to", "less than"], 0);
			readPercent = AddNumber("Percent", 90, 0, 100);
		}
		else if (f == Fields.Counter)
		{
			counterStart = AddNumber("Start at", 1, -1_000_000, 1_000_000);
			counterStep = AddNumber("Step", 1, -1_000_000, 1_000_000);
			counterPad = AddNumber("Pad to width (0 for none)", 0, 0, 50);
		}
		else if (f == Fields.Custom)
		{
			custom = AddCombo("Custom value name", customKeys, -1);
			custom.DropDownStyle = ComboBoxStyle.DropDown;
		}
		else if (f == Fields.FirstLetter)
		{
			firstLetter = AddCombo("Take the first letter of", [Fields.Series.DisplayName, Fields.Publisher.DisplayName, Fields.Imprint.DisplayName, Fields.AlternateSeries.DisplayName], 0);
		}
		else
		{
			switch (f.Kind)
			{
				case FieldKind.Int or FieldKind.Issue:
					pad = AddCombo("Pad with zeros", PadChoices(), 0);
					break;
				case FieldKind.Month:
					monthShow = AddCombo("Show as", ["Month name", "Month number"], 0);
					pad = AddCombo("Pad number", PadChoices(), 0);
					break;
				case FieldKind.Items:
					separator = AddText("Separator", lastSeparator.Length > 0 ? lastSeparator : ", ", "Goes between the values when a book has several.");
					scope = AddCombo("Choose values", ScopeChoices.Select(choice => choice.Text).ToArray(), 0);
					break;
				case FieldKind.YesNo or FieldKind.MangaYesNo:
					yesNoText = AddText("Text for Yes", f == Fields.Manga ? "Manga" : f.DisplayName, "Leave empty to insert nothing.");
					invert = AddCheck("Write the text for No instead");
					break;
				case FieldKind.Date:
					dateFormat = AddCombo("Date format", TokenBuilder.DateFormats, 0);
					dateFormat.DropDownStyle = ComboBoxStyle.DropDown;
					dateSample = AddHint("");
					break;
			}
		}
	}

	private static string[] PadChoices()
		=> ["No padding", "Match the series' last issue", .. Enumerable.Range(2, 9).Select(n => $"{n} digits")];

	private TextBox AddText(string label, string value, string? hint)
	{
		var box = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), MinimumSize = new Size(180, 0) };
		box.TextChanged += (_, _) => Recompute();
		AddRowTo(label, box);
		if (hint is not null)
		{
			AddHint(hint);
		}
		return box;
	}

	private ComboBox AddCombo(string label, IReadOnlyList<string> items, int selected)
	{
		var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 230, Margin = new Padding(0, 2, 0, 2) };
		combo.Items.AddRange([.. items]);
		if (selected >= 0)
		{
			combo.SelectedIndex = selected;
		}
		combo.SelectedIndexChanged += (_, _) => Recompute();
		combo.TextChanged += (_, _) => Recompute();
		AddRowTo(label, combo);
		return combo;
	}

	private NumericUpDown AddNumber(string label, int value, int min, int max)
	{
		var number = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 80, Margin = new Padding(0, 2, 0, 2) };
		number.ValueChanged += (_, _) => Recompute();
		AddRowTo(label, number);
		return number;
	}

	private CheckBox AddCheck(string label)
	{
		var check = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
		check.CheckedChanged += (_, _) => Recompute();
		AddRowTo("", check);
		return check;
	}

	private Label AddHint(string text)
	{
		Label hint = Ui.Hint(text);
		hint.MaximumSize = new Size(420, 0);
		hint.Margin = new Padding(0, 0, 0, 4);
		inputs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		inputs.Controls.Add(Ui.Label(""), 0, inputs.RowCount);
		inputs.Controls.Add(hint, 1, inputs.RowCount++);
		return hint;
	}

	private void AddRowTo(string label, Control control)
	{
		inputs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		inputs.Controls.Add(Ui.Label(label), 0, inputs.RowCount);
		inputs.Controls.Add(control, 1, inputs.RowCount++);
	}

	private void Recompute()
	{
		if (shown is null)
		{
			Token = null;
			tokenText.Text = "";
			tokenError.Text = "";
			insert.Enabled = false;
			return;
		}
		if (dateSample is not null && dateFormat is not null)
		{
			dateSample.Text = "Example: " + SampleDate(dateFormat.Text) + (dateFormat.Text.Length == 0 ? " (empty means yyyy-MM-dd)" : "");
		}
		string text = TokenBuilder.Build(shown, Inputs());
		tokenText.Text = text;
		TemplateError? problem = Template.Parse(text, TemplateTarget.FileName).Errors.Cast<TemplateError?>().FirstOrDefault();
		tokenError.Text = problem?.Message ?? "";
		Token = problem is null ? text : null;
		insert.Enabled = problem is null;
	}

	private static string SampleDate(string format)
	{
		try
		{
			return new DateTime(2026, 3, 7).ToString(format.Length == 0 ? "yyyy-MM-dd" : format, CultureInfo.InvariantCulture);
		}
		catch (FormatException)
		{
			return "(not a valid format)";
		}
	}

	private TokenInputs Inputs()
	{
		int padIndex = pad?.SelectedIndex ?? 0;
		return new TokenInputs
		{
			Prefix = prefix.Text,
			Postfix = postfix.Text,
			AutoSpace = autoSpace,
			Pad = padIndex <= 0 ? PadSpec.None : padIndex == 1 ? PadSpec.Auto : PadSpec.Fixed(padIndex),
			MonthAsNumber = monthShow?.SelectedIndex == 1,
			Separator = separator?.Text ?? ", ",
			Scope = ScopeChoices[Math.Max(0, scope?.SelectedIndex ?? 0)].Scope,
			YesNoText = yesNoText?.Text ?? "",
			InvertYesNo = invert?.Checked ?? false,
			ReadText = readText?.Text ?? "",
			ReadOperator = (readOperator?.SelectedIndex ?? 0) switch { 1 => '=', 2 => '<', _ => '>' },
			ReadPercent = (int)(readPercent?.Value ?? 90),
			CounterStart = (int)(counterStart?.Value ?? 1),
			CounterStep = (int)(counterStep?.Value ?? 1),
			CounterPad = (int)(counterPad?.Value ?? 0),
			DateFormat = dateFormat?.Text ?? "",
			CustomKey = custom?.Text ?? "",
			FirstLetterSource = firstLetter?.Text ?? Fields.Series.DisplayName,
		};
	}
}
