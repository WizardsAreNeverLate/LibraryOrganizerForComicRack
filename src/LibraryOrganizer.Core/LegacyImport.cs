using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LibraryOrganizer;

public sealed record ImportResult(IReadOnlyList<ProfileSettings> Profiles, IReadOnlyList<ProfileId> LastUsed, IReadOnlyList<string> Warnings);

/// <summary>
/// 2.x and 1.7 import: losettingsx.dat and .lop files (roots Profiles, Profile, Settings, Setting).
/// Pure: XDocument in, profiles and warnings out. Never writes. The caller merges the result and asks about name clashes.
/// Each imported profile gets a new <see cref="ProfileId"/>.
/// </summary>
public static class LegacyImport
{
	public const string SettingsFileName = "losettingsx.dat";

	private static readonly Dictionary<string, string> Version1ControlNames = new(StringComparer.Ordinal)
	{
		["SeriesComplete"] = "Series Complete", ["Read"] = "Read Percentage", ["FirstLetter"] = "First Letter", ["AgeRating"] = "Age Rating",
		["AlternateSeriesMulti"] = "Alternate Series Multi", ["MonthNumber"] = "Month Number", ["AlternateNumber"] = "Alternate Number",
		["StartMonth"] = "Start Month", ["AlternateSeries"] = "Alternate Series", ["ScanInformation"] = "Scan Information", ["StartYear"] = "Start Year",
		["AlternateCount"] = "Alternate Count",
	};

