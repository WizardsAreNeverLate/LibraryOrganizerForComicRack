using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer.Plugin.Forms.Run;

/// <summary>What the review grid calls an entry's outcome. Failed includes books planned into the failed-books folder.</summary>
internal enum ReviewOutcome { Planned, AlreadyInPlace, Skipped, Overridden, Failed, Waiting, Blocked }

/// <summary>One grid row: a plan entry with its text already worked out.</summary>
/// <param name="Conflict">The conflict the row's resolution cell edits: the open one, else the entry's last. Null when the entry has none.</param>
internal sealed record ReviewRow(
	EntryKey Key, string Book, string Profile, ReviewOutcome Outcome, string Reason, string Action, string From, string To, Conflict? Conflict)
{
	public string OutcomeText => Reason.Length == 0 ? ReviewRows.Label(Outcome) : $"{ReviewRows.Label(Outcome)}: {Reason}";

	public string Resolution => Conflict is null ? "" : ReviewRows.ResolutionText(Conflict);

	/// <summary>Lower-cased text the filter box searches.</summary>
	public string SearchText { get; } = string.Join("\n", Book, Profile, ReviewRows.Label(Outcome), Reason, Action, From, To).ToLowerInvariant();
}

/// <summary>The outcomes to show, and text a row must contain. No outcomes selected shows every outcome.</summary>
internal sealed record ReviewFilter(ISet<ReviewOutcome> Outcomes, string Text)
{
	public static ReviewFilter All { get; } = new(new HashSet<ReviewOutcome>(), "");

	public bool Accepts(ReviewRow row)
		=> (Outcomes.Count == 0 || Outcomes.Contains(row.Outcome))
			&& (Text.Length == 0 || row.SearchText.Contains(Text.ToLowerInvariant()));
}

internal static class ReviewRows
{
	public static IReadOnlyList<ReviewRow> Build(Plan plan)
	{
		var conflicts = new Dictionary<EntryKey, Conflict>();
		foreach (Conflict conflict in plan.Conflicts)
		{
			conflicts[conflict.Key.Entry] = conflict;
		}
		var rows = new List<ReviewRow>(plan.Entries.Count);
		foreach (PlanEntry entry in plan.Entries)
		{
			conflicts.TryGetValue(entry.Key, out Conflict? conflict);
			rows.Add(Row(entry, conflict, plan.Conflicts));
		}
		return rows;
	}

	public static IReadOnlyDictionary<ReviewOutcome, int> Counts(IEnumerable<ReviewRow> rows)
	{
		Dictionary<ReviewOutcome, int> counts = Enum.GetValues(typeof(ReviewOutcome)).Cast<ReviewOutcome>().ToDictionary(outcome => outcome, _ => 0);
		foreach (ReviewRow row in rows)
		{
			counts[row.Outcome]++;
		}
		return counts;
	}

	/// <summary>Indexes into <paramref name="rows"/>, in order.</summary>
	public static int[] Filter(IReadOnlyList<ReviewRow> rows, ReviewFilter filter)
	{
		var shown = new List<int>(rows.Count);
		for (int i = 0; i < rows.Count; i++)
		{
			if (filter.Accepts(rows[i]))
			{
				shown.Add(i);
			}
		}
		return [.. shown];
	}

	public static string Label(ReviewOutcome outcome) => outcome switch
	{
		ReviewOutcome.Planned => "Planned",
		ReviewOutcome.AlreadyInPlace => "Already in place",
		ReviewOutcome.Skipped => "Skipped",
		ReviewOutcome.Overridden => "Overridden",
		ReviewOutcome.Failed => "Failed",
		ReviewOutcome.Waiting => "Waiting",
		_ => "Blocked",
	};

	public static string ChoiceLabel(ResolutionChoice choice, Conflict conflict) => choice switch
	{
		ResolutionChoice.Skip => "Skip this book",
		ResolutionChoice.Replace => "Replace the existing file",
		ResolutionChoice.KeepBoth => conflict.KeepBothPath is { } path ? $"Keep both, as {path.Name}" : "Keep both",
		_ => "Use another path...",
	};

	public static string ResolutionText(Conflict conflict) => conflict.Chosen switch
	{
		null => "Choose...",
		Resolution.Skip => "Skip",
		Resolution.Replace => "Replace",
		Resolution.KeepBoth => conflict.KeepBothPath is { } path ? $"Keep both: {path.Name}" : "Keep both",
		Resolution.UsePath typed => $"Use: {typed.Path}",
		_ => "",
	};

