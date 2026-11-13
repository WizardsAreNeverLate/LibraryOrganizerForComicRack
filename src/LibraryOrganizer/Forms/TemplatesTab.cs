using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>The folder and file templates, a field picker, and the controls that build a token from a field.</summary>
internal sealed class TemplatesTab : ProfileEditor
{
	private static readonly Dictionary<FieldGroup, string> GroupNames = new()
	{
		[FieldGroup.Text] = "Text",
		[FieldGroup.Number] = "Numbers and dates",
		[FieldGroup.YesNo] = "Yes / No",
		[FieldGroup.Items] = "Several values",
		[FieldGroup.Calculated] = "Calculated",
	};

	private readonly TemplateEditor folder;
	private readonly TemplateEditor file;
	private readonly TextBox search = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 4) };
	private readonly TreeView picker = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, ShowRootLines = false, FullRowSelect = true, Margin = new Padding(0) };
	private readonly InsertPanel insertPanel;
	private readonly CheckBox autoSpace = new() { Text = "Space inserted fields automatically", AutoSize = true, Checked = true };
	private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 200 };

	private Func<ProfileSettings, string>? preview;
	private TemplateEditor lastFocused;
	private bool folderActive = true;
	private bool fileActive = true;
	private Dictionary<string, string> lastPrefix = [];
	private Dictionary<string, string> lastPostfix = [];
	private Dictionary<string, string> lastSeparator = [];

	public TemplatesTab(IReadOnlyList<string> customKeys)
	{
		Dock = DockStyle.Fill;
		Padding = new Padding(12);

		var separatorButton = Ui.Button("New folder level  \\", (_, _) => { });
		separatorButton.Margin = new Padding(0);
		folder = new TemplateEditor("Folder template", TemplateTarget.Folder, separatorButton);
		separatorButton.Click += (_, _) => InsertInto(folder, "\\");
		file = new TemplateEditor("File name template", TemplateTarget.FileName);
		lastFocused = folder;
		insertPanel = new InsertPanel(customKeys);

		var pickerBox = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
		pickerBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		pickerBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		pickerBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		pickerBox.Controls.Add(Ui.Label("Find a field"), 0, 0);
		pickerBox.Controls.Add(search, 0, 1);
		pickerBox.Controls.Add(picker, 0, 2);

		var insertColumn = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill };
		insertColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		insertColumn.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		var insertScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
		insertScroll.Controls.Add(insertPanel);
		insertPanel.Dock = DockStyle.Top;
		insertColumn.Controls.Add(insertScroll, 0, 0);
		insertColumn.Controls.Add(autoSpace, 0, 1);

		var lower = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill };
		lower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
		lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		lower.Controls.Add(pickerBox, 0, 0);
		lower.Controls.Add(insertColumn, 1, 0);

		var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.Controls.Add(folder, 0, 0);
		layout.Controls.Add(file, 0, 1);
		layout.Controls.Add(lower, 0, 2);
		Controls.Add(layout);

		BuildPicker();
		search.TextChanged += (_, _) => BuildPicker();
		picker.AfterSelect += (_, e) => ChooseField(e.Node?.Tag as Field);
		picker.NodeMouseDoubleClick += (_, e) =>
		{
			if (e.Node?.Tag is Field)
			{
				InsertToken();
			}
		};
		picker.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Enter && picker.SelectedNode?.Tag is Field)
			{
				e.SuppressKeyPress = true;
				InsertToken();
			}
		};
		insertPanel.InsertRequested += (_, _) => InsertToken();
		autoSpace.CheckedChanged += (_, _) =>
		{
			insertPanel.AutoSpace = autoSpace.Checked;
			RaiseEdited();
		};
		folder.Box.Enter += (_, _) => lastFocused = folder;
		file.Box.Enter += (_, _) => lastFocused = file;
		folder.ValueChanged += (_, _) => TemplateEdited();
		file.ValueChanged += (_, _) => TemplateEdited();
		previewTimer.Tick += (_, _) =>
		{
			previewTimer.Stop();
			RefreshPreviews();
		};
	}

	/// <summary>Supplies the profile as currently edited, for the previews. Set by the form.</summary>
	public Func<ProfileSettings>? CurrentProfile { get; set; }

	/// <summary>Renders a profile for the previews; null shows "(preview unavailable)".</summary>
	public Func<ProfileSettings, string>? Preview
	{
		get => preview;
		set
		{
			preview = value;
			SchedulePreview();
		}
	}

	public void SelectField(Field target)
	{
		search.Text = "";
		TreeNode? node = picker.Nodes.Cast<TreeNode>().SelectMany(group => group.Nodes.Cast<TreeNode>()).FirstOrDefault(n => n.Tag == target);
		if (node is not null)
		{
			picker.SelectedNode = node;
			node.EnsureVisible();
		}
	}

	public TemplateParse FolderParse => folder.Parse;

	public TemplateParse FileParse => file.Parse;

	public void SetUses(bool useFolder, bool useFileName)
	{
		folderActive = useFolder;
		fileActive = useFileName;
		folder.SetActive(useFolder);
		file.SetActive(useFileName);
		if (!lastFocused.Active)
		{
			lastFocused = useFolder ? folder : file;
		}
		SchedulePreview();
	}

	protected override void LoadProfile(ProfileSettings p)
	{
		folder.Value = p.FolderTemplate;
		file.Value = p.FileTemplate;
		autoSpace.Checked = p.AutoSpaceFields;
		insertPanel.AutoSpace = p.AutoSpaceFields;
		lastPrefix = p.LastPrefix.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		lastPostfix = p.LastPostfix.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		lastSeparator = p.LastSeparator.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		ChooseField(picker.SelectedNode?.Tag as Field);
		SetUses(p.UseFolder, p.UseFileName);
	}

	public override ProfileSettings Collect(ProfileSettings p) => p with
	{
		FolderTemplate = folder.Value,
		FileTemplate = file.Value,
		AutoSpaceFields = autoSpace.Checked,
		LastPrefix = lastPrefix,
		LastPostfix = lastPostfix,
		LastSeparator = lastSeparator,
	};

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			previewTimer.Dispose();
		}
		base.Dispose(disposing);
	}

	private void BuildPicker()
	{
		string needle = search.Text.Trim();
		Field? keep = picker.SelectedNode?.Tag as Field;
		picker.BeginUpdate();
		picker.Nodes.Clear();
		IEnumerable<Field> fields = Fields.All.Where(f => f.Uses.HasFlag(FieldUses.Template) && f.TemplateName is not null);
		TreeNode? reselect = null;
		foreach (IGrouping<FieldGroup, Field> group in fields.GroupBy(f => f.Group).OrderBy(g => g.Key))
		{
			Field[] matches = [.. group.Where(f => needle.Length == 0
				|| f.DisplayName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
				|| f.TemplateName!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)];
			if (matches.Length == 0)
			{
				continue;
			}
			var groupNode = new TreeNode(GroupNames[group.Key]) { ForeColor = System.Drawing.SystemColors.GrayText };
			foreach (Field f in matches)
			{
				var node = new TreeNode(f.DisplayName) { Tag = f };
				groupNode.Nodes.Add(node);
				if (f == keep)
				{
					reselect = node;
				}
			}
			picker.Nodes.Add(groupNode);
			groupNode.Expand();
		}
		picker.EndUpdate();
		if (reselect is not null)
		{
			picker.SelectedNode = reselect;
		}
		else if (picker.Nodes.Count > 0)
		{
			picker.SelectedNode = picker.Nodes[0].Nodes[0];
		}
	}

	private void ChooseField(Field? chosen)
	{
		if (chosen is null)
		{
			insertPanel.ShowField(null, "", "", "", autoSpace.Checked);
			return;
		}
		insertPanel.ShowField(chosen, Get(lastPrefix, chosen), Get(lastPostfix, chosen), Get(lastSeparator, chosen), autoSpace.Checked);
	}

	private static string Get(Dictionary<string, string> map, Field field) => map.TryGetValue(field.Key, out string? value) ? value : "";

	private void InsertToken()
	{
		if (insertPanel.Token is not { } token || insertPanel.Field is not { } field)
		{
			return;
		}
		lastPrefix[field.Key] = insertPanel.Prefix;
		lastPostfix[field.Key] = insertPanel.Postfix;
		if (insertPanel.Separator is { } separator)
		{
			lastSeparator[field.Key] = separator;
		}
		InsertInto(lastFocused.Active ? lastFocused : folderActive ? folder : file, token);
	}

	private void InsertInto(TemplateEditor target, string text)
	{
		if (!target.Active)
		{
			return;
		}
		lastFocused = target;
		target.InsertAtCaret(text);
	}

	private void TemplateEdited()
	{
		SchedulePreview();
		RaiseEdited();
	}

	private void SchedulePreview()
	{
		previewTimer.Stop();
		previewTimer.Start();
	}

	private void RefreshPreviews()
	{
		if (IsDisposed)
		{
			return;
		}
		folder.SetPreview(folderActive ? PreviewOf(folder.Parse, true) : "");
		file.SetPreview(fileActive ? PreviewOf(file.Parse, false) : "");
	}

	private string PreviewOf(TemplateParse parse, bool folderPart)
	{
		if (!parse.Ok)
		{
			return "";
		}
		if (Preview is not { } render || CurrentProfile is not { } current)
		{
			return "Preview: (preview unavailable)";
		}
		try
		{
			ProfileSettings profile = current() with { UseFolder = folderPart, UseFileName = !folderPart };
			string text = render(profile);
			return "Preview: " + (text.Length == 0 ? "(nothing)" : text);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			return "Preview: (failed: " + ex.Message + ")";
		}
	}
}
