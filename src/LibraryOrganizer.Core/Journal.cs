using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LibraryOrganizer;

/// <param name="Done">Operations whose file change and library write are both recorded.</param>
/// <param name="Failed">Operations whose last attempt failed.</param>
/// <param name="Finished">The run reached its end, cancelled or not. False for a run that was interrupted.</param>
/// <param name="Profiles">The names of the profiles the run's entries belong to, as they were at the time.</param>
public sealed record RunSummary(
	RunId Id, DateTime Started, PlanKind Kind, int Operations, int Done, int Failed, bool Finished, bool Cancelled, IReadOnlyList<string> Profiles);

/// <summary>What the journal knows about one operation.</summary>
/// <param name="File">The file change was made.</param>
/// <param name="Applied">The library write was made too.</param>
/// <param name="Created">The library book a copy added.</param>
/// <param name="Failed">Why the last attempt failed. Null when it did not.</param>
public readonly record struct OpMark(bool File, bool Applied, BookId? Created, string? Failed);

/// <summary>A run as its journal recorded it: the approved plan, and how far each operation got.</summary>
public sealed record RunJournal(
	RunSummary Summary, IReadOnlyList<PlanEntry> Entries, IReadOnlyList<Operation> Operations, IReadOnlyDictionary<OpId, OpMark> Marks)
{
	public OpMark Mark(OpId operation) => Marks.TryGetValue(operation, out OpMark mark) ? mark : default;
}

/// <summary>
/// The run journals, one file per run in "dataFolder\runs". A journal holds the whole approved plan, written before
/// the run changes anything, then one mark per step. Undo and resume read it.
/// </summary>
/// <remarks>
/// Each line is one XML element with attributes only. A line counts once its line end is on the disk, so a write cut
/// short by a crash is ignored. Only the executor writes, and one run has one writer.
/// </remarks>
public sealed class JournalStore
{
	private const string Extension = ".journal";

	private readonly string folder;
	private readonly int keepRuns;
	private readonly Func<DateTime> clock;

	/// <param name="keepRuns">How many finished runs <see cref="Trim"/> keeps.</param>
	public JournalStore(string dataFolder, int keepRuns)
		: this(dataFolder, keepRuns, () => DateTime.UtcNow)
	{
	}

	internal JournalStore(string dataFolder, int keepRuns, Func<DateTime> clock)
	{
		folder = Path.Combine(dataFolder, "runs");
		this.keepRuns = keepRuns;
		this.clock = clock;
	}

	/// <summary>Every run, newest first.</summary>
	public IReadOnlyList<RunSummary> List() => Load().Select(journal => journal.Summary).ToList();

	/// <summary>Runs with no end line, newest first: a crash or a kill stopped them. <see cref="Executor.Resume"/> finishes one.</summary>
	public IReadOnlyList<RunSummary> Interrupted() => List().Where(summary => !summary.Finished).ToList();

	/// <exception cref="FileNotFoundException">No journal for the run, or one whose plan was never written in full.</exception>
	public RunJournal Read(RunId run)
		=> (PathOf(run) is { } path ? ReadFile(path) : null) ?? throw new FileNotFoundException($"There is no journal for run {run.Value:N}.");

	/// <summary>
	/// Deletes the oldest finished runs beyond the number to keep, and journals whose plan was never written in full
	/// (such a run changed nothing). Never an interrupted run. A journal that cannot be deleted now stays for next time.
	/// </summary>
	public void Trim()
	{
		var finished = new List<string>();
		foreach (string path in Files())
		{
			RunJournal? journal = ReadFile(path);
			if (journal is null)
			{
				Delete(path);
			}
			else if (journal.Summary.Finished)
			{
				finished.Add(path);
			}
		}
		foreach (string path in finished.Skip(Math.Max(keepRuns, 0)))
		{
			Delete(path);
		}
	}

