using System;
using System.Collections.Generic;

namespace LibraryOrganizer;

/// <summary>
/// The disk as it would be with some operations applied or taken back, without touching it. Only which files exist is
/// tracked.
/// </summary>
/// <remarks>
/// A plan can fill a path an earlier operation vacated (B moves to C, then A moves to B), or recycle a file and move
/// another into its place. After such a run, "the source is gone" is false for the earlier operation when asked of the
/// real disk. Walking the operations latest first and taking each one back here gives every operation the disk as it
/// was right after it ran.
/// </remarks>
internal sealed class DiskOverlay : IFileProbe
{
	private readonly IFileProbe disk;
	private readonly Func<OpId, OpMark> mark;
	private readonly HashSet<OpId> behindPendingRecycle = [];
	private readonly Dictionary<AbsPath, bool> files = [];

	/// <param name="run">The run whose operations <see cref="Holds"/> is asked about, in execution order.</param>
	/// <param name="mark">What the run's journal says about each operation.</param>
	public DiskOverlay(IFileProbe disk, IReadOnlyList<Operation> run, Func<OpId, OpMark> mark)
	{
		this.disk = disk;
		this.mark = mark;
		var pending = new HashSet<AbsPath>();
		foreach (Operation operation in run)
		{
			if (operation is Operation.RecycleFile recycle && !mark(recycle.Id).File)
			{
				pending.Add(recycle.Path);
			}
			else if (Fills(operation) is { } path && pending.Contains(path))
			{
				behindPendingRecycle.Add(operation.Id);
			}
		}
	}

	public bool FileExists(AbsPath path) => files.TryGetValue(path, out bool exists) ? exists : disk.FileExists(path);

	public bool FolderExists(AbsPath path) => disk.FolderExists(path);

	public string? ActualName(AbsPath path) => disk.ActualName(path);

	/// <summary>Unknown for a path an operation here has filled or vacated: the real file there is another one.</summary>
	public long? FileSize(AbsPath path) => files.ContainsKey(path) ? null : disk.FileSize(path);

	public IReadOnlyList<AbsPath> Children(AbsPath folder) => disk.Children(folder);

	/// <summary>
	/// True when the operation's end state holds. A move also counts when its target is there and the journal says the
	/// file was moved, although something has since appeared at the source: without the mark that state cannot be told
	/// from a move that never ran because its target was taken.
	/// </summary>
	/// <remarks>
	/// Never true for an operation that fills a path an earlier recycle of the run frees, while that recycle has no
	/// file mark. The executor runs in order and never overwrites, so the operation has not run, and the file at the
	/// path is the one the recycle is to remove.
	/// </remarks>
	public bool Holds(Operation operation)
		=> !behindPendingRecycle.Contains(operation.Id)
			&& (operation.IsSatisfied(this) || operation is Operation.MoveFile move && mark(move.Id).File && FileExists(move.To));

	public void Apply(Operation operation)
	{
		switch (operation)
		{
			case Operation.MoveFile move:
				files[move.From] = false;
				files[move.To] = true;
				break;
			case Operation.CopyFile copy:
				files[copy.To] = true;
				break;
			case Operation.WriteCover cover:
				files[cover.To] = true;
				break;
			case Operation.RecycleFile recycle:
				files[recycle.Path] = false;
				break;
		}
	}

	public void TakeBack(Operation operation)
	{
		switch (operation)
		{
			case Operation.MoveFile move:
				files[move.To] = false;
				files[move.From] = true;
				break;
			case Operation.CopyFile copy:
				files[copy.To] = false;
				break;
			case Operation.WriteCover cover:
				files[cover.To] = false;
				break;
			case Operation.RecycleFile recycle:
				files[recycle.Path] = true;
				break;
		}
	}

	/// <summary>The operations of a run whose end state holds on the disk now.</summary>
	public static HashSet<OpId> Held(IReadOnlyList<Operation> operations, Func<OpId, OpMark> mark, IFileProbe disk)
	{
		var overlay = new DiskOverlay(disk, operations, mark);
		var held = new HashSet<OpId>();
		for (int i = operations.Count - 1; i >= 0; i--)
		{
			if (overlay.Holds(operations[i]))
			{
				held.Add(operations[i].Id);
				overlay.TakeBack(operations[i]);
			}
		}
		return held;
	}

	private static AbsPath? Fills(Operation operation) => operation switch
	{
		Operation.MoveFile move => move.To,
		Operation.CopyFile copy => copy.To,
		Operation.WriteCover cover => cover.To,
		_ => null,
	};
}
