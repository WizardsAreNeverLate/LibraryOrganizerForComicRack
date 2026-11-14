using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Run;

/// <summary>
/// Asks which profiles run, and in what order. Starts from the last run order. <see cref="Chosen"/> is set when the
/// dialog closes with OK; Cancel leaves it null.
/// </summary>
public sealed class ProfilePicker : Form
{
	private sealed record Item(ProfileSettings Profile)
	{
		public override string ToString() => $"{Profile.Name}  ({(Profile.Action is ProfileAction.Move ? "Move" : "Copy")})";
	}

	private readonly ListBox available = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
	private readonly ListBox order = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
	private readonly Button ok = new() { Text = "OK", AutoSize = true, MinimumSize = new Size(90, 0), UseVisualStyleBackColor = true };

	public ProfilePicker(IReadOnlyList<ProfileSettings> profiles, IReadOnlyList<ProfileId> lastRunOrder)
	{
		IReadOnlyList<ProfileSettings> start = ProfileOrder.Start(profiles, lastRunOrder);
		order.Items.AddRange([.. start.Select(profile => new Item(profile))]);
		available.Items.AddRange([.. profiles.Where(profile => !start.Contains(profile)).Select(profile => new Item(profile))]);

		Text = "Choose the profiles to run";
		Font = SystemFonts.MessageBoxFont;
		AutoScaleMode = AutoScaleMode.Font;
		FormBorderStyle = FormBorderStyle.Sizable;
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(640, 400);
		MinimumSize = new Size(560, 340);

		var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
		ok.Click += (_, _) => Accept();
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
		buttons.Controls.AddRange([cancel, ok]);
		AcceptButton = ok;
		CancelButton = cancel;

		var moves = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(8, 0, 8, 0) };
		moves.Controls.Add(Ui.Button("Add >", (_, _) => Transfer(available, order)));
		moves.Controls.Add(Ui.Button("< Remove", (_, _) => Transfer(order, available)));
		moves.Controls.Add(Ui.Button("Move up", (_, _) => MoveSelected(-1)));
		moves.Controls.Add(Ui.Button("Move down", (_, _) => MoveSelected(1)));
		foreach (Control button in moves.Controls)
		{
			button.Margin = new Padding(0, 0, 0, 6);
			button.MinimumSize = new Size(96, 0);
		}
		available.DoubleClick += (_, _) => Transfer(available, order);
		order.DoubleClick += (_, _) => Transfer(order, available);
		order.SelectedIndexChanged += (_, _) => UpdateOk();

		var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill };
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		grid.Controls.Add(Ui.Label("Profiles"), 0, 0);
		grid.Controls.Add(Ui.Label("Run order, first to last"), 2, 0);
		grid.Controls.Add(available, 0, 1);
		grid.Controls.Add(moves, 1, 1);
		grid.Controls.Add(order, 2, 1);

		var main = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, Padding = new Padding(12) };
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		main.Controls.Add(Ui.Hint("Profiles run from top to bottom. The last Move profile that accepts a book decides where it goes. Every Copy profile makes its own copy."), 0, 0);
		main.Controls.Add(grid, 0, 1);
		main.Controls.Add(buttons, 0, 2);
		Controls.Add(main);
		UpdateOk();
	}

	/// <summary>The profile ids in run order, set when the dialog closes with OK.</summary>
	public IReadOnlyList<ProfileId>? Chosen { get; private set; }

	/// <summary>Shows the dialog. Null when the user cancels.</summary>
	public static IReadOnlyList<ProfileId>? Pick(IWin32Window? owner, IReadOnlyList<ProfileSettings> profiles, IReadOnlyList<ProfileId> lastRunOrder)
	{
		using var picker = new ProfilePicker(profiles, lastRunOrder);
		return picker.ShowDialog(owner) == DialogResult.OK ? picker.Chosen : null;
	}

	private void Accept()
	{
		Chosen = [.. order.Items.Cast<Item>().Select(item => item.Profile.Id)];
		DialogResult = DialogResult.OK;
	}

	private void UpdateOk() => ok.Enabled = order.Items.Count > 0;

	private void Transfer(ListBox from, ListBox to)
	{
		Item[] moving = [.. from.SelectedItems.Cast<Item>()];
		foreach (Item item in moving)
		{
			from.Items.Remove(item);
			to.Items.Add(item);
		}
		to.ClearSelected();
		foreach (Item item in moving)
		{
			to.SelectedItems.Add(item);
		}
		UpdateOk();
	}

	private void MoveSelected(int delta)
	{
		int[] selected = [.. order.SelectedIndices.Cast<int>().OrderBy(index => index)];
		List<object> items = [.. order.Items.Cast<object>()];
		int[] moved = ProfileOrder.Shift(items, selected, delta);
		if (moved.SequenceEqual(selected))
		{
			return;
		}
		order.BeginUpdate();
		order.Items.Clear();
		order.Items.AddRange([.. items]);
		foreach (int index in moved)
		{
			order.SelectedIndices.Add(index);
		}
		order.EndUpdate();
	}
}

internal static class ProfileOrder
{
	/// <summary>
	/// The last run order, minus profiles that no longer exist and repeats. When nothing is left, the first profile, so
	/// OK is possible.
	/// </summary>
	public static IReadOnlyList<ProfileSettings> Start(IReadOnlyList<ProfileSettings> profiles, IReadOnlyList<ProfileId> lastRunOrder)
	{
		List<ProfileSettings> start = [];
		foreach (ProfileId id in lastRunOrder)
		{
			ProfileSettings? found = profiles.FirstOrDefault(profile => profile.Id == id);
			if (found is not null && !start.Contains(found))
			{
				start.Add(found);
			}
		}
		return start.Count > 0 || profiles.Count == 0 ? start : [profiles[0]];
	}

	/// <summary>
	/// Moves the items at <paramref name="selected"/> (ascending) one step up (negative) or down in place, and returns
	/// where they are now. Selected items at the edge stay put, so a block never gets reordered inside itself.
	/// </summary>
	public static int[] Shift<T>(IList<T> items, int[] selected, int delta)
	{
		if (selected.Length == 0 || delta < 0 && selected[0] == 0 || delta > 0 && selected[selected.Length - 1] == items.Count - 1)
		{
			return selected;
		}
		int step = Math.Sign(delta);
		foreach (int index in step < 0 ? selected : selected.Reverse())
		{
			(items[index], items[index + step]) = (items[index + step], items[index]);
		}
		return [.. selected.Select(index => index + step)];
	}
}
