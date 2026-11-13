using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LibraryOrganizer;

/// <param name="Done">Operations finished before this one.</param>
/// <param name="Current">The path the current operation works on. Empty when the run is over.</param>
public sealed record RunProgress(int Done, int Total, string Current);

public abstract record OpResult
{
	private OpResult() { }

	public sealed record Done(OpId Op) : OpResult;

	/// <summary>The end state already held: a resumed run, or a file that was moved by hand.</summary>
	public sealed record AlreadyDone(OpId Op) : OpResult;

	/// <summary>The disk changed after planning, or the system refused. Nothing was overwritten.</summary>
	public sealed record Failed(OpId Op, string Reason) : OpResult;

	/// <summary>The run was cancelled before it reached the operation.</summary>
	public sealed record NotRun(OpId Op) : OpResult;
}

public sealed record RunReport(RunId Run, IReadOnlyList<PlanEntry> Entries, IReadOnlyDictionary<OpId, OpResult> Results, bool Cancelled)
{
	/// <summary>
	/// Counts per profile, in the order profiles first appear. Planned counts the entries whose operations all
	/// succeeded. Failed adds the entries with an operation that failed. Skipped adds the entries a cancel left undone.
	/// </summary>
	public IReadOnlyList<ProfileTally> Tally()
	{
		bool Any<T>(PlanEntry entry) where T : OpResult
			=> Operations(entry).Any(operation => Results.TryGetValue(operation.Id, out OpResult? result) && result is T);
		bool Failed(PlanEntry entry) => entry.Outcome is EntryOutcome.Failed or EntryOutcome.PlannedAsFailed || Any<OpResult.Failed>(entry);
		bool Skipped(PlanEntry entry) => entry.Outcome is EntryOutcome.Skipped or EntryOutcome.Overridden || !Failed(entry) && Any<OpResult.NotRun>(entry);
		return Entries
			.GroupBy(entry => (entry.Key.Profile, entry.ProfileName))
			.Select(group => new ProfileTally(
				group.Key.ProfileName ?? "",
				Planned: group.Count(entry => entry.Outcome is EntryOutcome.Planned && !Failed(entry) && !Skipped(entry)),
				InPlace: group.Count(entry => entry.Outcome is EntryOutcome.AlreadyInPlace),
				Skipped: group.Count(Skipped),
				Failed: group.Count(Failed),
				Waiting: 0,
				Blocked: 0))
			.ToList();
	}

	private static IReadOnlyList<Operation> Operations(PlanEntry entry) => entry.Outcome switch
	{
		EntryOutcome.Planned planned => planned.Operations,
		EntryOutcome.PlannedAsFailed failed => failed.Operations,
		_ => [],
	};
}

/// <summary>Runs an approved plan. The only code that changes files or the library.</summary>
/// <remarks>
/// Synchronous: call it on a worker thread. Each operation is a file change, a "file" mark in the journal, a library
/// write through the gateway, then an "applied" mark. An operation whose end state already holds is not repeated, so
/// running the same plan again, or resuming it after a crash, ends in the same state as one clean run.
/// </remarks>
public sealed class Executor
{
	private const string CaseRenameSuffix = ".lo-rename";

	private readonly IFileSystem disk;
	private readonly ILibraryGateway library;
	private readonly JournalStore journals;

	public Executor(IFileSystem disk, ILibraryGateway library, JournalStore journals)
	{
		this.disk = disk;
		this.library = library;
		this.journals = journals;
	}

	/// <summary>
	/// Writes the plan to a new journal, then runs it. An operation the system refuses fails with the system's message
	/// and the run goes on. A cancel is honoured between operations and leaves the rest not run. Old journals are
	/// trimmed at the end.
	/// </summary>
	/// <exception cref="Exception">
	/// Whatever the gateway or the journal throws. The run stops there and its journal stays open, for <see cref="Resume"/>.
	/// </exception>
	public RunReport Run(ApprovedPlan plan, IProgress<RunProgress>? progress, CancellationToken cancel)
	{
		RunReport report;
		using (JournalWriter journal = journals.Begin(plan))
		{
			report = Execute(plan.Id, plan.Entries, plan.Operations, _ => default, journal, progress, cancel);
		}
		journals.Trim();
		return report;
	}

	/// <summary>
	/// Finishes a run that was interrupted. It does what is missing: a file change, or only the library write when
	/// the crash fell between the two. For a run that already finished, it changes nothing and returns what the
	/// journal recorded.
	/// </summary>
	public RunReport Resume(RunId run, IProgress<RunProgress>? progress, CancellationToken cancel)
	{
		RunJournal recorded = journals.Read(run);
		if (recorded.Summary.Finished)
		{
			return new RunReport(
				run,
				recorded.Entries,
				recorded.Operations.ToDictionary(operation => operation.Id, operation => recorded.Mark(operation.Id) switch
				{
					{ Failed: { } reason } => new OpResult.Failed(operation.Id, reason),
					{ Applied: true } => new OpResult.AlreadyDone(operation.Id),
					_ => (OpResult)new OpResult.NotRun(operation.Id),
				}),
				recorded.Summary.Cancelled);
		}
		RunReport report;
		using (JournalWriter journal = journals.Reopen(run))
		{
			report = Execute(run, recorded.Entries, recorded.Operations, recorded.Mark, journal, progress, cancel);
		}
		journals.Trim();
		return report;
	}

