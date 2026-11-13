using System;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>Which books a profile organizes: a rule tree and a list of excluded folders.</summary>
internal sealed class RulesTab : ProfileEditor
{
	private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(0, 1, 6, 1) };
	private readonly ComboBox match = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, Margin = new Padding(0, 1, 6, 1) };
	private readonly GroupNode root = new(Combinator.Any, [], isRoot: true);
	private readonly Label empty = Ui.Hint("");
	private readonly ListBox excluded = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };

	public RulesTab()
	{
		Dock = DockStyle.Fill;
		Padding = new Padding(12);
		mode.Items.AddRange(["Only", "Do not"]);
		match.Items.AddRange(["Any", "All"]);

		var sentence = Ui.Row(mode, Ui.Label("organize books that match"), match, Ui.Label("of these rules"));
		var buttons = Ui.Row(Ui.Button("Add rule", (_, _) => root.AddCondition()), Ui.Button("Add group", (_, _) => root.AddGroup()));
		buttons.Margin = new Padding(0, 4, 0, 4);

		var treeHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
		root.Dock = DockStyle.Top;
		empty.Dock = DockStyle.Top;
		empty.Margin = new Padding(6);
		treeHost.Controls.Add(root);
		treeHost.Controls.Add(empty);

		var folderButtons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
		folderButtons.Controls.Add(Ui.Button("Add folder...", (_, _) => AddFolder()));
		folderButtons.Controls.Add(Ui.Button("Remove", (_, _) => RemoveFolders()));
		foreach (Control button in folderButtons.Controls)
		{
			button.Margin = new Padding(6, 0, 0, 4);
		}
		var folderRow = new Panel { Dock = DockStyle.Fill };
		folderRow.Controls.Add(excluded);
		folderRow.Controls.Add(folderButtons);
		var folderBox = new GroupBox { Text = "Never organize books in these folders (and their subfolders)", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8) };
		folderBox.Controls.Add(folderRow);

		var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
		layout.Controls.Add(sentence, 0, 0);
		layout.Controls.Add(buttons, 0, 1);
		layout.Controls.Add(treeHost, 0, 2);
		layout.Controls.Add(folderBox, 0, 3);
		Controls.Add(layout);

		mode.SelectedIndexChanged += (_, _) => Changed();
		match.SelectedIndexChanged += (_, _) => Changed();
		root.Edited += (_, _) => Changed();
	}

	protected override void LoadProfile(ProfileSettings p)
	{
		mode.SelectedIndex = p.RuleMode == RuleMode.Only ? 0 : 1;
		match.SelectedIndex = p.Rules.Combinator == Combinator.All ? 1 : 0;
		root.SetCombinator(p.Rules.Combinator);
		root.Fill(p.Rules.Children);
		excluded.Items.Clear();
		excluded.Items.AddRange([.. p.ExcludedFolders]);
		UpdateHint();
	}

	public override ProfileSettings Collect(ProfileSettings p) => p with
	{
		RuleMode = mode.SelectedIndex == 0 ? RuleMode.Only : RuleMode.Except,
		Rules = root.ToGroup(),
		ExcludedFolders = [.. excluded.Items.Cast<string>()],
	};

	private void Changed()
	{
		root.SetCombinator(match.SelectedIndex == 1 ? Combinator.All : Combinator.Any);
		UpdateHint();
		RaiseEdited();
	}

	private void UpdateHint()
	{
		empty.Visible = root.Count == 0;
		empty.Text = "No rules yet, so every book is organized. Add a rule to organize only some books, or to leave some out.";
	}

	private void AddFolder()
	{
		if (Ui.PickFolder(this, "", "Choose a folder to leave alone.") is { } folder && !excluded.Items.Cast<string>().Contains(folder, StringComparer.OrdinalIgnoreCase))
		{
			excluded.Items.Add(folder);
			RaiseEdited();
		}
	}

	private void RemoveFolders()
	{
		foreach (string folder in excluded.SelectedItems.Cast<string>().ToList())
		{
			excluded.Items.Remove(folder);
		}
		RaiseEdited();
	}
}
