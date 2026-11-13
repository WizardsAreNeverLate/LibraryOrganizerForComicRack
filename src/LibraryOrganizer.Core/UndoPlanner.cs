using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>
/// Builds the plan that undoes a run. It walks the run's operations latest first over a <see cref="DiskOverlay"/> that
/// follows the undo: the overlay is the disk as it will be once the inverses emitted so far have run.
/// </summary>
internal static class UndoPlanner
{
	private const string NothingToUndo = "The run did not change this file, or it has changed since";
	private const string CannotBeUndone = "The run sent a file to the Recycle Bin, and that cannot be undone";

	private sealed class Row(PlanEntry entry)
	{
		public PlanEntry Entry { get; } = entry;
		public List<Operation> Operations { get; } = [];
		public (ConflictKey Key, Operation Pending)? Blocked { get; set; }
		public string? Skipped { get; set; }
		public bool Irreversible { get; set; }

		public EntryOutcome Outcome
			=> Blocked is { } blocked ? new EntryOutcome.Blocked(blocked.Key, blocked.Pending)
				: Operations.Count > 0 && Irreversible ? new EntryOutcome.PlannedAsFailed(Operations, CannotBeUndone)
				: Operations.Count > 0 ? new EntryOutcome.Planned(Operations)
				: Irreversible ? new EntryOutcome.Failed(CannotBeUndone)
				: new EntryOutcome.Skipped(Skipped ?? NothingToUndo);
	}

	public static Plan Plan(RunJournal journal, IFileProbe disk, ILookup<AbsPath, BookId> libraryFiles, Decisions decisions)
	{
		var overlay = new DiskOverlay(disk, journal.Operations, journal.Mark);
		var rows = new List<Row>();
		var rowOf = new Dictionary<EntryKey, Row>();
		var operations = new List<Operation>();
		var conflicts = new List<Conflict>();
		ILookup<EntryKey, PlanEntry> recorded = journal.Entries.ToLookup(entry => entry.Key);

		foreach (Operation original in journal.Operations.Reverse().Where(operation => operation is not Operation.PruneFolders))
		{
			if (!rowOf.TryGetValue(original.Entry, out Row? row))
			{
				PlanEntry entry = recorded[original.Entry].FirstOrDefault() ?? new PlanEntry(original.Entry, "", null, new EntryOutcome.AlreadyInPlace());
				rowOf[original.Entry] = row = new Row(entry);
				rows.Add(row);
			}
			bool renameCutShort = original is Operation.RenameCase rename && overlay.FileExists(rename.Temporary);
			if (!overlay.Holds(original) && !renameCutShort)
			{
				continue;
			}
			if (BookFileAt(original) is { } at && libraryFiles[at].Any(holder => holder != original.Entry.Book))
			{
				row.Skipped = $"Another library book is at {at}, so the file there may not be this book's";
				continue;
			}
			Operation? inverse = original.Inverse(new OpId(operations.Count + 1), journal.Mark(original.Id).Created);
			if (inverse is null)
			{
				row.Irreversible = true;
				continue;
			}
			if (inverse is Operation.MoveFile back && overlay.FileExists(back.To))
			{
				AbsPath keepBoth = Planner.FreeName(back.To, overlay.FileExists);
				List<ResolutionChoice> allowed = [ResolutionChoice.Skip, ResolutionChoice.KeepBoth];
				var key = new ConflictKey(original.Entry, back.To);
				Resolution? chosen = decisions.Resolutions.TryGetValue(key, out Resolution? choice) && allowed.Contains(choice.Choice) ? choice
					: decisions.Default is { } everywhere && allowed.Contains(everywhere.Choice) ? everywhere
					: null;
				var kind = new ConflictKind.TargetExists(null);
				conflicts.Add(new Conflict(key, kind, allowed, keepBoth, chosen));
				if (chosen is Resolution.KeepBoth)
				{
					inverse = back with { To = keepBoth };
				}
				else
				{
					if (chosen is null)
					{
						row.Blocked = (key, back with { Id = default });
					}
					else
					{
						row.Skipped = $"{kind.Describe()}: {back.To}";
					}
					continue;
				}
			}
			operations.Add(inverse);
			row.Operations.Add(inverse);
			overlay.Apply(inverse);
		}

		if (journal.Operations.OfType<Operation.PruneFolders>().LastOrDefault() is { } pruned)
		{
			List<AbsPath> emptied = operations.OfType<Operation.MoveFile>().Select(move => move.From.Parent).Distinct().ToList();
			if (emptied.Count > 0)
			{
				operations.Add(new Operation.PruneFolders(new OpId(operations.Count + 1), emptied, pruned.StopAt, pruned.Keep));
			}
		}

		return new Plan(
			new PlanKind.Undo(journal.Summary.Id),
			rows.Select(row => row.Entry with { Outcome = row.Outcome }).ToList(),
			[],
			conflicts,
			operations,
			decisions.Answers);
	}

	/// <summary>Where the operation left the book's own file. A copy or a cover is a new file, so it has none.</summary>
	private static AbsPath? BookFileAt(Operation operation) => operation switch
	{
		Operation.MoveFile move => move.To,
		Operation.RenameCase rename => rename.To,
		_ => null,
	};
}