	internal JournalWriter Begin(ApprovedPlan plan)
	{
		Directory.CreateDirectory(folder);
		DateTime started = clock().ToUniversalTime();
		string path = Path.Combine(folder, $"{started.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)}-{plan.Id.Value:N}{Extension}");
		var writer = new JournalWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough));
		try
		{
			writer.Plan(
			[
				JournalFormat.Run(plan.Id, started, plan.Kind, plan.Entries.Count, plan.Operations.Count),
				.. plan.Entries.Select(JournalFormat.Entry),
				.. plan.Operations.Select(JournalFormat.Op),
			]);
			return writer;
		}
		catch
		{
			writer.Dispose();
			throw;
		}
	}

	/// <summary>Opens an interrupted journal to append to it, after cutting off a last line that was not written in full.</summary>
	internal JournalWriter Reopen(RunId run)
	{
		string path = PathOf(run) ?? throw new FileNotFoundException($"There is no journal for run {run.Value:N}.");
		var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
		try
		{
			long end = stream.Length;
			while (end > 0)
			{
				stream.Position = end - 1;
				if (stream.ReadByte() == '\n')
				{
					break;
				}
				end--;
			}
			stream.SetLength(end);
			stream.Position = end;
			return new JournalWriter(stream);
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	private IEnumerable<RunJournal> Load() => Files().Select(ReadFile).OfType<RunJournal>();

	/// <summary>Newest first: a file name starts with the time the run started.</summary>
	private List<string> Files()
		=> Directory.Exists(folder)
			? [.. Directory.EnumerateFiles(folder, "*" + Extension).OrderByDescending(Path.GetFileName, StringComparer.Ordinal)]
			: [];

	private string? PathOf(RunId run)
		=> Directory.Exists(folder) ? Directory.EnumerateFiles(folder, $"*-{run.Value:N}{Extension}").FirstOrDefault() : null;

	private static RunJournal? ReadFile(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return JournalFormat.Parse(reader.ReadToEnd());
	}

	private static void Delete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}
}

/// <summary>Appends to one run's journal. Every mark is on the disk before the call returns.</summary>
internal sealed class JournalWriter : IDisposable
{
	private readonly StreamWriter writer;

	public JournalWriter(FileStream stream) => writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

	/// <summary>One flush for the whole plan: nothing has changed yet, so only the last line has to be safe.</summary>
	public void Plan(IEnumerable<XElement> lines)
	{
		foreach (XElement line in lines)
		{
			Write(line);
		}
		writer.Flush();
	}

	public void File(OpId operation) => Mark(new XElement("file", new XAttribute("op", operation.Value)));

	public void Applied(OpId operation, BookId? created)
		=> Mark(new XElement("applied", new XAttribute("op", operation.Value), created is { } book ? new XAttribute("created", book.Value) : null));

	public void Failed(OpId operation, string reason)
		=> Mark(new XElement("failed", new XAttribute("op", operation.Value), new XAttribute("reason", JournalFormat.Clean(reason))));

	public void End(bool cancelled) => Mark(new XElement("end", new XAttribute("cancelled", cancelled)));

	public void Dispose() => writer.Dispose();

	private void Mark(XElement line)
	{
		Write(line);
		writer.Flush();
	}

	private void Write(XElement line)
	{
		writer.Write(line.ToString(SaveOptions.DisableFormatting));
		writer.Write('\n');
	}
}

/// <summary>The journal's line format, schema 1. A shipped format: change it only with a new schema number.</summary>
internal static class JournalFormat
{
	private const string Schema = "1";
	private const char ListSeparator = '|';

	public static XElement Run(RunId id, DateTime started, PlanKind kind, int entries, int operations)
		=> new("run",
			new XAttribute("schema", Schema),
			new XAttribute("id", id.Value),
			new XAttribute("started", started.ToString("o", CultureInfo.InvariantCulture)),
			new XAttribute("kind", kind is PlanKind.Undo ? "undo" : "organize"),
			kind is PlanKind.Undo undo ? new XAttribute("of", undo.Of.Value) : null,
			new XAttribute("entries", entries),
			new XAttribute("ops", operations));

	public static XElement Entry(PlanEntry entry)
	{
		(string outcome, string? text, IReadOnlyList<Operation> operations) = entry.Outcome switch
		{
			EntryOutcome.Planned planned => ("planned", null, planned.Operations),
			EntryOutcome.PlannedAsFailed failed => ("plannedfailed", failed.Reason, failed.Operations),
			EntryOutcome.AlreadyInPlace => ("inplace", null, []),
			EntryOutcome.Skipped skipped => ("skipped", skipped.Reason, []),
			EntryOutcome.Overridden overridden => ("overridden", overridden.ByProfile, []),
			EntryOutcome.Failed failed => ("failed", failed.Reason, (IReadOnlyList<Operation>)[]),
			_ => throw new ArgumentException($"An approved plan has no {entry.Outcome.GetType().Name} entry.", nameof(entry)),
		};
		return new XElement("entry",
			new XAttribute("book", entry.Key.Book.Value),
			entry.Key.Profile is { } profile ? new XAttribute("profile", profile.Value) : null,
			new XAttribute("caption", Clean(entry.BookCaption)),
			entry.ProfileName is { } name ? new XAttribute("profileName", Clean(name)) : null,
			new XAttribute("outcome", outcome),
			text is null ? null : new XAttribute("text", Clean(text)),
			operations.Count == 0 ? null : new XAttribute("ops", string.Join(" ", operations.Select(operation => operation.Id.Value))));
	}

