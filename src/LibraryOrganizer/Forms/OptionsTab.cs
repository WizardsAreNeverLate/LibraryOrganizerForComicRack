using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

internal sealed class OptionsTab : ProfileEditor
{
	private static readonly Field[] RequiredFields = [.. Fields.All.Where(f => f.Uses.HasFlag(FieldUses.Required))];

	private readonly CheckBox collapseSpaces = new() { Text = "Replace runs of spaces with a single space", AutoSize = true };
	private readonly CheckBox dontAskOneValue = new() { Text = "Use a several-value field's only value without asking", AutoSize = true };
	private readonly TextBox emptyFolder = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
	private readonly DataGridView substitutes = Grid();
	private readonly CheckedListBox required = new() { CheckOnClick = true, IntegralHeight = false, Height = 170 };
	private readonly TextBox[] months = [.. Enumerable.Range(0, 16).Select(_ => new TextBox { Dock = DockStyle.Fill, MinimumSize = new Size(70, 0), Margin = new Padding(0, 1, 8, 1) })];
	private readonly DataGridView illegal = Grid();
	private readonly CheckBox prune = new() { Text = "Remove folders left empty after organizing", AutoSize = true };
	private readonly ListBox keep = new() { IntegralHeight = false, Height = 64, Dock = DockStyle.Fill, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };

	private readonly Dictionary<string, Field> substituteFields = RequiredFields.ToDictionary(f => f.DisplayName, StringComparer.Ordinal);
	private List<string> unknownRequired = [];

	public OptionsTab()
	{
		Dock = DockStyle.Fill;
		AutoScroll = true;
		Padding = new Padding(12);

		var fieldColumn = new DataGridViewComboBoxColumn { HeaderText = "Field", FlatStyle = FlatStyle.Flat, FillWeight = 40, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox };
		fieldColumn.Items.AddRange([.. RequiredFields.Select(f => (object)f.DisplayName)]);
		substitutes.Columns.Add(fieldColumn);
		substitutes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Use this text", FillWeight = 60 });
		illegal.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Character", FillWeight = 30, MaxInputLength = 1 });
		illegal.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Replace with", FillWeight = 70 });

		var cleaning = Ui.Column();
		cleaning.AddRow(collapseSpaces);
		cleaning.AddRow(dontAskOneValue);
		var emptyRow = new TableLayoutPanel { ColumnCount = 2, Height = 28 };
		emptyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		emptyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		emptyRow.Controls.Add(Ui.Label("Name for a folder level that comes out empty"), 0, 0);
		emptyRow.Controls.Add(emptyFolder, 1, 0);
		cleaning.AddRow(emptyRow);
		cleaning.AddRow(Ui.Hint("Leave it empty to drop the level."));

		substitutes.Height = 130;
		var emptyValues = Ui.Column();
		emptyValues.AddRow(Ui.Hint("When a field has no value, write this text in its place."));
		emptyValues.AddRow(substitutes);
		emptyValues.AddRow(Ui.Row(Ui.Button("Remove selected", (_, _) => RemoveSelected(substitutes))));

		var requiredBody = Ui.Column();
		requiredBody.AddRow(Ui.Hint("A book with any of these fields empty is not organized. It goes to the failed books folder, or is skipped."));
		requiredBody.AddRow(required);

		var monthGrid = new TableLayoutPanel { ColumnCount = 6, AutoSize = true };
		for (int pair = 0; pair < 3; pair++)
		{
			monthGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			monthGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
		}
		for (int i = 0; i < 16; i++)
		{
			int column = (i / 6) * 2;
			monthGrid.Controls.Add(Ui.Label((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), column, i % 6);
			monthGrid.Controls.Add(months[i], column + 1, i % 6);
			months[i].TextChanged += (_, _) => RaiseEdited();
		}
		var monthBody = Ui.Column();
		monthBody.AddRow(Ui.Hint("Months 13 to 16 are the seasons: spring, summer, fall, winter."));
		monthBody.AddRow(monthGrid);

		illegal.Height = 100;
		illegal.Margin = new Padding(0, 0, 0, 6);
		substitutes.Margin = new Padding(0, 0, 0, 6);
		required.Margin = new Padding(0, 0, 0, 6);
		var illegalBody = Ui.Column();
		illegalBody.AddRow(Ui.Hint("Replaced in every folder and file name. Characters Windows forbids are always removed, listed or not."));
		illegalBody.AddRow(illegal);
		illegalBody.AddRow(Ui.Row(
			Ui.Button("Remove selected", (_, _) => RemoveSelected(illegal)),
			Ui.Button("Restore defaults", (_, _) => RestoreIllegalDefaults())));

		var pruneBody = Ui.Column();
		pruneBody.AddRow(prune);
		pruneBody.AddRow(Ui.Hint("Never remove these folders, even when empty:"));
		pruneBody.AddRow(keep);
		pruneBody.AddRow(Ui.Row(Ui.Button("Add folder...", (_, _) => AddKeepFolder()), Ui.Button("Remove", (_, _) => RemoveKeepFolders())));

		var left = Ui.Column();
		left.AddRow(Ui.Section("Names", cleaning));
		left.AddRow(Ui.Section("Empty values", emptyValues));
		left.AddRow(Ui.Section("Required fields", requiredBody));
		var right = Ui.Column();
		right.AddRow(Ui.Section("Month names", monthBody));
		right.AddRow(Ui.Section("Illegal characters", illegalBody));
		right.AddRow(Ui.Section("Empty folders", pruneBody));

		var columns = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true };
		columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		left.Margin = new Padding(0, 0, 8, 0);
		columns.Controls.Add(left, 0, 0);
		columns.Controls.Add(right, 1, 0);
		Controls.Add(columns);

		collapseSpaces.CheckedChanged += (_, _) => RaiseEdited();
		dontAskOneValue.CheckedChanged += (_, _) => RaiseEdited();
		emptyFolder.TextChanged += (_, _) => RaiseEdited();
		prune.CheckedChanged += (_, _) => RaiseEdited();
		required.ItemCheck += (_, _) =>
		{
			if (!IsLoading && IsHandleCreated)
			{
				BeginInvoke(new Action(RaiseEdited));
			}
		};
		substitutes.CellValueChanged += (_, _) => RaiseEdited();
		substitutes.RowsRemoved += (_, _) => RaiseEdited();
		illegal.CellValueChanged += (_, _) => RaiseEdited();
		illegal.RowsRemoved += (_, _) => RaiseEdited();
	}

