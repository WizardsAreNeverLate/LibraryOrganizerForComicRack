using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>
/// The profile configuration dialog. It edits a working copy of <see cref="Settings"/>: OK exposes the result in
/// <see cref="Edited"/>, Cancel discards every change. Left is the profile list and its commands; right are four tabs.
/// </summary>
public sealed class ConfigForm : Form
{
	private const string ProfileFileFilter = "Library Organizer profiles (*.xml;*.lop;*.dat)|*.xml;*.lop;*.dat|All files (*.*)|*.*";

	private readonly Settings original;
	private readonly List<ProfileSettings> profiles;
	private readonly ListBox profileList = new() { Dock = DockStyle.Fill, IntegralHeight = false, Margin = new Padding(0, 0, 0, 8) };
	private readonly Button duplicate;
	private readonly Button delete;
	private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
	private readonly GeneralTab general = new();
	private readonly TemplatesTab templates;
	private readonly RulesTab rules = new();
	private readonly OptionsTab options = new();
	private readonly ListBox problemList = new() { Dock = DockStyle.Top, Height = 60, IntegralHeight = false, FormattingEnabled = true };
	private readonly Label problemTitle = new() { AutoSize = true, ForeColor = Ui.ErrorColor, Margin = new Padding(0, 0, 0, 2) };
	private readonly TableLayoutPanel problemPanel = new() { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Visible = false, Margin = new Padding(0, 6, 0, 0) };
	private int current;
	private bool switching;
	private bool dirty;

	public ConfigForm(Settings settings, ProfileId? select = null, IReadOnlyList<string>? customKeys = null)
	{
		original = settings;
		profiles = [.. settings.Profiles];
		if (profiles.Count == 0)
		{
			profiles.Add(new ProfileSettings { Name = "Default" });
		}
		templates = new TemplatesTab(customKeys ?? []);

		Text = "Library Organizer settings";
		Font = SystemFonts.MessageBoxFont;
		AutoScaleMode = AutoScaleMode.Font;
		FormBorderStyle = FormBorderStyle.Sizable;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(1040, 720);
		MinimumSize = new Size(900, 620);

		duplicate = Ui.Button("Duplicate...", (_, _) => DuplicateProfile());
		delete = Ui.Button("Delete", (_, _) => DeleteProfile());
		Control left = BuildProfilePanel();

		foreach ((string title, ProfileEditor editor) in new (string, ProfileEditor)[] { ("General", general), ("Templates", templates), ("Rules", rules), ("Options", options) })
		{
			var page = new TabPage(title) { UseVisualStyleBackColor = true };
			page.Controls.Add(editor);
			tabs.TabPages.Add(page);
			editor.Edited += (_, _) => OnEdited();
		}
		general.Edited += (_, _) => templates.SetUses(general.UseFolder, general.UseFileName);
		templates.CurrentProfile = CollectCurrent;

		problemList.DoubleClick += (_, _) => JumpToSelected();
		problemList.Format += (_, e) => e.Value = e.ListItem is ProfileProblem p ? Describe(p) : e.Value;
		problemPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		problemPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		problemPanel.Controls.Add(problemTitle, 0, 0);
		problemPanel.Controls.Add(problemList, 0, 1);

		var ok = new Button { Text = "OK", Width = 90, UseVisualStyleBackColor = true };
		var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
		ok.Click += (_, _) => Accept();
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
		buttons.Controls.AddRange([cancel, ok]);
		CancelButton = cancel;

		var right = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Margin = new Padding(12, 0, 0, 0) };
		right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		right.Controls.Add(tabs, 0, 0);
		right.Controls.Add(problemPanel, 0, 1);

