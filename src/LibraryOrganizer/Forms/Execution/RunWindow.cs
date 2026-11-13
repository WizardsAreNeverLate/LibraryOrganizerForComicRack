using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms.Execution;

/// <summary>
/// A modal progress window. The work runs on a worker thread; closing the window or Cancel asks it to stop between
/// operations, and the window stays open until it has.
/// </summary>
internal sealed class RunWindow : Form
{
	private readonly Func<IProgress<RunProgress>, CancellationToken, RunReport> work;
	private readonly CancellationTokenSource cancel = new();
	private readonly ProgressBar bar = new() { Dock = DockStyle.Top, Height = 22 };
	private readonly Label current = new() { Dock = DockStyle.Top, AutoEllipsis = true, Height = 36, TextAlign = ContentAlignment.MiddleLeft };
	private readonly Button stop = new() { Text = "Cancel", AutoSize = true, Anchor = AnchorStyles.Right };
	private Task<RunReport>? running;
	private RunReport? report;
	private ExceptionDispatchInfo? failure;

	private RunWindow(string title, Func<IProgress<RunProgress>, CancellationToken, RunReport> work)
	{
		this.work = work;
		Text = title;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = MinimizeBox = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(520, 110);
		Padding = new Padding(12);
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
		buttons.Controls.Add(stop);
		Controls.Add(buttons);
		Controls.Add(current);
		Controls.Add(bar);
		stop.Click += (_, _) => RequestStop();
	}

	/// <summary>Runs <paramref name="work"/> and returns its report. An exception from the work is rethrown here.</summary>
	public static RunReport Show(IWin32Window owner, string title, Func<IProgress<RunProgress>, CancellationToken, RunReport> work)
	{
		using var window = new RunWindow(title, work);
		window.ShowDialog(owner);
		window.failure?.Throw();
		return window.report!;
	}

	protected override async void OnShown(EventArgs e)
	{
		base.OnShown(e);
		var progress = new Progress<RunProgress>(p =>
		{
			bar.Maximum = Math.Max(1, p.Total);
			bar.Value = Math.Min(bar.Maximum, p.Done);
			current.Text = p.Current;
		});
		running = Task.Run(() => work(progress, cancel.Token));
		try
		{
			report = await running;
		}
		catch (Exception error)
		{
			failure = ExceptionDispatchInfo.Capture(error);
		}
		Close();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (running is { IsCompleted: false })
		{
			e.Cancel = true;
			RequestStop();
		}
		base.OnFormClosing(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			cancel.Dispose();
		}
		base.Dispose(disposing);
	}

	private void RequestStop()
	{
		cancel.Cancel();
		stop.Enabled = false;
		current.Text = "Stopping after the current file...";
	}
}
