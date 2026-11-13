using System;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>Action, destination and what to do with books that cannot be organized.</summary>
internal sealed class GeneralTab : ProfileEditor
{
	private static readonly (string Text, CoverFormat? Format)[] CoverChoices =
	[
		("Skip them", null),
		("Save their custom cover as a .jpg", CoverFormat.Jpg),
		("Save their custom cover as a .png", CoverFormat.Png),
		("Save their custom cover as a .bmp", CoverFormat.Bmp),
	];

	private readonly RadioButton move = new() { Text = "Move the file", AutoSize = true };
	private readonly RadioButton copy = new() { Text = "Copy the file", AutoSize = true };
	private readonly CheckBox addToLibrary = new() { Text = "Add the copy to the ComicRack library", AutoSize = true, Margin = new Padding(24, 0, 0, 0) };
	private readonly CheckBox useFolder = new() { Text = "Organize into folders using the folder template", AutoSize = true };
	private readonly FolderBox baseFolder = new("Choose the base folder. Organized books go into folders below it.");
	private readonly Label baseFolderLabel = Ui.Label("Base folder");
	private readonly CheckBox useFileName = new() { Text = "Rename files using the file template", AutoSize = true };
	private readonly FolderBox failedFolder = new("Choose the folder for books that cannot be organized.");
	private readonly ComboBox filelessCovers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
	private readonly Label filelessLabel = Ui.Label("Books without a file");
	private readonly CheckBox keepProgress = new() { Text = "When a file replaces an existing one, keep the read progress", AutoSize = true };

	public GeneralTab()
	{
		Dock = DockStyle.Fill;
		AutoScroll = true;
		Padding = new Padding(12);

		var actionBody = Ui.Column();
		actionBody.AddRow(Ui.Row(move, copy));
		actionBody.AddRow(addToLibrary);
		actionBody.AddRow(Ui.Hint("Several Move profiles can accept the same book: the last one in the run order wins. Every accepting Copy profile makes its own copy."));

		var whereBody = Ui.Column();
		whereBody.AddRow(useFolder);
		var baseRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(24, 0, 0, 0) };
		baseRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		baseRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		baseRow.Controls.Add(baseFolderLabel, 0, 0);
		baseFolder.Dock = DockStyle.Fill;
		baseRow.Controls.Add(baseFolder, 1, 0);
		whereBody.AddRow(baseRow);
		whereBody.AddRow(useFileName);

		var failBody = Ui.Column();
		failBody.AddRow(Ui.Label("A book with an empty required field goes to this folder. Leave it empty to skip such books."));
		failBody.AddRow(failedFolder);
		failBody.AddRow(Ui.Row(filelessLabel, filelessCovers));
		failBody.AddRow(Ui.Hint("Choose the required fields on the Options tab."));

		var column = Ui.Column();
		column.AddRow(Ui.Section("Action", actionBody));
		column.AddRow(Ui.Section("Destination", whereBody));
		column.AddRow(Ui.Section("Books that cannot be organized", failBody));
		column.AddRow(Ui.Section("Replacing files", Wrap(keepProgress)));
		Controls.Add(column);

		foreach ((string text, _) in CoverChoices)
		{
			filelessCovers.Items.Add(text);
		}
		move.CheckedChanged += (_, _) => Changed();
		useFolder.CheckedChanged += (_, _) => Changed();
		copy.CheckedChanged += (_, _) => Changed();
		addToLibrary.CheckedChanged += (_, _) => Changed();
		useFileName.CheckedChanged += (_, _) => Changed();
		keepProgress.CheckedChanged += (_, _) => Changed();
		filelessCovers.SelectedIndexChanged += (_, _) => Changed();
		baseFolder.Changed += (_, _) => Changed();
		failedFolder.Changed += (_, _) => Changed();
	}

	public bool UseFolder => useFolder.Checked;

	public bool UseFileName => useFileName.Checked;

	protected override void LoadProfile(ProfileSettings p)
	{
		move.Checked = p.Action is ProfileAction.Move;
		copy.Checked = p.Action is ProfileAction.Copy;
		addToLibrary.Checked = p.Action is ProfileAction.Copy { AddToLibrary: true };
		useFolder.Checked = p.UseFolder;
		useFileName.Checked = p.UseFileName;
		baseFolder.Value = p.BaseFolder;
		failedFolder.Value = p.FailedBooksFolder;
		filelessCovers.SelectedIndex = Math.Max(0, Array.FindIndex(CoverChoices, choice => choice.Format == p.FilelessCovers));
		keepProgress.Checked = p.KeepReadProgressOnReplace;
		UpdateEnabled();
	}

	public override ProfileSettings Collect(ProfileSettings p) => p with
	{
		Action = copy.Checked ? new ProfileAction.Copy(addToLibrary.Checked) : new ProfileAction.Move(),
		UseFolder = useFolder.Checked,
		UseFileName = useFileName.Checked,
		BaseFolder = baseFolder.Value.Trim(),
		FailedBooksFolder = failedFolder.Value.Trim(),
		FilelessCovers = CoverChoices[Math.Max(0, filelessCovers.SelectedIndex)].Format,
		KeepReadProgressOnReplace = keepProgress.Checked,
	};

	private static Control Wrap(Control control)
	{
		var column = Ui.Column();
		column.AddRow(control);
		return column;
	}

	private void Changed()
	{
		UpdateEnabled();
		RaiseEdited();
	}

	private void UpdateEnabled()
	{
		addToLibrary.Enabled = copy.Checked;
		baseFolder.Enabled = useFolder.Checked;
		baseFolderLabel.Enabled = useFolder.Checked;
		filelessCovers.Enabled = useFolder.Checked;
		filelessLabel.Enabled = useFolder.Checked;
	}
}
