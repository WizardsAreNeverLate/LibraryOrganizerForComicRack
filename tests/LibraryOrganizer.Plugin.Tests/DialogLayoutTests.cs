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
		["ConfigForm"] = () => new ConfigForm(Settings.CreateDefault()),
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
			using Form form = Factories[dialog]();
			form.Font = new Font("Segoe UI", fontPoints);
			form.StartPosition = FormStartPosition.Manual;
			form.Location = new Point(-20000, -20000);
			form.ShowInTaskbar = false;
			form.Show();
			Application.DoEvents();
			clipped = [.. Controls(form).OfType<ButtonBase>()
				.Where(b => b.Height < b.GetPreferredSize(Size.Empty).Height)
				.Select(b => $"{b.Text.Trim()} {b.Height}<{b.GetPreferredSize(Size.Empty).Height}")];
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		await Assert.That(clipped).IsEmpty();
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
