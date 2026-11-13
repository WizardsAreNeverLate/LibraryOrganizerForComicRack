using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using LibraryOrganizer;

namespace LoSmoke;

/// <summary>
/// Runs inside a real ComicRack started with an alternate configuration. Adds two books to that library, runs
/// Library Organizer (Quick) on them through ComicRack's own command path, then undoes the run from History, clicking
/// the plugin's dialogs as a user would. Writes ASSERT lines to report.txt and closes ComicRack.
/// </summary>
public sealed class Probe : DotNetPlugin, IStartup
{
	private readonly string root = Environment.GetEnvironmentVariable("LO_SMOKE_ROOT")!;
	private readonly List<string> seen = [];
	private readonly HashSet<Form> handled = [];
	private Form main = null!;
	private int historyOpened;

	public override string Key { get; set; } = "lo-smoke";

	public override string Name { get; set; } = "Library Organizer smoke probe";

	private string ReportPath => Path.Combine(root, "out", "report.txt");

	void IStartup.Execute()
	{
		main = (Form)ComicRack.MainWindow;
		var timer = new Timer { Interval = 100 };
		timer.Tick += (_, _) => Drive();
		try
		{
			timer.Start();
			Run();
		}
		catch (Exception e)
		{
			Add("EXCEPTION " + e);
		}
		finally
		{
			timer.Stop();
			Add("dialogs seen: " + string.Join(" | ", seen));
			// A plain Close only hides ComicRack to the tray; MenuClose is File > Exit.
			main.BeginInvoke(new Action(() => main.GetType().GetMethod("MenuClose", BindingFlags.Instance | BindingFlags.Public)!.Invoke(main, null)));
		}
	}

	private void Run()
	{
		Type utility = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("cYo.Projects.ComicRack.Viewer.ScriptUtility")).First(t => t != null);
		var engine = (PluginEngine)utility.GetProperty("Scripts")!.GetValue(null, null)!;
		MethodInfo run = utility.GetMethod("CreateBookCode")!;
		string[] commands = [.. engine.GetAllCommands().Where(c => c.Key.StartsWith("library-organizer-net")).Select(c => $"{c.Key}:{c.Hook}").OrderBy(s => s)];
		Check("ComicRack registered the three commands", commands.Length == 3, string.Join(", ", commands));

		string inbox = Directory.CreateDirectory(Path.Combine(root, "In")).FullName;
		string comics = Path.Combine(root, "Comics");
		var profile = new ProfileSettings { Name = "Smoke", BaseFolder = comics };
		new SettingsStore(Environment.GetEnvironmentVariable("LIBRARYORGANIZER_NET_DATA")!)
			.Save(new Settings([profile], [profile.Id], RunPreferences.Default));

		ComicBook first = AddBook(Path.Combine(inbox, "xmen 1.cbz"), "1", 1991, 10);
		ComicBook seventh = AddBook(Path.Combine(inbox, "xmen 7.cbz"), "7", 1992, 3);
		string firstTarget = Path.Combine(comics, @"Marvel\X-Men (1991)\X-Men Vol.2 #01 (of 12) (October, 1991).cbz");
		string seventhTarget = Path.Combine(comics, @"Marvel\X-Men (1991)\X-Men Vol.2 #07 (of 12) (March, 1992).cbz");

		Command quick = engine.GetAllCommands().Single(c => c.Key == "library-organizer-net-quick" && c.IsHook("Books"));
		run.Invoke(null, [main, quick, (Func<IEnumerable<ComicBook>>)(() => new[] { first, seventh })]);

		Check("quick run moved both files to the 2.x default-template paths", File.Exists(firstTarget) && File.Exists(seventhTarget), $"{firstTarget} | {seventhTarget}");
		Check("quick run emptied the sources", !File.Exists(Path.Combine(inbox, "xmen 1.cbz")) && !File.Exists(Path.Combine(inbox, "xmen 7.cbz")), inbox);
		Check("the library books follow their files", first.FilePath == firstTarget && seventh.FilePath == seventhTarget, $"{first.FilePath} | {seventh.FilePath}");
		string runs = Path.Combine(Environment.GetEnvironmentVariable("LIBRARYORGANIZER_NET_DATA")!, "runs");
		Check("the run wrote one journal", Directory.Exists(runs) && Directory.GetFiles(runs, "*.journal").Length == 1, runs);