	private RunReport Execute(
		RunId run, IReadOnlyList<PlanEntry> entries, IReadOnlyList<Operation> operations, Func<OpId, OpMark> marks,
		JournalWriter journal, IProgress<RunProgress>? progress, CancellationToken cancel)
	{
		HashSet<OpId> held = DiskOverlay.Held(operations, marks, disk);
		var results = new Dictionary<OpId, OpResult>();
		bool cancelled = false;
		for (int i = 0; i < operations.Count; i++)
		{
			Operation operation = operations[i];
			cancelled = cancelled || cancel.IsCancellationRequested;
			if (cancelled)
			{
				results[operation.Id] = new OpResult.NotRun(operation.Id);
				continue;
			}
			progress?.Report(new RunProgress(i, operations.Count, Subject(operation)));
			OpMark mark = marks(operation.Id);
			bool done = held.Contains(operation.Id);
			if (!done)
			{
				try
				{
					ChangeFiles(operation);
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException)
				{
					journal.Failed(operation.Id, e.Message);
					results[operation.Id] = new OpResult.Failed(operation.Id, e.Message);
					continue;
				}
			}
			if (!mark.File)
			{
				journal.File(operation.Id);
			}
			if (!done || !mark.Applied)
			{
				journal.Applied(operation.Id, ChangeLibrary(operation));
			}
			results[operation.Id] = done ? new OpResult.AlreadyDone(operation.Id) : new OpResult.Done(operation.Id);
		}
		journal.End(cancelled);
		progress?.Report(new RunProgress(operations.Count, operations.Count, ""));
		return new RunReport(run, entries, results, cancelled);
	}

	private void ChangeFiles(Operation operation)
	{
		switch (operation)
		{
			case Operation.MoveFile move:
				disk.CreateFolder(move.To.Parent);
				disk.Move(move.From, move.To);
				break;
			case Operation.RenameCase rename:
				AbsPath temporary = rename.To.Parent.Combine(rename.To.Name + CaseRenameSuffix);
				if (!disk.FileExists(temporary))
				{
					disk.Move(rename.From, temporary);
				}
				disk.Move(temporary, rename.To);
				break;
			case Operation.CopyFile copy:
				disk.CreateFolder(copy.To.Parent);
				disk.Copy(copy.From, copy.To);
				break;
			case Operation.WriteCover cover:
				byte[] image = library.Cover(cover.Entry.Book, cover.Format) ?? throw new IOException("The book has no custom cover.");
				disk.CreateFolder(cover.To.Parent);
				disk.WriteNew(cover.To, image);
				break;
			case Operation.RecycleFile recycle:
				disk.Recycle(recycle.Path);
				break;
			case Operation.PruneFolders prune:
				Prune(prune);
				break;
		}
	}

	private BookId? ChangeLibrary(Operation operation)
	{
		switch (operation)
		{
			case Operation.MoveFile move:
				library.FileMoved(move.Entry.Book, move.To);
				break;
			case Operation.RenameCase rename:
				library.FileMoved(rename.Entry.Book, rename.To);
				break;
			case Operation.CopyFile { AddToLibrary: true } copy:
				return library.AddCopy(copy.Entry.Book, copy.To);
			case Operation.RecycleFile { ExistingBook: { } existing } recycle:
				if (recycle.TransferReadProgressTo is { } incoming)
				{
					library.TransferReadProgress(existing, incoming);
				}
				library.Remove(existing);
				break;
		}
		return null;
	}

	private void Prune(Operation.PruneFolders prune)
	{
		var refused = new List<string>();
		foreach (AbsPath candidate in prune.Candidates)
		{
			try
			{
				for (AbsPath folder = candidate; !folder.IsRoot && !prune.StopAt.Contains(folder) && !prune.Keep.Contains(folder); folder = folder.Parent)
				{
					if (!disk.FolderExists(folder))
					{
						continue;
					}
					if (disk.Children(folder).Count > 0)
					{
						break;
					}
					disk.DeleteEmptyFolder(folder);
				}
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				refused.Add(e.Message);
			}
		}
		if (refused.Count > 0)
		{
			throw new IOException(string.Join(" ", refused));
		}
	}

	private static string Subject(Operation operation) => operation switch
	{
		Operation.MoveFile move => move.From.Value,
		Operation.RenameCase rename => rename.From.Value,
		Operation.CopyFile copy => copy.From.Value,
		Operation.WriteCover cover => cover.To.Value,
		Operation.RecycleFile recycle => recycle.Path.Value,
		_ => "",
	};
}