		var main = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Padding = new Padding(12) };
		main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
		main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.Controls.Add(left, 0, 0);
		main.Controls.Add(right, 1, 0);
		main.Controls.Add(buttons, 0, 1);
		main.SetColumnSpan(buttons, 2);
		Controls.Add(main);

		profileList.SelectedIndexChanged += (_, _) => OnProfileSelected();
		current = Math.Max(0, select is { } id ? profiles.FindIndex(p => p.Id == id) : 0);
		RefreshList();
		LoadCurrent();
	}

	/// <summary>The settings as edited, set when the dialog closes with OK.</summary>
	public Settings? Edited { get; private set; }

	/// <summary>Renders a profile to a sample path for the template previews. Null shows "(preview unavailable)".</summary>
	public Func<ProfileSettings, string>? Preview
	{
		get => templates.Preview;
		set => templates.Preview = value;
	}

	/// <summary>Applies the host's theme to the form once, before it is first shown.</summary>
	public Action<Form>? Theme { get; set; }

	public void ShowTab(ConfigTab tab) => tabs.SelectedIndex = (int)tab;

	/// <summary>Opens the Templates tab with this field chosen in the picker.</summary>
	public void ShowField(Field field)
	{
		ShowTab(ConfigTab.Templates);
		templates.SelectField(field);
	}

	public void SelectProfile(int index)
	{
		CollectCurrent();
		current = Math.Max(0, Math.Min(index, profiles.Count - 1));
		RefreshList();
		LoadCurrent();
	}

	public static Settings? Show(IWin32Window? owner, Settings settings, Func<ProfileSettings, string>? preview = null, IReadOnlyList<string>? customKeys = null, Action<Form>? theme = null)
	{
		using var form = new ConfigForm(settings, null, customKeys) { Preview = preview, Theme = theme };
		return (owner is null ? form.ShowDialog() : form.ShowDialog(owner)) == DialogResult.OK ? form.Edited : null;
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		Theme?.Invoke(this);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		base.OnFormClosing(e);
		if (DialogResult != DialogResult.OK && !e.Cancel && dirty
			&& MessageBox.Show(this, "Discard your changes to the settings?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			e.Cancel = true;
			DialogResult = DialogResult.None;
		}
	}

	private Control BuildProfilePanel()
	{
		var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		panel.Controls.Add(Ui.Label("Profiles"), 0, 0);
		panel.Controls.Add(profileList, 0, 1);

		Button[] commands =
		[
			Ui.Button("New...", (_, _) => NewProfile()),
			duplicate,
			Ui.Button("Rename...", (_, _) => RenameProfile()),
			delete,
			Ui.Button("Import...", (_, _) => ImportProfiles()),
			Ui.Button("Export selected...", (_, _) => Export(all: false)),
			Ui.Button("Export all...", (_, _) => Export(all: true)),
		];
		foreach (Button button in commands)
		{
			button.Dock = DockStyle.Top;
			button.AutoSize = false;
			button.Height = 28;
			button.Margin = new Padding(0, 0, 0, 4);
			panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			panel.Controls.Add(button, 0, panel.RowCount++);
		}
		commands[4].Margin = new Padding(0, 10, 0, 4);
		return panel;
	}

	private ProfileSettings CollectCurrent()
	{
		ProfileSettings profile = profiles[current];
		profile = general.Collect(profile);
		profile = templates.Collect(profile);
		profile = rules.Collect(profile);
		profile = options.Collect(profile);
		profiles[current] = profile;
		return profile;
	}

	private void LoadCurrent()
	{
		ProfileSettings profile = profiles[current];
		general.Display(profile);
		templates.Display(profile);
		rules.Display(profile);
		options.Display(profile);
	}

	private void RefreshList()
	{
		switching = true;
		try
		{
			profileList.Items.Clear();
			profileList.Items.AddRange([.. profiles.Select(p => (object)p.Name)]);
			profileList.SelectedIndex = current;
			delete.Enabled = profiles.Count > 1;
		}
		finally
		{
			switching = false;
		}
	}

	private void OnProfileSelected()
	{
		int next = profileList.SelectedIndex;
		if (switching || next < 0 || next == current)
		{
			return;
		}
		if (!LeaveCurrent())
		{
			switching = true;
			profileList.SelectedIndex = current;
			switching = false;
			return;
		}
		current = next;
		LoadCurrent();
	}

	/// <summary>Collects the current profile and checks it. When it has problems they are shown and the user stays.</summary>
	private bool LeaveCurrent()
	{
		CollectCurrent();
		IReadOnlyList<ProfileProblem> problems = ProfileValidator.Validate(profiles[current]);
		if (problems.Count > 0)
		{
			ShowProblems(problems, "Fix these before switching to another profile. Double-click a line to go there.");
			return false;
		}
		HideProblems();
		return true;
	}

	private void OnEdited()
	{
		dirty = true;
		if (!problemPanel.Visible)
		{
			return;
		}
		CollectCurrent();
		IReadOnlyList<ProfileProblem> remaining = ProfileValidator.ValidateAll(profiles);
		if (remaining.Count == 0)
		{
			HideProblems();
		}
		else
		{
			ShowProblems(remaining, problemTitle.Text);
		}
	}

	private void ShowProblems(IReadOnlyList<ProfileProblem> problems, string title)
	{
		problemTitle.Text = title;
		problemList.BeginUpdate();
		problemList.Items.Clear();
		problemList.Items.AddRange([.. problems]);
		problemList.EndUpdate();
		problemList.Height = Math.Min(130, 8 + problems.Count * problemList.ItemHeight);
		problemPanel.Visible = true;
		if (problemList.Items.Count > 0 && problemList.SelectedIndex < 0)
		{
			problemList.SelectedIndex = 0;
		}
	}

	private void HideProblems() => problemPanel.Visible = false;

	private string Describe(ProfileProblem problem)
		=> (profiles.Count > 1 ? problem.ProfileName + " / " : "") + problem.Tab + ": " + problem.Message;

	private void JumpToSelected()
	{
		if (problemList.SelectedItem is ProfileProblem problem)
		{
			JumpTo(problem);
		}
	}

	/// <summary>Checks every profile. On problems it lists them all, jumps to the first and returns false.</summary>
	public bool CheckAll()
	{
		CollectCurrent();
		IReadOnlyList<ProfileProblem> problems = ProfileValidator.ValidateAll(profiles);
		if (problems.Count == 0)
		{
			return true;
		}
		ShowProblems(problems, "Fix these before saving. Double-click a line to go there.");
		JumpTo(problems[0]);
		return false;
	}

	private void Accept()
	{
		if (!CheckAll())
		{
			return;
		}
		var kept = profiles.Select(p => p.Id).ToHashSet();
		Edited = new Settings([.. profiles], [.. original.LastRunOrder.Where(kept.Contains)], original.Preferences);
		DialogResult = DialogResult.OK;
	}

	private void JumpTo(ProfileProblem problem)
	{
		int index = profiles.FindIndex(p => p.Id == problem.Profile);
		if (index >= 0 && index != current)
		{
			SelectProfile(index);
		}
		ShowTab(problem.Tab);
	}

	private string? NameProblem(string name, int exceptIndex)
	{
		if (name.Length == 0)
		{
			return "Enter a name.";
		}
		return profiles.Where((_, i) => i != exceptIndex).Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? "A profile with this name exists." : null;
	}

	private string? AskName(string title, string prompt, string initial, int exceptIndex)
	{
		using var dialog = new NameDialog(title, prompt, initial, name => NameProblem(name, exceptIndex));
		return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Value : null;
	}

	private void AddAndSelect(ProfileSettings profile)
	{
		dirty = true;
		profiles.Add(profile);
		current = profiles.Count - 1;
		RefreshList();
		LoadCurrent();
		HideProblems();
	}

	private void NewProfile()
	{
		if (!LeaveCurrent())
		{
			return;
		}
		if (AskName("New profile", "Name for the new profile", "New profile", -1) is { } name)
		{
			AddAndSelect(new ProfileSettings { Name = name });
		}
	}

	private void DuplicateProfile()
	{
		if (!LeaveCurrent())
		{
			return;
		}
		ProfileSettings source = profiles[current];
		if (AskName("Duplicate profile", "Name for the copy", Unique(source.Name + " (copy)", profiles.Select(p => p.Name)), -1) is { } name)
		{
			AddAndSelect(source with { Id = new ProfileId(Guid.NewGuid()), Name = name });
		}
	}

	private void RenameProfile()
	{
		CollectCurrent();
		if (AskName("Rename profile", "New name", profiles[current].Name, current) is { } name)
		{
			dirty = true;
			profiles[current] = profiles[current] with { Name = name };
			RefreshList();
		}
	}

	private void DeleteProfile()
	{
		if (profiles.Count <= 1)
		{
			return;
		}
		string name = profiles[current].Name;
		if (MessageBox.Show(this, $"Delete the profile \"{name}\"?", "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
		{
			return;
		}
		dirty = true;
		profiles.RemoveAt(current);
		current = Math.Min(current, profiles.Count - 1);
		RefreshList();
		LoadCurrent();
		HideProblems();
	}

	private void ImportProfiles()
	{
		using var dialog = new OpenFileDialog { Title = "Import profiles", Filter = ProfileFileFilter, CheckFileExists = true };
		if (dialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		ImportResult result = SettingsStore.ImportFile(dialog.FileName);
		if (result.Profiles.Count == 0)
		{
			string why = result.Warnings.Count > 0 ? string.Join(Environment.NewLine, result.Warnings) : "The file holds no profiles.";
			MessageBox.Show(this, why, "Import profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		CollectCurrent();
		var taken = profiles.Select(p => p.Name).ToList();
		var renamed = new List<string>();
		int firstImported = profiles.Count;
		foreach (ProfileSettings imported in result.Profiles)
		{
			string name = Unique(imported.Name, taken);
			if (name != imported.Name)
			{
				renamed.Add($"\"{imported.Name}\" was added as \"{name}\".");
			}
			taken.Add(name);
			profiles.Add(imported with { Name = name });
		}
		dirty = true;
		current = firstImported;
		RefreshList();
		LoadCurrent();

		var notes = renamed.Concat(result.Warnings).ToList();
		string summary = $"Imported {result.Profiles.Count} profile{(result.Profiles.Count == 1 ? "" : "s")}.";
		MessageBox.Show(this, notes.Count == 0 ? summary : summary + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, notes), "Import profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
	}

	private void Export(bool all)
	{
		CollectCurrent();
		string suggested = all ? "Library Organizer profiles" : profiles[current].Name;
		foreach (char bad in Path.GetInvalidFileNameChars())
		{
			suggested = suggested.Replace(bad, '_');
		}
		using var dialog = new SaveFileDialog { Title = all ? "Export all profiles" : "Export profile", Filter = "Library Organizer profiles (*.xml)|*.xml", FileName = suggested + ".xml", OverwritePrompt = true };
		if (dialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		try
		{
			SettingsStore.Export(dialog.FileName, all ? [.. profiles] : [profiles[current]]);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Ui.Warn(this, "Export profiles", $"The file could not be written: {ex.Message}");
		}
	}

	private static string Unique(string name, IEnumerable<string> taken)
	{
		var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
		string candidate = name;
		for (int n = 2; used.Contains(candidate); n++)
		{
			candidate = $"{name} ({n})";
		}
		return candidate;
	}
}
