using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibraryOrganizer.Tests;

/// <summary>An in-memory disk with the same never-overwrite rules as WindowsFileSystem. Paths are given as "D:\x\y.cbz".</summary>
internal sealed class MemoryFileSystem : IFileSystem
{
	private readonly Dictionary<AbsPath, (string Name, byte[] Content)> files = [];
	private readonly Dictionary<AbsPath, string> folders = [];

	public List<AbsPath> Recycled { get; } = [];

	public static AbsPath P(string path) => AbsPath.TryParse(path) ?? throw new ArgumentException(path);

	public MemoryFileSystem With(params string[] paths)
	{
		foreach (string path in paths)
		{
			AddFile(P(path), [1]);
		}
		return this;
	}

	public IReadOnlyList<string> AllFiles => [.. files.Keys.Select(path => Spelled(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

	public bool FileExists(AbsPath path) => files.ContainsKey(path);

	public bool FolderExists(AbsPath path) => folders.ContainsKey(path) || path.IsRoot;

	public string? ActualName(AbsPath path)
		=> files.TryGetValue(path, out var file) ? file.Name : folders.TryGetValue(path, out string? name) ? name : null;

	public long? FileSize(AbsPath path) => files.TryGetValue(path, out var file) ? file.Content.Length : null;

	public IReadOnlyList<AbsPath> Children(AbsPath folder)
		=> [.. files.Keys.Concat(folders.Keys).Where(path => !path.IsRoot && path.Parent.Equals(folder))];

	public void CreateFolder(AbsPath folder)
	{
		for (AbsPath current = folder; !current.IsRoot; current = current.Parent)
		{
			if (!folders.ContainsKey(current))
			{
				folders[current] = current.Name;
			}
		}
	}

	public void Move(AbsPath from, AbsPath to)
	{
		var file = files.TryGetValue(from, out var found) ? found : throw new FileNotFoundException(from.Value);
		RequireFolder(to);
		if (files.ContainsKey(to) && !to.Equals(from))
		{
			throw new IOException($"The file '{to}' already exists.");
		}
		files.Remove(from);
		files[to] = (to.Name, file.Content);
	}

	public void Copy(AbsPath from, AbsPath to)
	{
		var file = files.TryGetValue(from, out var found) ? found : throw new FileNotFoundException(from.Value);
		WriteNew(to, file.Content);
	}

	public void WriteNew(AbsPath to, byte[] content)
	{
		RequireFolder(to);
		if (files.ContainsKey(to))
		{
			throw new IOException($"The file '{to}' already exists.");
		}
		files[to] = (to.Name, content);
	}

	public void Recycle(AbsPath file)
	{
		if (!files.Remove(file))
		{
			throw new FileNotFoundException(file.Value);
		}
		Recycled.Add(file);
	}

	public void DeleteEmptyFolder(AbsPath folder)
	{
		if (Children(folder).Count == 0)
		{
			folders.Remove(folder);
		}
	}

	private void AddFile(AbsPath path, byte[] content)
	{
		CreateFolder(path.Parent);
		files[path] = (path.Name, content);
	}

	private void RequireFolder(AbsPath file)
	{
		if (!FolderExists(file.Parent))
		{
			throw new DirectoryNotFoundException(file.Parent.Value);
		}
	}

	private string Spelled(AbsPath path) => path.Parent.Value.TrimEnd('\\') + "\\" + files[path].Name;
}
