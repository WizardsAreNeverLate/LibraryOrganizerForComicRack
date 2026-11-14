using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibraryOrganizer.Plugin.Forms.Run;

namespace LibraryOrganizer.Plugin.Tests;

public class ReviewFormLayoutTests
{
	[Test]
	[Arguments(9f)]
	[Arguments(12f)]
	public async Task Every_button_is_tall_enough_for_its_text(float fontPoints)
	{
		string[] clipped = [];
		var thread = new Thread(() =>
		{
			Plan plan = Organizer.Plan(new OrganizeInput([], [], SeriesIndex.Empty, new WindowsFileSystem(), new Dictionary<AbsPath, BookId>()), Decisions.None);
			using var form = new ReviewForm(plan, decisions => plan) { Font = new Font("Segoe UI", fontPoints) };
			form.CreateControl();
			form.PerformLayout();
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