	public static XElement Op(Operation operation)
	{
		var line = new XElement("op", new XAttribute("id", operation.Id.Value));
		void Add(string name, object? value)
		{
			if (value is not null)
			{
				line.Add(new XAttribute(name, value));
			}
		}
		void AddEntry(string kind)
		{
			Add("kind", kind);
			Add("book", operation.Entry.Book.Value);
			Add("profile", operation.Entry.Profile?.Value);
		}
		switch (operation)
		{
			case Operation.MoveFile move:
				AddEntry("move");
				Add("from", move.From.Value);
				Add("to", move.To.Value);
				break;
			case Operation.RenameCase rename:
				AddEntry("case");
				Add("from", rename.From.Value);
				Add("to", rename.To.Value);
				break;
			case Operation.CopyFile copy:
				AddEntry("copy");
				Add("from", copy.From.Value);
				Add("to", copy.To.Value);
				Add("library", copy.AddToLibrary);
				break;
			case Operation.WriteCover cover:
				AddEntry("cover");
				Add("to", cover.To.Value);
				Add("format", cover.Format.ToString());
				break;
			case Operation.RecycleFile recycle:
				AddEntry("recycle");
				Add("path", recycle.Path.Value);
				Add("existing", recycle.ExistingBook?.Value);
				Add("transfer", recycle.TransferReadProgressTo?.Value);
				break;
			case Operation.PruneFolders prune:
				Add("kind", "prune");
				Add("candidates", Join(prune.Candidates));
				Add("stop", Join(prune.StopAt));
				Add("keep", Join(prune.Keep));
				break;
			default:
				throw new ArgumentException($"Unknown operation {operation.GetType().Name}.", nameof(operation));
		}
		return line;
	}

	/// <summary>Null when the plan part is not there in full: the run stopped before it changed anything.</summary>
	public static RunJournal? Parse(string text)
	{
		string[] pieces = text.Split('\n');
		// The last piece has no line end. It is empty after a whole line, and a write cut short otherwise.
		List<XElement?> lines = pieces.Take(pieces.Length - 1).Select(Element).ToList();
		try
		{
			if (lines.Count == 0 || lines[0] is not { } run || run.Name != "run" || Text(run, "schema") != Schema)
			{
				return null;
			}
			int entryCount = Int(run, "entries");
			int operationCount = Int(run, "ops");
			if (lines.Count < 1 + entryCount + operationCount)
			{
				return null;
			}
			List<Operation> operations = lines.Skip(1 + entryCount).Take(operationCount).Select(line => ParseOperation(Named(line, "op"))).ToList();
			Dictionary<OpId, Operation> byId = operations.ToDictionary(operation => operation.Id);
			List<PlanEntry> entries = lines.Skip(1).Take(entryCount).Select(line => ParseEntry(Named(line, "entry"), byId)).ToList();

			var marks = new Dictionary<OpId, OpMark>();
			bool finished = false;
			bool cancelled = false;
			foreach (XElement line in lines.Skip(1 + entryCount + operationCount).OfType<XElement>())
			{
				try
				{
					if (line.Name == "end")
					{
						finished = true;
						cancelled = Bool(line, "cancelled");
						continue;
					}
					var id = new OpId(Int(line, "op"));
					if (!byId.ContainsKey(id))
					{
						continue;
					}
					marks.TryGetValue(id, out OpMark mark);
					switch (line.Name.LocalName)
					{
						case "file":
							marks[id] = mark with { File = true, Failed = null };
							break;
						case "applied":
							BookId? created = line.Attribute("created") is null ? mark.Created : new BookId(Id(line, "created"));
							marks[id] = mark with { File = true, Applied = true, Created = created, Failed = null };
							break;
						case "failed":
							marks[id] = mark with { Failed = Text(line, "reason") };
							break;
					}
				}
				catch (FormatException)
				{
					// A mark that cannot be read is a mark that was not written: the disk decides what was done.
				}
			}

			PlanKind kind = Text(run, "kind") == "undo" ? new PlanKind.Undo(new RunId(Id(run, "of"))) : new PlanKind.Organize();
			var summary = new RunSummary(
				new RunId(Id(run, "id")),
				DateTime.ParseExact(Text(run, "started"), "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
				kind,
				operations.Count,
				Done: marks.Values.Count(mark => mark.Applied && mark.Failed is null),
				Failed: marks.Values.Count(mark => mark.Failed is not null),
				finished,
				cancelled,
				entries.Select(entry => entry.ProfileName).OfType<string>().Distinct().ToList());
			return new RunJournal(summary, entries, operations, marks);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	/// <summary>Drops the characters XML cannot hold, which a caption or an error message may contain.</summary>
	public static string Clean(string text)
	{
		var clean = new StringBuilder(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			if (XmlConvert.IsXmlChar(text[i]))
			{
				clean.Append(text[i]);
			}
			else if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], text[i]))
			{
				clean.Append(text[i]).Append(text[++i]);
			}
		}
		return clean.ToString();
	}

