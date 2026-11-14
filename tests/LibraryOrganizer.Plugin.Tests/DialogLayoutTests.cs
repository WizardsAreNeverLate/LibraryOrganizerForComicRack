using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibraryOrganizer.Plugin.Forms;
using LibraryOrganizer.Plugin.Forms.Run;

namespace LibraryOrganizer.Plugin.Tests;

[NotInParallel]
public class DialogLayoutTests
{
	public static IEnumerable<Func<(string, float)>> Dialogs()
	{
		foreach (string dialog in Factories.Keys)
		{
			foreach (float points in new[] { 9f, 12f })
			{
				yield return () => (dialog, points);
			}
		}
	}

	private static readonly Dictionary<string, Func<Form>> Factories = new()
	{
		["ConfigForm"] = () =>
		{
			Settings settings = Settings.CreateDefault();
			ProfileSettings withRule = settings.Profiles[0] with
			{
				Rules = new RuleSettings.Group(Combinator.All, [new RuleSettings.Condition(Fields.MainCharacterOrTeam.Key, Operator.Is, "X-Men")]),
			};
			return new ConfigForm(settings with { Profiles = [withRule] });
		},
		["ConfigForm Rules tab"] = () => Factories!["ConfigForm"](),
		["ReviewForm"] = () =>
		{
			Plan plan = Organizer.Plan(new OrganizeInput([], [], SeriesIndex.Empty, new WindowsFileSystem(), new Dictionary<AbsPath, BookId>()), Decisions.None);
			return new ReviewForm(plan, _ => plan);
		},
		["ProfilePicker"] = () =>
		{
			Settings settings = Settings.CreateDefault();
			return new ProfilePicker(settings.Profiles, []);
		},
		["QuestionDialog"] = () => new QuestionDialog(new Question(
			new QuestionKey(Fields.Writer, new AnswerScope.Series(new SeriesKey("Marvel", "X-Men", 2))), "Writer", ["Chris Claremont", "Jim Lee"], [], "X-Men #7", 1)),
		["PathEditorDialog"] = () => new PathEditorDialog("The path is too long.", @"D:\Comics\x.cbz", 259),
		["NameDialog"] = () => new NameDialog("New profile", "Name", "Default", _ => null),
	};

	[Test]
	[MethodDataSource(nameof(Dialogs))]
	public async Task Every_button_is_tall_enough_for_its_text(string dialog, float fontPoints)
	{
		string[] clipped = [];
		var thread = new Thread(() =>
		{
			using Form form = Show(dialog, fontPoints);
			clipped = [.. Controls(form).OfType<ButtonBase>()
				.Where(b => b.Visible && b.Height < b.GetPreferredSize(Size.Empty).Height)
				.Select(b => $"{b.Text.Trim()} {b.Height}<{b.GetPreferredSize(Size.Empty).Height}")];
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		await Assert.That(clipped).IsEmpty();
	}

	[Test]
	[MethodDataSource(nameof(Dialogs))]
	public async Task Every_dropdown_list_is_wide_enough_for_its_longest_item(string dialog, float fontPoints)
	{
		string[] truncated = [];
		var thread = new Thread(() =>
		{
			using Form form = Show(dialog, fontPoints);
			truncated = [.. Controls(form).OfType<ComboBox>()
				.Where(c => c.Visible && c.DropDownStyle == ComboBoxStyle.DropDownList && c.Items.Count > 0)
				.Select(c => (Combo: c, Longest: c.Items.Cast<object>().Select(c.GetItemText).OrderByDescending(t => TextRenderer.MeasureText(t, c.Font).Width).First()))
				.Where(x => TextRenderer.MeasureText(x.Longest, x.Combo.Font).Width > x.Combo.Width - SystemInformation.VerticalScrollBarWidth - 6)
				.Select(x => $"'{x.Longest}' in {x.Combo.Width}px")];
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		await Assert.That(truncated).IsEmpty();
	}

	private static Form Show(string dialog, float fontPoints)
	{
		Form form = Factories[dialog]();
		// Display scaling makes the dialog font larger in pixels while fixed control sizes stay put; turning off
		// AutoScaleMode keeps the font change from resizing the controls, as it does on a high-DPI screen.
		form.AutoScaleMode = AutoScaleMode.None;
		form.Font = new Font("Segoe UI", fontPoints);
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(-20000, -20000);
		form.ShowInTaskbar = false;
		form.Show();
		Application.DoEvents();
		if (dialog == "ConfigForm Rules tab")
		{
			((ConfigForm)form).ShowTab(ConfigTab.Rules);
			Application.DoEvents();
		}
		return form;
	}

	private static IEnumerable<Control> Controls(Control parent)
	{
		foreach (Control child in parent.Controls)
		{
			yield return child;
			foreach (Control grandchild in Controls(child))
			{
				yield return grandchild;
			}
		}
	}
}
