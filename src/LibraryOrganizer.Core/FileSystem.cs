using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace LibraryOrganizer;

/// <summary>Read-only disk view. The planner and undo planner read through this; tests pass an in-memory tree.</summary>
public interface IFileProbe
{
	bool FileExists(AbsPath path);
	bool FolderExists(AbsPath path);
	/// <summary>Exact on-disk spelling of the last segment, for case-only renames. Null when missing.</summary>
	string? ActualName(AbsPath path);
	long? FileSize(AbsPath path);
	/// <summary>Direct children, files and folders.</summary>
	IReadOnlyList<AbsPath> Children(AbsPath folder);
}

/// <summary>Disk writes. Only the executor calls these.</summary>
public interface IFileSystem : IFileProbe
{
	/// <summary>Creates the folder and its missing parents.</summary>
	void CreateFolder(AbsPath folder);
	/// <summary>Never overwrites.</summary>
	void Move(AbsPath from, AbsPath to);
	/// <summary>Never overwrites. A crash mid-copy leaves only a temporary file, never a partial file at the target.</summary>
	void Copy(AbsPath from, AbsPath to);
	/// <summary>Never overwrites.</summary>
	void WriteNew(AbsPath to, byte[] content);
	void Recycle(AbsPath file);
	/// <summary>Deletes the folder only when it is empty.</summary>
	void DeleteEmptyFolder(AbsPath folder);
}

public sealed class WindowsFileSystem : IFileSystem
{
	private const string PartialSuffix = ".lo-partial";

	public bool FileExists(AbsPath path) => File.Exists(path.Value);

	public bool FolderExists(AbsPath path) => Directory.Exists(path.Value);

	public string? ActualName(AbsPath path)
	{
		if (path.IsRoot || !Directory.Exists(path.Parent.Value))
		{
			return null;
		}
		return Directory.EnumerateFileSystemEntries(path.Parent.Value, path.Name)
			.Select(Path.GetFileName)
			.FirstOrDefault(name => string.Equals(name, path.Name, StringComparison.OrdinalIgnoreCase));
	}

	public long? FileSize(AbsPath path)
	{
		var info = new FileInfo(path.Value);
		return info.Exists ? info.Length : null;
	}

	public IReadOnlyList<AbsPath> Children(AbsPath folder)
		=> Directory.Exists(folder.Value)
			? [.. Directory.EnumerateFileSystemEntries(folder.Value).Select(entry => AbsPath.TryParse(entry)).OfType<AbsPath>()]
			: [];

	public void CreateFolder(AbsPath folder) => Directory.CreateDirectory(folder.Value);

	public void Move(AbsPath from, AbsPath to) => File.Move(from.Value, to.Value);

	public void Copy(AbsPath from, AbsPath to)
	{
		if (File.Exists(to.Value))
		{
			throw new IOException($"The file '{to.Value}' already exists.");
		}
		string partial = to.Value + PartialSuffix;
		File.Copy(from.Value, partial, overwrite: true);
		try
		{
			File.Move(partial, to.Value);
		}
		catch
		{
			File.Delete(partial);
			throw;
		}
	}

	public void WriteNew(AbsPath to, byte[] content)
	{
		using var stream = new FileStream(to.Value, FileMode.CreateNew, FileAccess.Write);
		stream.Write(content, 0, content.Length);
	}

	public void Recycle(AbsPath file)
		=> FileSystem.DeleteFile(file.Value, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);

	public void DeleteEmptyFolder(AbsPath folder)
	{
		if (Directory.Exists(folder.Value) && !Directory.EnumerateFileSystemEntries(folder.Value).Any())
		{
			Directory.Delete(folder.Value);
		}
	}
}
