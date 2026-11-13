using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LibraryOrganizer;

public sealed record SettingsLoad(Settings Settings, IReadOnlyList<string> Warnings);

/// <summary>
/// settings.xml in the data folder. Single owner of the file: load, save atomically (temp file + File.Replace).
/// Root: &lt;LibraryOrganizer schema="1"&gt;. Rule fields are stored by <see cref="Field.Key"/>.
/// </summary>
public sealed class SettingsStore
{
	public const int SchemaVersion = 1;

	private readonly string dataFolder;

	public SettingsStore(string dataFolder)
	{
		this.dataFolder = dataFolder;
		Path = System.IO.Path.Combine(dataFolder, "settings.xml");
	}

	public string Path { get; }

	/// <summary>
	/// Missing file: one "Default" profile with the 2.x default templates. A file that cannot be read is renamed to
	/// settings.xml.corrupt-&lt;timestamp&gt; and reported as a warning, so the next save never overwrites it.
	/// </summary>
	public SettingsLoad Load()
	{
		if (!File.Exists(Path))
		{
			return new SettingsLoad(Settings.CreateDefault(), []);
		}
		var warnings = new List<string>();
		try
		{
			XDocument document = XDocument.Load(Path, LoadOptions.PreserveWhitespace);
			XElement? root = document.Root;
			if (root is null || root.Name != "LibraryOrganizer")
			{
				throw new InvalidDataException("The root element is not <LibraryOrganizer>.");
			}
			if (!int.TryParse((string?)root.Attribute("schema"), NumberStyles.None, CultureInfo.InvariantCulture, out int schema))
			{
				throw new InvalidDataException("The root element has no valid schema attribute.");
			}
			if (schema > SchemaVersion)
			{
				throw new InvalidDataException($"The file uses schema {schema}, which is newer than this plugin understands ({SchemaVersion}).");
			}
			Settings settings = SettingsXml.Read(root, warnings);
			return new SettingsLoad(settings, warnings);
		}
		catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException or UnauthorizedAccessException)
		{
			string? aside = MoveAside();
			string where = aside is null ? $"It could not be moved aside, so the next save will overwrite {Path}." : $"The file was kept as {aside}.";
			warnings.Add($"Settings could not be read ({ex.Message}). Starting with a default profile. {where}");
			return new SettingsLoad(Settings.CreateDefault(), warnings);
		}
	}

	public void Save(Settings settings)
	{
		Directory.CreateDirectory(dataFolder);
		WriteAtomically(Path, SettingsXml.Write(settings));
	}

	/// <summary>Writes the profiles to a file the user chose, in the settings.xml schema, so it can be imported on another machine.</summary>
	public static void Export(string file, IReadOnlyList<ProfileSettings> profiles)
		=> WriteAtomically(file, SettingsXml.Write(new Settings(profiles, [], RunPreferences.Default)));

	/// <summary>
	/// Reads an export or settings.xml (root &lt;LibraryOrganizer&gt;) or a 2.x/1.7 profile file, chosen by the root element.
	/// Every imported profile gets a new <see cref="ProfileId"/>. A file that cannot be read gives no profiles and a warning.
	/// </summary>
	public static ImportResult ImportFile(string file)
	{
		XDocument document;
		try
		{
			document = XDocument.Load(file);
		}
		catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
		{
			return new ImportResult([], [], [$"{System.IO.Path.GetFileName(file)} could not be read: {ex.Message}"]);
		}
		if (document.Root?.Name != "LibraryOrganizer")
		{
			return LegacyImport.Read(document);
		}

		var warnings = new List<string>();
		if (!int.TryParse((string?)document.Root.Attribute("schema"), NumberStyles.None, CultureInfo.InvariantCulture, out int schema))
		{
			return new ImportResult([], [], ["The file has no valid schema attribute, so it is not a Library Organizer export."]);
		}
		if (schema > SchemaVersion)
		{
			return new ImportResult([], [], [$"The file uses schema {schema}, which is newer than this plugin understands ({SchemaVersion}). Update the plugin to import it."]);
		}
		Settings settings = SettingsXml.Read(document.Root, warnings);
		var newIds = settings.Profiles.ToDictionary(profile => profile.Id, _ => new ProfileId(Guid.NewGuid()));
		return new ImportResult(
			[.. settings.Profiles.Select(profile => profile with { Id = newIds[profile.Id] })],
			[.. settings.LastRunOrder.Select(id => newIds[id])],
			warnings);
	}

	private static void WriteAtomically(string path, XDocument document)
	{
		string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			var writerSettings = new XmlWriterSettings
			{
				Indent = true,
				IndentChars = "\t",
				Encoding = new UTF8Encoding(false),
				NewLineHandling = NewLineHandling.Entitize,
			};
			using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (XmlWriter writer = XmlWriter.Create(stream, writerSettings))
			{
				document.Save(writer);
			}
			if (File.Exists(path))
			{
				File.Replace(temp, path, null);
			}
			else
			{
				File.Move(temp, path);
			}
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
		}
	}

	private string? MoveAside()
	{
		string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		string target = Path + ".corrupt-" + stamp;
		for (int n = 2; File.Exists(target); n++)
		{
			target = $"{Path}.corrupt-{stamp}-{n}";
		}
		try
		{
			File.Move(Path, target);
			return target;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}

internal static class SettingsXml
{
	public static XDocument Write(Settings settings)
		=> new(
			new XElement(
				"LibraryOrganizer",
				new XAttribute("schema", SettingsStore.SchemaVersion),
				new XElement(
					"Preferences",
					new XAttribute("defaultResolution", settings.Preferences.DefaultResolution)),
				new XElement("LastRunOrder", settings.LastRunOrder.Select(id => new XElement("Profile", new XAttribute("id", id.Value)))),
				new XElement("Profiles", settings.Profiles.Select(WriteProfile))));

	public static Settings Read(XElement root, List<string> warnings)
	{
		XElement? preferences = root.Element("Preferences");
		RunPreferences defaults = RunPreferences.Default;
		RunPreferences runPreferences = preferences is null
			? defaults
			: new RunPreferences(
				ReadEnum(preferences.Attribute("defaultResolution"), defaults.DefaultResolution, "Preferences", warnings));

		var profiles = new List<ProfileSettings>();
		var seen = new HashSet<ProfileId>();
		foreach (XElement element in root.Element("Profiles")?.Elements("Profile") ?? [])
		{
			ProfileSettings profile = ReadProfile(element, warnings);
			if (!seen.Add(profile.Id))
			{
				profile = profile with { Id = new ProfileId(Guid.NewGuid()) };
				warnings.Add($"Profile '{profile.Name}' repeated another profile's id and was given a new one.");
				seen.Add(profile.Id);
			}
			profiles.Add(profile);
		}
		if (profiles.Count == 0)
		{
			profiles.Add(new ProfileSettings { Name = "Default" });
		}

		var lastRun = new List<ProfileId>();
		foreach (XElement element in root.Element("LastRunOrder")?.Elements("Profile") ?? [])
		{
			if (Guid.TryParse((string?)element.Attribute("id"), out Guid id) && seen.Contains(new ProfileId(id)))
			{
				lastRun.Add(new ProfileId(id));
			}
		}
		return new Settings(profiles, lastRun, runPreferences);
	}

	private static XElement WriteProfile(ProfileSettings p)
		=> new(
			"Profile",
			new XAttribute("id", p.Id.Value),
			new XElement("Name", p.Name),
			p.Action switch
			{
				ProfileAction.Copy copy => new XElement("Action", new XAttribute("kind", "Copy"), new XAttribute("addToLibrary", Bool(copy.AddToLibrary))),
				_ => new XElement("Action", new XAttribute("kind", "Move")),
			},
			new XElement("FolderTemplate", p.FolderTemplate),
			new XElement("FileTemplate", p.FileTemplate),
			new XElement("UseFolder", Bool(p.UseFolder)),
			new XElement("UseFileName", Bool(p.UseFileName)),
			new XElement("BaseFolder", p.BaseFolder),
			new XElement("EmptyFolderName", p.EmptyFolderName),
			WriteMap("EmptySubstitutes", p.EmptySubstitutes),
			new XElement("CollapseSpaces", Bool(p.CollapseSpaces)),
			new XElement("IllegalCharacters", p.IllegalCharacters.Select(pair => new XElement("Item", new XAttribute("char", pair.Key), pair.Value))),
			new XElement("MonthNames", p.MonthNames.Select(name => new XElement("Month", name))),
			new XElement("ExcludedFolders", p.ExcludedFolders.Select(folder => new XElement("Folder", folder))),
			new XElement("Rules", new XAttribute("mode", p.RuleMode), new XAttribute("combinator", p.Rules.Combinator), p.Rules.Children.Select(WriteRule)),
			new XElement("RequiredFields", p.RequiredFields.Select(key => new XElement("Field", key))),
			new XElement("FailedBooksFolder", p.FailedBooksFolder),
			new XElement("FilelessCovers", p.FilelessCovers?.ToString() ?? "None"),
			new XElement("PruneEmptyFolders", Bool(p.PruneEmptyFolders)),
			new XElement("KeepFolders", p.KeepFolders.Select(folder => new XElement("Folder", folder))),
			new XElement("DontAskWhenOneValue", Bool(p.DontAskWhenOneValue)),
			new XElement("KeepReadProgressOnReplace", Bool(p.KeepReadProgressOnReplace)),
			new XElement("AutoSpaceFields", Bool(p.AutoSpaceFields)),
			WriteMap("LastPrefix", p.LastPrefix),
			WriteMap("LastPostfix", p.LastPostfix),
			WriteMap("LastSeparator", p.LastSeparator));

	private static XElement WriteRule(RuleSettings rule)
		=> rule switch
		{
			RuleSettings.Group group => new XElement("Group", new XAttribute("combinator", group.Combinator), group.Children.Select(WriteRule)),
			RuleSettings.Condition c => new XElement("Condition", new XAttribute("field", c.FieldKey), new XAttribute("op", c.Operator), new XAttribute("value", c.RawOperand)),
			_ => throw new ArgumentOutOfRangeException(nameof(rule)),
		};

	private static XElement WriteMap(string name, IReadOnlyDictionary<string, string> map)
		=> new(name, map.Select(pair => new XElement("Item", new XAttribute("key", pair.Key), pair.Value)));

	private static string Bool(bool value) => value ? "true" : "false";

	private static ProfileSettings ReadProfile(XElement e, List<string> warnings)
	{
		var defaults = new ProfileSettings();
		string name = Text(e, "Name", defaults.Name);
		void Warn(string message) => warnings.Add($"Profile '{name}': {message}");

		ProfileId id;
		if (Guid.TryParse((string?)e.Attribute("id"), out Guid parsedId))
		{
			id = new ProfileId(parsedId);
		}
		else
		{
			id = new ProfileId(Guid.NewGuid());
			Warn("has no valid id, so it was given a new one.");
		}

		ProfileAction action = defaults.Action;
		if (e.Element("Action") is { } actionElement)
		{
			action = (string?)actionElement.Attribute("kind") switch
			{
				"Copy" => new ProfileAction.Copy(ReadBool((string?)actionElement.Attribute("addToLibrary"), true, "Action", Warn)),
				"Move" => new ProfileAction.Move(),
				var other => Unknown(other),
			};
		}
		ProfileAction Unknown(string? kind)
		{
			Warn($"has the unknown action '{kind}'. It was set to Move.");
			return new ProfileAction.Move();
		}

		var months = e.Element("MonthNames")?.Elements("Month").Select(m => m.Value).ToList();
		if (months is not null && months.Count != 16)
		{
			Warn($"has {months.Count} month names, not 16. The defaults were used.");
			months = null;
		}

		RuleSettings.Group rules = defaults.Rules;
		RuleMode mode = defaults.RuleMode;
		if (e.Element("Rules") is { } rulesElement)
		{
			mode = ReadEnum(rulesElement.Attribute("mode"), mode, "Rules", Warn);
			rules = new RuleSettings.Group(ReadEnum(rulesElement.Attribute("combinator"), Combinator.Any, "Rules", Warn), ReadRules(rulesElement, Warn));
		}

		CoverFormat? covers = defaults.FilelessCovers;
		if (e.Element("FilelessCovers") is { } coversElement)
		{
			string text = coversElement.Value.Trim();
			if (text.Equals("None", StringComparison.OrdinalIgnoreCase))
			{
				covers = null;
			}
			else if (Enum.TryParse(text, true, out CoverFormat format) && Enum.IsDefined(typeof(CoverFormat), format))
			{
				covers = format;
			}
			else
			{
				Warn($"has the unknown fileless cover format '{text}'. Fileless books are skipped.");
				covers = null;
			}
		}

		return new ProfileSettings
		{
			Id = id,
			Name = name,
			Action = action,
			FolderTemplate = Text(e, "FolderTemplate", defaults.FolderTemplate),
			FileTemplate = Text(e, "FileTemplate", defaults.FileTemplate),
			UseFolder = Flag(e, "UseFolder", defaults.UseFolder, Warn),
			UseFileName = Flag(e, "UseFileName", defaults.UseFileName, Warn),
			BaseFolder = Text(e, "BaseFolder", defaults.BaseFolder),
			EmptyFolderName = Text(e, "EmptyFolderName", defaults.EmptyFolderName),
			EmptySubstitutes = ReadMap(e, "EmptySubstitutes") ?? defaults.EmptySubstitutes,
			CollapseSpaces = Flag(e, "CollapseSpaces", defaults.CollapseSpaces, Warn),
			IllegalCharacters = e.Element("IllegalCharacters")?.Elements("Item")
				.Select(item => new KeyValuePair<string, string>((string?)item.Attribute("char") ?? "", item.Value)).ToList() ?? defaults.IllegalCharacters,
			MonthNames = months ?? defaults.MonthNames,
			ExcludedFolders = ReadList(e, "ExcludedFolders", "Folder") ?? defaults.ExcludedFolders,
			Rules = rules,
			RuleMode = mode,
			RequiredFields = ReadList(e, "RequiredFields", "Field") ?? defaults.RequiredFields,
			FailedBooksFolder = Text(e, "FailedBooksFolder", defaults.FailedBooksFolder),
			FilelessCovers = covers,
			PruneEmptyFolders = Flag(e, "PruneEmptyFolders", defaults.PruneEmptyFolders, Warn),
			KeepFolders = ReadList(e, "KeepFolders", "Folder") ?? defaults.KeepFolders,
			DontAskWhenOneValue = Flag(e, "DontAskWhenOneValue", defaults.DontAskWhenOneValue, Warn),
			KeepReadProgressOnReplace = Flag(e, "KeepReadProgressOnReplace", defaults.KeepReadProgressOnReplace, Warn),
			AutoSpaceFields = Flag(e, "AutoSpaceFields", defaults.AutoSpaceFields, Warn),
			LastPrefix = ReadMap(e, "LastPrefix") ?? defaults.LastPrefix,
			LastPostfix = ReadMap(e, "LastPostfix") ?? defaults.LastPostfix,
			LastSeparator = ReadMap(e, "LastSeparator") ?? defaults.LastSeparator,
		};
	}

	private static List<RuleSettings> ReadRules(XElement parent, Action<string> warn)
	{
		var children = new List<RuleSettings>();
		foreach (XElement child in parent.Elements())
		{
			if (child.Name == "Group")
			{
				children.Add(new RuleSettings.Group(ReadEnum(child.Attribute("combinator"), Combinator.Any, "Group", warn), ReadRules(child, warn)));
			}
			else if (child.Name == "Condition")
			{
				string? field = (string?)child.Attribute("field");
				string? op = (string?)child.Attribute("op");
				if (field is null || !Enum.TryParse(op, true, out Operator parsed) || !Enum.IsDefined(typeof(Operator), parsed))
				{
					warn($"has a rule with the unknown operator '{op}'. The rule was dropped.");
					continue;
				}
				children.Add(new RuleSettings.Condition(field, parsed, (string?)child.Attribute("value") ?? ""));
			}
		}
		return children;
	}

	private static string Text(XElement parent, string name, string fallback) => parent.Element(name)?.Value ?? fallback;

	private static bool Flag(XElement parent, string name, bool fallback, Action<string> warn)
		=> parent.Element(name) is { } element ? ReadBool(element.Value, fallback, name, warn) : fallback;

	private static bool ReadBool(string? text, bool fallback, string name, Action<string> warn)
	{
		if (text is null)
		{
			return fallback;
		}
		if (bool.TryParse(text.Trim(), out bool value))
		{
			return value;
		}
		warn($"has '{text}' for {name}, which is not true or false. The default was used.");
		return fallback;
	}

	private static T ReadEnum<T>(XAttribute? attribute, T fallback, string where, Action<string> warn) where T : struct, Enum
	{
		if (attribute is null)
		{
			return fallback;
		}
		if (Enum.TryParse(attribute.Value, true, out T value) && Enum.IsDefined(typeof(T), value))
		{
			return value;
		}
		warn($"has '{attribute.Value}' for {where} {attribute.Name}, which is not valid. The default was used.");
		return fallback;
	}

	private static T ReadEnum<T>(XAttribute? attribute, T fallback, string where, List<string> warnings) where T : struct, Enum
		=> ReadEnum(attribute, fallback, where, (Action<string>)warnings.Add);

	private static IReadOnlyList<string>? ReadList(XElement parent, string name, string item)
		=> parent.Element(name)?.Elements(item).Select(element => element.Value).ToList();

	private static IReadOnlyDictionary<string, string>? ReadMap(XElement parent, string name)
	{
		if (parent.Element(name) is not { } element)
		{
			return null;
		}
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (XElement item in element.Elements("Item"))
		{
			if ((string?)item.Attribute("key") is { } key)
			{
				map[key] = item.Value;
			}
		}
		return map;
	}
}