	private static readonly Dictionary<string, Operator> OperatorNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["is"] = Operator.Is,
		["is not"] = Operator.IsNot,
		["contains"] = Operator.Contains,
		["does not contain"] = Operator.DoesNotContain,
		["greater than"] = Operator.GreaterThan,
		["less than"] = Operator.LessThan,
	};

	public static ImportResult Read(XDocument document)
	{
		var warnings = new List<string>();
		XElement? root = document.Root;
		if (root is null || root.Name.LocalName is not ("Profiles" or "Profile" or "Settings" or "Setting"))
		{
			warnings.Add("The file is not a Library Organizer profile file.");
			return new ImportResult([], [], warnings);
		}
		IEnumerable<XElement> nodes = root.Name.LocalName switch
		{
			"Profiles" => root.Elements("Profile"),
			"Settings" => root.Elements("Setting"),
			_ => [root],
		};

		var profiles = new List<ProfileSettings>();
		var firstIdByOriginalName = new Dictionary<string, ProfileId>(StringComparer.Ordinal);
		var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (XElement node in nodes)
		{
			string? originalName = (string?)node.Attribute("Name");
			if (string.IsNullOrEmpty(originalName))
			{
				warnings.Add("A profile with no name was skipped.");
				continue;
			}
			ProfileSettings profile = ReadProfile(node, originalName!, warnings);
			string name = originalName!;
			for (int n = 2; !usedNames.Add(name); n++)
			{
				name = $"{originalName} ({n})";
			}
			if (name != originalName)
			{
				warnings.Add($"Profile '{originalName}' appears more than once. This copy was renamed '{name}'.");
			}
			profile = profile with { Name = name };
			profiles.Add(profile);
			if (!firstIdByOriginalName.ContainsKey(originalName!))
			{
				firstIdByOriginalName[originalName!] = profile.Id;
			}
		}

		var lastUsed = new List<ProfileId>();
		if ((string?)root.Attribute("LastUsed") is { } lastUsedText)
		{
			foreach (string part in lastUsedText.Split(','))
			{
				if (firstIdByOriginalName.TryGetValue(part.Trim(), out ProfileId id) && !lastUsed.Contains(id))
				{
					lastUsed.Add(id);
				}
			}
		}
		return new ImportResult(profiles, lastUsed, warnings);
	}

	/// <summary>
	/// The 2.x install's settings file: <paramref name="scriptsFolder"/> itself or one of its direct subfolders (the 2.x plugin installs
	/// into its own folder under ComicRack's Scripts folder). The most recently written file wins.
	/// </summary>
	public static string? FindInstalled2x(string scriptsFolder)
	{
		try
		{
			if (!Directory.Exists(scriptsFolder))
			{
				return null;
			}
			IEnumerable<string> candidates = new[] { Path.Combine(scriptsFolder, SettingsFileName) }
				.Concat(Directory.EnumerateDirectories(scriptsFolder).Select(folder => Path.Combine(folder, SettingsFileName)))
				.Where(File.Exists);
			return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static ProfileSettings ReadProfile(XElement node, string name, List<string> warnings)
	{
		void Warn(string message) => warnings.Add($"Profile '{name}': {message}");

		// 2.x started every profile blank. Only a fresh install got the default templates.
		var profile = new ProfileSettings { Name = name, FolderTemplate = "", FileTemplate = "" };
		double version = double.TryParse((string?)node.Attribute("Version"), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

		string mode = Text(node, "Mode") ?? "Move";
		bool copyMode = Flag(node, "CopyMode", true, Warn);
		ProfileAction action;
		if (mode.Equals("Copy", StringComparison.OrdinalIgnoreCase))
		{
			action = new ProfileAction.Copy(copyMode);
		}
		else
		{
			action = new ProfileAction.Move();
			if (mode.Equals("Simulate", StringComparison.OrdinalIgnoreCase) || mode.Equals("Test", StringComparison.OrdinalIgnoreCase))
			{
				Warn("used Simulate mode. It was set to Move, because the review step replaces Simulate.");
			}
			else if (!mode.Equals("Move", StringComparison.OrdinalIgnoreCase))
			{
				Warn($"has the unknown mode '{mode}'. It was set to Move.");
			}
		}

		string ruleMode = Text(node, "ExcludeMode") ?? "Do not";
		string rootOperator = Text(node, "ExcludeOperator") ?? "Any";
		XElement? rulesElement = node.Element("ExcludeRules");
		if (rulesElement is not null)
		{
			ruleMode = (string?)rulesElement.Attribute("ExcludeMode") ?? ruleMode;
			rootOperator = (string?)rulesElement.Attribute("Operator") ?? rootOperator;
		}

		bool failEmpty = Flag(node, "FailEmptyValues", false, Warn);
		var required = new List<string>();
		if (failEmpty)
		{
			foreach (string legacyName in StringList(node, "FailedFields") ?? [])
			{
				Field? field = Fields.ByLegacyName(legacyName.Trim());
				if (field is null || !field.Uses.HasFlag(FieldUses.Required))
				{
					Warn($"required field '{legacyName}' is not known. It was dropped.");
				}
				else if (!required.Contains(field.Key))
				{
					required.Add(field.Key);
				}
			}
		}

		string failedFolder = Flag(node, "MoveFailed", false, Warn) ? Text(node, "FailedFolder") ?? "" : "";

		bool moveFileless = Flag(node, "MoveFileless", false, Warn);
		CoverFormat? covers = null;
		if (moveFileless)
		{
			string format = (Text(node, "FilelessFormat") ?? ".jpg").Trim().TrimStart('.');
			covers = Enum.TryParse(format, true, out CoverFormat parsed) && Enum.IsDefined(typeof(CoverFormat), parsed) ? parsed : CoverFormat.Jpg;
		}

		var excluded = StringList(node, "ExcludeFolders") ?? [];
		foreach (string folder in excluded.Concat(StringList(node, "ExcludedEmptyFolder") ?? []))
		{
			if (AbsPath.TryParse(folder) is null)
			{
				Warn($"folder '{folder}' is not a full path. 2.x matched it as text anywhere in a path, so it will need fixing.");
			}
		}

		return profile with
		{
			Id = new ProfileId(Guid.NewGuid()),
			Action = action,
			FolderTemplate = Text(node, "FolderTemplate") ?? "",
			FileTemplate = Text(node, "FileTemplate") ?? "",
			BaseFolder = Text(node, "BaseFolder") ?? "",
			EmptyFolderName = Text(node, "EmptyFolder") ?? "",
			UseFolder = Flag(node, "UseFolder", true, Warn),
			UseFileName = Flag(node, "UseFileName", true, Warn),
			CollapseSpaces = Flag(node, "ReplaceMultipleSpaces", true, Warn),
			DontAskWhenOneValue = Flag(node, "DontAskWhenMultiOne", true, Warn),
			KeepReadProgressOnReplace = Flag(node, "CopyReadPercentage", true, Warn),
			AutoSpaceFields = Flag(node, "AutoSpaceFields", true, Warn),
			PruneEmptyFolders = Flag(node, "RemoveEmptyFolder", true, Warn),
			KeepFolders = StringList(node, "ExcludedEmptyFolder") ?? [],
			ExcludedFolders = excluded,
			EmptySubstitutes = ReadEmptyData(node, Warn),
			IllegalCharacters = MergeIllegalCharacters(node),
			MonthNames = MergeMonths(node, Warn),
			LastPrefix = ReadControlMap(node, "Prefix", version),
			LastPostfix = ReadControlMap(node, "Postfix", version),
			LastSeparator = ReadControlMap(node, "Seperator", version),
			RuleMode = ruleMode.Equals("Only", StringComparison.OrdinalIgnoreCase) ? RuleMode.Only : RuleMode.Except,
			Rules = new RuleSettings.Group(
				rootOperator.Equals("All", StringComparison.OrdinalIgnoreCase) ? Combinator.All : Combinator.Any,
				rulesElement is null ? [] : ReadRules(rulesElement, Warn)),
			RequiredFields = required,
			FailedBooksFolder = failedFolder,
			FilelessCovers = covers,
		};
	}

	private static IReadOnlyList<RuleSettings> ReadRules(XElement parent, Action<string> warn)
	{
		var children = new List<RuleSettings>();
		foreach (XElement child in parent.Elements())
		{
			if (child.Name == "ExcludeGroup")
			{
				string text = (string?)child.Attribute("Operator") ?? "Any";
				children.Add(new RuleSettings.Group(text.Equals("All", StringComparison.OrdinalIgnoreCase) ? Combinator.All : Combinator.Any, ReadRules(child, warn)));
			}
			else if (child.Name == "ExcludeRule" && ReadCondition(child, warn) is { } condition)
			{
				children.Add(condition);
			}
		}
		return children;
	}

	private static RuleSettings.Condition? ReadCondition(XElement node, Action<string> warn)
	{
		string fieldName = ((string?)node.Attribute("Field") ?? "").Trim();
		string operatorName = ((string?)node.Attribute("Operator") ?? "").Trim();
		string value = (string?)node.Attribute("Value") ?? (string?)node.Attribute("Text") ?? "";

		Field? field = Fields.ByLegacyName(fieldName);
		if (field is null || !field.Uses.HasFlag(FieldUses.Rule))
		{
			warn($"has a rule on '{fieldName}', which is not a field that rules can use. The rule was dropped.");
			return null;
		}
		if (!OperatorNames.TryGetValue(operatorName, out Operator op))
		{
			warn($"has a rule with the unknown operator '{operatorName}'. The rule was dropped.");
			return null;
		}
		if (field.Kind == FieldKind.Date)
		{
			op = op switch { Operator.Is => Operator.On, Operator.GreaterThan => Operator.After, Operator.LessThan => Operator.Before, _ => op };
		}
		if (field.Kind is FieldKind.YesNo or FieldKind.MangaYesNo && OperandParser.CanonicalYesNo(value) is { } canonical)
		{
			value = canonical;
		}
		if (Rule.Condition.Create(field, op, value, out string? error) is null)
		{
			warn($"has the rule '{field.DisplayName} {Operators.Describe(op)} {value}', which is not valid now: {error} It was kept so you can fix it.");
		}
		return new RuleSettings.Condition(field.Key, op, value);
	}

	private static IReadOnlyDictionary<string, string> ReadEmptyData(XElement node, Action<string> warn)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string key, string value) in Items(node, "EmptyData"))
		{
			if (Fields.ByLegacyName(key.Trim()) is { } field)
			{
				if (value.Length > 0)
				{
					map[field.Key] = value;
				}
			}
			else
			{
				warn($"has an empty-value substitute for '{key}', which is not a known field. It was dropped.");
			}
		}
		return map;
	}

	private static IReadOnlyDictionary<string, string> ReadControlMap(XElement node, string name, double version)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string key, string value) in Items(node, name))
		{
			string control = version < 2.0 && Version1ControlNames.TryGetValue(key, out string? renamed) ? renamed : key;
			map[Fields.ByLegacyName(control)?.Key ?? control] = value;
		}
		return map;
	}

	// 2.x merged the file into the default dictionary: a saved file could change a default character but never remove one.
	private static IReadOnlyList<KeyValuePair<string, string>> MergeIllegalCharacters(XElement node)
	{
		var merged = PathDefaults.IllegalCharacters.ToList();
		foreach ((string key, string value) in Items(node, "IllegalCharacters"))
		{
			int index = merged.FindIndex(pair => pair.Key == key);
			if (index >= 0)
			{
				merged[index] = new KeyValuePair<string, string>(key, value);
			}
			else
			{
				merged.Add(new KeyValuePair<string, string>(key, value));
			}
		}
		return merged;
	}

	private static IReadOnlyList<string> MergeMonths(XElement node, Action<string> warn)
	{
		string[] months = PathDefaults.MonthNames.ToArray();
		foreach ((string key, string value) in Items(node, "Months"))
		{
			if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int month) && month is >= 1 and <= 16)
			{
				months[month - 1] = value;
			}
			else
			{
				warn($"has a month name for '{key}', which is not a month from 1 to 16. It was dropped.");
			}
		}
		return months;
	}

	private static IEnumerable<(string Key, string Value)> Items(XElement node, string name)
		=> (node.Element(name)?.Elements("Item") ?? [])
			.Where(item => item.Attribute("Name") is not null && item.Attribute("Value") is not null)
			.Select(item => ((string)item.Attribute("Name")!, (string)item.Attribute("Value")!));

	// A list element replaces the default only when it holds at least one item.
	private static IReadOnlyList<string>? StringList(XElement node, string name)
	{
		List<string> items = (node.Element(name)?.Elements("Item") ?? []).Select(item => item.Value).Where(text => text.Length > 0).ToList();
		return items.Count > 0 ? items : null;
	}

	private static string? Text(XElement node, string name) => node.Element(name)?.Value;

	private static bool Flag(XElement node, string name, bool fallback, Action<string> warn)
	{
		if (node.Element(name) is not { } element)
		{
			return fallback;
		}
		if (bool.TryParse(element.Value.Trim(), out bool value))
		{
			return value;
		}
		warn($"has '{element.Value}' for {name}, which is not true or false. The default was used.");
		return fallback;
	}
}