	private static XElement? Element(string line)
	{
		try
		{
			return XElement.Parse(line);
		}
		catch (XmlException)
		{
			return null;
		}
	}

	private static PlanEntry ParseEntry(XElement line, Dictionary<OpId, Operation> byId)
	{
		List<Operation> operations = (line.Attribute("ops")?.Value ?? "")
			.Split([' '], StringSplitOptions.RemoveEmptyEntries)
			.Select(id => byId.TryGetValue(new OpId(ParseInt(id)), out Operation? operation) ? operation : throw new FormatException($"Unknown operation {id}."))
			.ToList();
		string text = line.Attribute("text")?.Value ?? "";
		EntryOutcome outcome = Text(line, "outcome") switch
		{
			"planned" => new EntryOutcome.Planned(operations),
			"plannedfailed" => new EntryOutcome.PlannedAsFailed(operations, text),
			"inplace" => new EntryOutcome.AlreadyInPlace(),
			"skipped" => new EntryOutcome.Skipped(text),
			"overridden" => new EntryOutcome.Overridden(text),
			"failed" => new EntryOutcome.Failed(text),
			string other => throw new FormatException($"Unknown outcome '{other}'."),
		};
		return new PlanEntry(KeyOf(line), Text(line, "caption"), line.Attribute("profileName")?.Value, outcome);
	}

	private static Operation ParseOperation(XElement line)
	{
		var id = new OpId(Int(line, "id"));
		return Text(line, "kind") switch
		{
			"move" => new Operation.MoveFile(id, KeyOf(line), PathOf(line, "from"), PathOf(line, "to")),
			"case" => new Operation.RenameCase(id, KeyOf(line), PathOf(line, "from"), PathOf(line, "to")),
			"copy" => new Operation.CopyFile(id, KeyOf(line), PathOf(line, "from"), PathOf(line, "to"), Bool(line, "library")),
			"cover" => new Operation.WriteCover(
				id, KeyOf(line), PathOf(line, "to"),
				Enum.TryParse(Text(line, "format"), out CoverFormat format) ? format : throw new FormatException("Unknown cover format.")),
			"recycle" => new Operation.RecycleFile(
				id, KeyOf(line), PathOf(line, "path"),
				line.Attribute("existing") is null ? null : new BookId(Id(line, "existing")),
				line.Attribute("transfer") is null ? null : new BookId(Id(line, "transfer"))),
			"prune" => new Operation.PruneFolders(id, Paths(line, "candidates"), Paths(line, "stop"), Paths(line, "keep")),
			string other => throw new FormatException($"Unknown operation kind '{other}'."),
		};
	}

	private static EntryKey KeyOf(XElement line)
		=> new(new BookId(Id(line, "book")), line.Attribute("profile") is null ? null : new ProfileId(Id(line, "profile")));

	private static XElement Named(XElement? line, string name)
		=> line is not null && line.Name == name ? line : throw new FormatException($"Expected a '{name}' line.");

	private static string Text(XElement line, string name)
		=> line.Attribute(name)?.Value ?? throw new FormatException($"'{line.Name}' has no '{name}'.");

	private static int Int(XElement line, string name) => ParseInt(Text(line, name));

	private static int ParseInt(string text)
		=> int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : throw new FormatException($"'{text}' is not a number.");

	private static bool Bool(XElement line, string name)
		=> bool.TryParse(Text(line, name), out bool value) ? value : throw new FormatException($"'{name}' is not true or false.");

	private static Guid Id(XElement line, string name)
		=> Guid.TryParse(Text(line, name), out Guid value) ? value : throw new FormatException($"'{name}' is not an id.");

	private static AbsPath PathOf(XElement line, string name) => ParsePath(Text(line, name));

	private static AbsPath ParsePath(string text) => AbsPath.TryParse(text) ?? throw new FormatException($"'{text}' is not an absolute path.");

	/// <summary>Windows forbids '|' in a path, so it separates the paths of a list.</summary>
	private static string Join(IReadOnlyList<AbsPath> paths) => string.Join(ListSeparator.ToString(), paths.Select(path => path.Value));

	private static List<AbsPath> Paths(XElement line, string name)
		=> Text(line, name).Split([ListSeparator], StringSplitOptions.RemoveEmptyEntries).Select(ParsePath).ToList();
}