	protected override void LoadProfile(ProfileSettings p)
	{
		collapseSpaces.Checked = p.CollapseSpaces;
		dontAskOneValue.Checked = p.DontAskWhenOneValue;
		emptyFolder.Text = p.EmptyFolderName;
		prune.Checked = p.PruneEmptyFolders;

		substitutes.Rows.Clear();
		var column = (DataGridViewComboBoxColumn)substitutes.Columns[0];
		foreach (KeyValuePair<string, string> pair in p.EmptySubstitutes)
		{
			string name = Fields.ByKey(pair.Key)?.DisplayName ?? pair.Key;
			if (!column.Items.Contains(name))
			{
				column.Items.Add(name);
			}
			substitutes.Rows.Add(name, pair.Value);
		}

		required.Items.Clear();
		foreach (Field f in RequiredFields)
		{
			required.Items.Add(f.DisplayName, p.RequiredFields.Contains(f.Key));
		}
		unknownRequired = [.. p.RequiredFields.Where(key => RequiredFields.All(f => f.Key != key))];

		for (int i = 0; i < 16; i++)
		{
			months[i].Text = i < p.MonthNames.Count ? p.MonthNames[i] : "";
		}
		illegal.Rows.Clear();
		foreach (KeyValuePair<string, string> pair in p.IllegalCharacters)
		{
			illegal.Rows.Add(pair.Key, pair.Value);
		}
		keep.Items.Clear();
		keep.Items.AddRange([.. p.KeepFolders]);
	}

	public override ProfileSettings Collect(ProfileSettings p)
	{
		var substituteMap = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (DataGridViewRow row in substitutes.Rows)
		{
			if (row.IsNewRow || row.Cells[0].Value as string is not { Length: > 0 } name || row.Cells[1].Value as string is not { Length: > 0 } text)
			{
				continue;
			}
			string key = substituteFields.TryGetValue(name, out Field? field) ? field.Key : name;
			substituteMap[key] = text;
		}

		var illegalList = new List<KeyValuePair<string, string>>();
		foreach (DataGridViewRow row in illegal.Rows)
		{
			string character = row.Cells[0].Value as string ?? "";
			string replacement = row.Cells[1].Value as string ?? "";
			if (!row.IsNewRow && (character.Length > 0 || replacement.Length > 0))
			{
				illegalList.Add(new KeyValuePair<string, string>(character, replacement));
			}
		}

		return p with
		{
			CollapseSpaces = collapseSpaces.Checked,
			DontAskWhenOneValue = dontAskOneValue.Checked,
			EmptyFolderName = emptyFolder.Text,
			EmptySubstitutes = substituteMap,
			RequiredFields = [.. RequiredFields.Where((_, i) => required.GetItemChecked(i)).Select(f => f.Key), .. unknownRequired],
			MonthNames = [.. months.Select(box => box.Text)],
			IllegalCharacters = illegalList,
			PruneEmptyFolders = prune.Checked,
			KeepFolders = [.. keep.Items.Cast<string>()],
		};
	}

	private static DataGridView Grid() => new()
	{
		Dock = DockStyle.Top,
		AllowUserToResizeRows = false,
		RowHeadersVisible = false,
		AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
		SelectionMode = DataGridViewSelectionMode.FullRowSelect,
		BackgroundColor = SystemColors.Window,
		BorderStyle = BorderStyle.FixedSingle,
		EditMode = DataGridViewEditMode.EditOnEnter,
		MultiSelect = true,
	};

	private static void RemoveSelected(DataGridView grid)
	{
		foreach (DataGridViewRow row in grid.SelectedRows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).ToList())
		{
			grid.Rows.Remove(row);
		}
	}

	private void RestoreIllegalDefaults()
	{
		illegal.Rows.Clear();
		foreach (KeyValuePair<string, string> pair in PathDefaults.IllegalCharacters)
		{
			illegal.Rows.Add(pair.Key, pair.Value);
		}
		RaiseEdited();
	}

	private void AddKeepFolder()
	{
		if (Ui.PickFolder(this, "", "Choose a folder to keep even when it is empty.") is { } folder && !keep.Items.Cast<string>().Contains(folder, StringComparer.OrdinalIgnoreCase))
		{
			keep.Items.Add(folder);
			RaiseEdited();
		}
	}

	private void RemoveKeepFolders()
	{
		foreach (string folder in keep.SelectedItems.Cast<string>().ToList())
		{
			keep.Items.Remove(folder);
		}
		RaiseEdited();
	}
}
