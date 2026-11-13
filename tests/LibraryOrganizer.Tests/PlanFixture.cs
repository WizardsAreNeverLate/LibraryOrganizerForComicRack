using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibraryOrganizer.Tests;

internal static class TestProfiles
{
	public const string Folder = @"{<publisher>}\{<series>}";
	public const string FileName = "{<series>}{ #<number>}";

	/// <summary>A Move profile that renders "base\publisher\series\series #number.ext" unless <paramref name="edit"/> changes it.</summary>
	public static Profile Make(string name = "Main", string baseFolder = @"D:\Comics", Func<ProfileSettings, ProfileSettings>? edit = null)
	{
		var settings = new ProfileSettings { Name = name, BaseFolder = baseFolder, FolderTemplate = Folder, FileTemplate = FileName, PruneEmptyFolders = false };
		CompileResult result = (edit?.Invoke(settings) ?? settings).Compile();
		return result.Profile
			?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => $"{error.Where}: {error.Message}")));
	}

	public static Profile Copy(string name, string baseFolder, bool addToLibrary = false)
		=> Make(name, baseFolder, settings => settings with { Action = new ProfileAction.Copy(addToLibrary) });

	/// <summary>Renames in place: keeps the folder, renders "series #number.ext".</summary>
	public static Profile Rename(string name = "Rename") => Make(name, edit: settings => settings with { UseFolder = false });
}

internal sealed class Shelf
{
	private readonly Dictionary<AbsPath, BookId> libraryFiles = [];

	public MemoryFileSystem Disk { get; } = new();
	public List<BookRecord> Selection { get; } = [];
	public int MaxPathLength { get; set; } = 259;

	/// <summary>A selected library book whose file is on the disk.</summary>
	public BookRecord Add(string path, string series = "X-Men", string? number = "7", string publisher = "Marvel", string? writer = null, int? year = null)
	{
		BookRecord book = TestBooks.Book(series: series, number: number, publisher: publisher, writer: writer, year: year, path: path);
		Disk.With(path);
		libraryFiles[MemoryFileSystem.P(path)] = book.Id;
		Selection.Add(book);
		return book;
	}

	/// <summary>A selected book whose file is not on the disk.</summary>
	public BookRecord AddMissing(string path)
	{
		BookRecord book = TestBooks.Book(path: path, number: "7");
		Selection.Add(book);
		return book;
	}

	public BookRecord AddFileless(bool hasCustomCover)
	{
		BookRecord template = TestBooks.Book(number: "7");
		var book = new BookRecord(
			template.Id, new BookLocation.Fileless(hasCustomCover), template.Caption, Fields.All.Select(field => template[field]).ToArray(), new Dictionary<string, string>());
		Selection.Add(book);
		return book;
	}

	/// <summary>A file on the disk that is not selected. With <paramref name="inLibrary"/> it belongs to a library book.</summary>
	public BookId? Occupy(string path, bool inLibrary = false)
	{
		Disk.With(path);
		if (!inLibrary)
		{
			return null;
		}
		var id = new BookId(Guid.NewGuid());
		libraryFiles[MemoryFileSystem.P(path)] = id;
		return id;
	}

	public Plan Plan(Decisions decisions, params Profile[] runOrder)
		=> Organizer.Plan(new OrganizeInput(runOrder, Selection, SeriesIndex.Build(Selection), Disk, libraryFiles, MaxPathLength), decisions);

	public Plan Plan(params Profile[] runOrder) => Plan(Decisions.None, runOrder);

	/// <summary>
	/// Runs the file part of each operation in order on the disk, which never overwrites and fails on a missing source.
	/// Returns every file afterwards, so a test can check that the order the plan gives is one that works.
	/// </summary>
	public IReadOnlyList<string> Execute(ApprovedPlan plan)
	{
		foreach (Operation operation in plan.Operations)
		{
			switch (operation)
			{
				case Operation.MoveFile move:
					Disk.CreateFolder(move.To.Parent);
					Disk.Move(move.From, move.To);
					break;
				case Operation.RenameCase rename:
					Disk.Move(rename.From, rename.To);
					break;
				case Operation.CopyFile copy:
					Disk.CreateFolder(copy.To.Parent);
					Disk.Copy(copy.From, copy.To);
					break;
				case Operation.WriteCover cover:
					Disk.CreateFolder(cover.To.Parent);
					Disk.WriteNew(cover.To, [2]);
					break;
				case Operation.RecycleFile recycle:
					Disk.Recycle(recycle.Path);
					break;
				case Operation.PruneFolders:
					break;
				default:
					throw new InvalidDataException(operation.ToString());
			}
		}
		return Disk.AllFiles;
	}
}

internal static class PlanExtensions
{
	/// <summary>One line per operation in execution order, with full paths.</summary>
	public static string Script(this IEnumerable<Operation> operations) => string.Join("\n", operations.Select(operation => operation switch
	{
		Operation.MoveFile move => $"move {move.From} -> {move.To}",
		Operation.RenameCase rename => $"case {rename.From} -> {rename.To}",
		Operation.CopyFile copy => $"copy {copy.From} -> {copy.To}{(copy.AddToLibrary ? " +library" : "")}",
		Operation.WriteCover cover => $"cover {cover.To} as {cover.Format}",
		Operation.RecycleFile recycle => $"recycle {recycle.Path}",
		Operation.PruneFolders prune => $"prune {string.Join("; ", prune.Candidates)}",
		_ => throw new InvalidDataException(operation.ToString()),
	}));

	public static EntryOutcome OutcomeOf(this Plan plan, BookRecord book, Profile profile)
		=> plan.Entries.Single(entry => entry.Key == new EntryKey(book.Id, profile.Id)).Outcome;

	public static IReadOnlyList<Operation> OperationsOf(this Plan plan, BookRecord book, Profile profile)
		=> plan.OutcomeOf(book, profile) switch
		{
			EntryOutcome.Planned planned => planned.Operations,
			EntryOutcome.PlannedAsFailed failed => failed.Operations,
			EntryOutcome other => throw new InvalidOperationException($"Not planned: {other}"),
		};

	/// <summary>The path the book's file ends at under the profile.</summary>
	public static string TargetOf(this Plan plan, BookRecord book, Profile profile)
		=> plan.OperationsOf(book, profile).Select(operation => operation switch
		{
			Operation.MoveFile move => move.To.Value,
			Operation.RenameCase rename => rename.To.Value,
			Operation.CopyFile copy => copy.To.Value,
			Operation.WriteCover cover => cover.To.Value,
			_ => null,
		}).OfType<string>().Single();

	public static Conflict ConflictOf(this Plan plan, BookRecord book, Profile profile)
		=> plan.Conflicts.Single(conflict => conflict.Key.Entry == new EntryKey(book.Id, profile.Id));
}
