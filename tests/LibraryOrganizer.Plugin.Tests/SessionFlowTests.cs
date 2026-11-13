using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace LibraryOrganizer.Plugin.Tests;

[NotInParallel]
public sealed class SessionFlowTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "lo-review-" + Guid.NewGuid().ToString("N"));
	private readonly List<string> messages = [];
	private string expected = "";

	public SessionFlowTests() => Directory.CreateDirectory(Path.Combine(root, "In"));

	public void Dispose() => Directory.Delete(root, true);

	private sealed class GatewayThatDies : ILibraryGateway
	{
		public void FileMoved(BookId book, AbsPath newPath) => throw new InvalidOperationException("ComicRack went away");
		public BookId AddCopy(BookId source, AbsPath copy) => throw new NotSupportedException();
		public void Remove(BookId book) => throw new NotSupportedException();
		public void TransferReadProgress(BookId from, BookId to) => throw new NotSupportedException();
		public byte[]? Cover(BookId book, CoverFormat format) => null;
	}

	[Test]
	public async Task Finish_in_History_completes_an_interrupted_run()
	{
		ComicBook book = Book("x-men 7.cbz");
		var app = new FakeApplication(book);

		OnUiThread(() =>
		{
			Session session = NewSession(app);
			try
			{
				new Executor(new WindowsFileSystem(), new GatewayThatDies(), session.Journals).Run(PlanFor(app, book), null, CancellationToken.None);
			}
			catch (Exception)
			{
			}
			Drive("Finish", session.History);
		});

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(expected)).IsTrue();
			await Assert.That(string.Join(" | ", messages)).IsEqualTo("");
			await Assert.That(book.FilePath).IsEqualTo(expected);
		}
	}

	[Test]
	public async Task Opening_an_undo_review_keeps_the_session_answers()
	{
		ComicBook book = Book("x-men 7.cbz");
		var app = new FakeApplication(book);
		var key = new QuestionKey(Fields.All[0], new AnswerScope.Series(new SeriesKey("Marvel", "X-Men", 2)));
		int remembered = -1;

		OnUiThread(() =>
		{
			Session session = NewSession(app);
			new Executor(new WindowsFileSystem(), new ComicRackGateway(app), session.Journals).Run(PlanFor(app, book), null, CancellationToken.None);
			typeof(Session).GetProperty("Answers")!.SetValue(session, AnswerBook.Empty.With(key, new Answer(["Chris Claremont"], false, false, false, false)));
			Drive("Undo...", session.History);
			remembered = session.Answers.Remembered.Count;
		});

		await Assert.That(remembered).IsEqualTo(1);
	}

	[Test]
	public async Task A_book_from_the_folder_view_follows_its_file()
	{
		ComicBook book = Book("x-men 7.cbz");
		var app = new FakeApplication();
		SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

		new Executor(new WindowsFileSystem(), new ComicRackGateway(app, [book]), new JournalStore(Path.Combine(root, "data"), 5)).Run(PlanFor(app, book), null, CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(expected)).IsTrue();
			await Assert.That(book.FilePath).IsEqualTo(expected);
		}
	}

	private ApprovedPlan PlanFor(FakeApplication app, ComicBook book)
	{
		Profile profile = new ProfileSettings { Name = "Main", BaseFolder = Path.Combine(root, "Comics") }.Compile().Profile!;
		LibrarySnapshot snapshot = LibrarySnapshot.Take([book], app.Library);
		ApprovedPlan plan = Organizer.Plan(new OrganizeInput([profile], snapshot.Selection, snapshot.Series, new WindowsFileSystem(), snapshot.LibraryFiles), Decisions.None).Approve()!;
		expected = plan.Operations.OfType<Operation.MoveFile>().Single().To.Value;
		return plan;
	}

	private Session NewSession(FakeApplication app)
	{
		Environment.SetEnvironmentVariable("LIBRARYORGANIZER_NET_DATA", Path.Combine(root, "data"));
		typeof(Session).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
		return Session.Get(new PluginEnvironment(null, app, null, null, null, null), Path.Combine(root, "Scripts", "LibraryOrganizer"));
	}

	private ComicBook Book(string name)
	{
		string path = Path.Combine(root, "In", name);
		File.WriteAllText(path, "7");
		return new ComicBook { Series = "X-Men", Publisher = "Marvel", Volume = 2, Number = "7", Count = 12, Year = 1992, Month = 3, FilePath = path };
	}

	private static void OnUiThread(Action body)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
				body();
			}
			catch (Exception e)
			{
				failure = e;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure is not null)
		{
			throw new InvalidOperationException(failure.ToString());
		}
	}

	/// <summary>Plays the user: the named button on the first History window, Close on everything after it.</summary>
	private void Drive(string firstHistoryButton, Action command)
	{
		var handled = new HashSet<Form>();
		int history = 0;
		int ticks = 0;
		using var timer = new System.Windows.Forms.Timer { Interval = 50 };
		timer.Tick += (_, _) =>
		{
			CloseMessageBoxes();
			bool giveUp = ++ticks > 400;
			foreach (Form form in Application.OpenForms.Cast<Form>().Where(f => f.Visible && f.IsHandleCreated && (giveUp || !handled.Contains(f))).ToList())
			{
				string type = form.GetType().Name;
				if (type == "RunWindow" && !giveUp)
				{
					continue;
				}
				handled.Add(form);
				if (type == "HistoryForm" && !giveUp)
				{
					string text = ++history == 1 ? firstHistoryButton : "Close";
					form.BeginInvoke(new Action(() => Controls(form).OfType<Button>().First(b => b.Text == text).PerformClick()));
				}
				else
				{
					form.BeginInvoke(new Action(form.Close));
				}
			}
		};
		timer.Start();
		command();
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
				messages.Add(text.ToString().Split((char)10)[0].Trim());
				PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
			}
			return true;
		}, IntPtr.Zero);
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