	private static ReviewRow Row(PlanEntry entry, Conflict? conflict, IReadOnlyList<Conflict> all)
	{
		string profile = entry.ProfileName ?? "";
		ReviewRow Make(ReviewOutcome outcome, string reason, IReadOnlyList<Operation> operations)
		{
			(string action, string from, string to) = Describe(operations);
			return new ReviewRow(entry.Key, entry.BookCaption, profile, outcome, reason, action, from, to, conflict);
		}

		return entry.Outcome switch
		{
			EntryOutcome.Planned planned => Make(ReviewOutcome.Planned, "", planned.Operations),
			EntryOutcome.PlannedAsFailed failed => Make(ReviewOutcome.Failed, failed.Reason, failed.Operations),
			EntryOutcome.AlreadyInPlace => Make(ReviewOutcome.AlreadyInPlace, "", []),
			EntryOutcome.Skipped skipped => Make(ReviewOutcome.Skipped, skipped.Reason, []),
			EntryOutcome.Overridden overridden => Make(ReviewOutcome.Overridden, $"by {overridden.ByProfile}", []),
			EntryOutcome.Failed failed => Make(ReviewOutcome.Failed, failed.Reason, []),
			EntryOutcome.WaitingForAnswer waiting => Make(ReviewOutcome.Waiting, WaitingReason(waiting), []),
			EntryOutcome.Blocked blocked => Make(
				ReviewOutcome.Blocked,
				all.FirstOrDefault(candidate => candidate.Key == blocked.Conflict) is { } blocking ? blocking.Kind.Describe() : "A conflict is open",
				[blocked.Pending]),
			_ => Make(ReviewOutcome.Failed, "Unknown outcome", []),
		};
	}

	private static string WaitingReason(EntryOutcome.WaitingForAnswer waiting)
		=> "needs a choice for " + string.Join(", ", waiting.Questions.Select(key => key.Field.DisplayName).Distinct());

	/// <summary>The action, source and target of an entry's operations. A replace reads "Replace, then move".</summary>
	private static (string Action, string From, string To) Describe(IReadOnlyList<Operation> operations)
	{
		bool replaces = operations.OfType<Operation.RecycleFile>().Any();
		Operation? main = operations.FirstOrDefault(operation => operation is not Operation.RecycleFile);
		(string action, string from, string to) = main switch
		{
			Operation.MoveFile move => ("Move", move.From.Value, move.To.Value),
			Operation.RenameCase rename => ("Rename (case)", rename.From.Value, rename.To.Value),
			Operation.CopyFile copy => (copy.AddToLibrary ? "Copy and add to library" : "Copy", copy.From.Value, copy.To.Value),
			Operation.WriteCover cover => ("Write cover", "", cover.To.Value),
			_ => ("", "", ""),
		};
		return (replaces ? "Replace, then " + action.ToLowerInvariant() : action, from, to);
	}
}

/// <summary>Turns the user's choices into <see cref="Decisions"/>. The form never edits a plan.</summary>
internal static class ReviewDecisions
{
	public static Decisions Resolve(Decisions decisions, Conflict conflict, ResolutionChoice choice, AbsPath? typedPath = null)
	{
		if (!conflict.Allowed.Contains(choice))
		{
			throw new ArgumentException($"{choice} is not allowed for this conflict.", nameof(choice));
		}
		Resolution resolution = choice switch
		{
			ResolutionChoice.Skip => new Resolution.Skip(),
			ResolutionChoice.Replace => new Resolution.Replace(),
			ResolutionChoice.KeepBoth => new Resolution.KeepBoth(),
			_ => new Resolution.UsePath(typedPath ?? throw new ArgumentException("A path is needed.", nameof(typedPath))),
		};
		return decisions.WithResolution(conflict.Key, resolution);
	}

	public static Decisions Default(Decisions decisions, ConflictDefault choice) => decisions.WithDefault(choice.ToResolution());

	public static ConflictDefault DefaultOf(Decisions decisions) => decisions.Default switch
	{
		Resolution.Skip => ConflictDefault.Skip,
		Resolution.Replace => ConflictDefault.Replace,
		Resolution.KeepBoth => ConflictDefault.KeepBoth,
		_ => ConflictDefault.Ask,
	};
}

/// <summary>What the "use another path" editor accepts: the checks 2.x made with <c>FileInfo</c>, with the plan's length limit.</summary>
internal static class PathCheck
{
	private const string Forbidden = "<>|*?\"";
	private const int FolderLimit = 248;

	/// <summary>Null when the path is acceptable.</summary>
	public static string? Validate(string text, int maxLength, out AbsPath? path)
	{
		path = null;
		string trimmed = text.Trim();
		if (trimmed.Length == 0)
		{
			return "Type the full path of the file.";
		}
		if (trimmed.IndexOfAny(Forbidden.ToCharArray()) >= 0)
		{
			return "The path cannot contain < > | * ? or \".";
		}
		if (trimmed.IndexOf(':', trimmed.Length > 1 && trimmed[1] == ':' ? 2 : 0) >= 0)
		{
			return "The path cannot contain : after the drive letter.";
		}
		if (AbsPath.TryParse(trimmed) is not { } absolute || absolute.IsRoot)
		{
			return "The path must be a full path to a file, such as D:\\Comics\\Series\\Book.cbz.";
		}
		if (absolute.Value.Length > maxLength)
		{
			return $"The path has {absolute.Value.Length} characters, and the limit is {maxLength}.";
		}
		if (absolute.Parent.Value.Length >= FolderLimit)
		{
			return $"The folder part has {absolute.Parent.Value.Length} characters, and the limit is {FolderLimit - 1}.";
		}
		path = absolute;
		return null;
	}
}
