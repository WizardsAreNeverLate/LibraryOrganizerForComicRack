using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer.Plugin.Forms.Execution;

internal enum ReportStatus { Done, AlreadyInPlace, Skipped, Failed, NotRun }

internal sealed record ReportRow(string Book, string Profile, ReportStatus Status, string Detail);

/// <summary>One row per plan entry, with the outcome of its operations folded into one status.</summary>
internal static class ReportRows
{
	public static IReadOnlyList<ReportRow> From(RunReport report) => [.. report.Entries.Select(entry => Row(entry, report))];

	public static string Summary(IReadOnlyList<ReportRow> rows, bool cancelled)
	{
		string counts = string.Join(", ", rows.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Count()} {Text(g.Key).ToLowerInvariant()}"));
		return (cancelled ? "Cancelled. " : "") + (counts.Length == 0 ? "Nothing to do." : counts + ".");
	}

	public static string Text(ReportStatus status) => status switch
	{
		ReportStatus.Done => "Done",
		ReportStatus.AlreadyInPlace => "Already in place",
		ReportStatus.Skipped => "Skipped",
		ReportStatus.Failed => "Failed",
		_ => "Not run",
	};

	private static ReportRow Row(PlanEntry entry, RunReport report)
	{
		string profile = entry.ProfileName ?? "";
		return entry.Outcome switch
		{
			EntryOutcome.Planned planned => FromOperations(entry, profile, planned.Operations, report, null),
			EntryOutcome.PlannedAsFailed failed => FromOperations(entry, profile, failed.Operations, report, failed.Reason),
			EntryOutcome.AlreadyInPlace => new(entry.BookCaption, profile, ReportStatus.AlreadyInPlace, ""),
			EntryOutcome.Skipped skipped => new(entry.BookCaption, profile, ReportStatus.Skipped, skipped.Reason),
			EntryOutcome.Overridden overridden => new(entry.BookCaption, profile, ReportStatus.Skipped, $"Moved by the later profile '{overridden.ByProfile}'"),
			EntryOutcome.Failed failed => new(entry.BookCaption, profile, ReportStatus.Failed, failed.Reason),
			_ => new(entry.BookCaption, profile, ReportStatus.NotRun, "Not decided before the run"),
		};
	}

	private static ReportRow FromOperations(PlanEntry entry, string profile, IReadOnlyList<Operation> operations, RunReport report, string? failedFields)
	{
		OpResult[] results = [.. operations.Select(op => report.Results.TryGetValue(op.Id, out OpResult? r) ? r : new OpResult.NotRun(op.Id))];
		string target = string.Join("; ", operations.Select(Target).Where(t => t.Length > 0));
		if (results.OfType<OpResult.Failed>().FirstOrDefault() is { } failure)
		{
			return new(entry.BookCaption, profile, ReportStatus.Failed, failure.Reason);
		}
		if (results.Any(r => r is OpResult.NotRun))
		{
			return new(entry.BookCaption, profile, ReportStatus.NotRun, target);
		}
		return failedFields is null
			? new(entry.BookCaption, profile, ReportStatus.Done, target)
			: new(entry.BookCaption, profile, ReportStatus.Failed, $"{failedFields} Moved to {target}");
	}

	private static string Target(Operation operation) => operation switch
	{
		Operation.MoveFile move => move.To.Value,
		Operation.RenameCase rename => rename.To.Value,
		Operation.CopyFile copy => copy.To.Value,
		Operation.WriteCover cover => cover.To.Value,
		_ => "",
	};
}