		Command history = engine.GetAllCommands().Single(c => c.Key == "library-organizer-net-history" && c.IsHook("Library"));
		run.Invoke(null, [main, history, (Func<IEnumerable<ComicBook>>)(() => ComicRack.App.GetLibraryBooks())]);

		Check("undo moved both files back", File.Exists(Path.Combine(inbox, "xmen 1.cbz")) && File.Exists(Path.Combine(inbox, "xmen 7.cbz")), inbox);
		Check("undo restored the library paths", first.FilePath == Path.Combine(inbox, "xmen 1.cbz") && seventh.FilePath == Path.Combine(inbox, "xmen 7.cbz"), $"{first.FilePath} | {seventh.FilePath}");
		Check("undo removed the folders the run created", !Directory.Exists(Path.Combine(comics, "Marvel")), Path.Combine(comics, "Marvel"));
		Check("history was shown twice (undo, then close)", historyOpened == 2, historyOpened.ToString());
	}

	private ComicBook AddBook(string path, string number, int year, int month)
	{
		File.WriteAllText(path, "smoke " + number);
		ComicBook book = ComicRack.App.AddNewBook(false);
		book.Series = "X-Men";
		book.Publisher = "Marvel";
		book.Volume = 2;
		book.Number = number;
		book.Count = 12;
		book.Year = year;
		book.Month = month;
		book.FilePath = path;
		return book;
	}

	private void Drive()
	{
		CloseMessageBoxes();
		foreach (Form form in Application.OpenForms.Cast<Form>().Where(f => f.Visible && f.IsHandleCreated && !handled.Contains(f)).ToList())
		{
			switch (form.GetType().Name)
			{
				case "ReportForm":
					handled.Add(form);
					seen.Add("report: " + string.Join(" / ", Controls(form).OfType<Label>().Select(l => l.Text).Where(t => t.Length > 0)));
					form.BeginInvoke(new Action(form.Close));
					break;
				case "HistoryForm":
					handled.Add(form);
					historyOpened++;
					seen.Add("history #" + historyOpened);
					Click(form, historyOpened == 1 ? "Undo..." : "Close");
					break;
				case "ReviewForm":
					if (Controls(form).OfType<Button>().FirstOrDefault(b => b.Text == "Run") is { Enabled: true })
					{
						handled.Add(form);
						seen.Add("review: Run");
						Click(form, "Run");
					}
					break;
				case "RunWindow":
					break;
				case "NewsDialog":
					handled.Add(form);
					form.BeginInvoke(new Action(form.Close));
					break;
				default:
					if (form != main && form.GetType().Namespace?.StartsWith("LibraryOrganizer") == true)
					{
						handled.Add(form);
						seen.Add("UNEXPECTED " + form.GetType().Name + ": " + form.Text);
						form.BeginInvoke(new Action(form.Close));
					}
					break;
			}
		}
	}

	private static void Click(Form form, string text)
		=> form.BeginInvoke(new Action(() => Controls(form).OfType<Button>().First(b => b.Text == text).PerformClick()));

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

	/// <summary>A message box is not in OpenForms and would block the run: record its text and close it.</summary>
	private void CloseMessageBoxes()
	{
		EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
		{
			var className = new StringBuilder(64);
			GetClassName(window, className, className.Capacity);
			if (className.ToString() == "#32770" && IsWindowVisible(window))
			{
				var text = new StringBuilder(4096);
				GetDlgItemText(window, 0xFFFF, text, text.Capacity);
				seen.Add("MESSAGE BOX: " + text.ToString().Replace("\r", " ").Replace("\n", " "));
				Add("MESSAGE BOX " + text);
				PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
			}
			return true;
		}, IntPtr.Zero);
	}

	private void Check(string name, bool ok, string detail) => Add($"ASSERT {(ok ? "PASS" : "FAIL")} {name}: {detail}");

	private void Add(string line)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
		File.AppendAllText(ReportPath, line + Environment.NewLine);
	}

	private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

	[DllImport("user32.dll")]
	private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr data);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetDlgItemText(IntPtr dialog, int id, StringBuilder text, int capacity);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr window);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
