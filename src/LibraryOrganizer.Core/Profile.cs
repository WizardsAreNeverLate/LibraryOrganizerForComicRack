using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>Which part of the path a profile renders. "Neither" cannot be constructed.</summary>
public abstract record Destination
{
	private Destination() { }

	public sealed record FolderAndName(AbsPath BaseFolder, Template Folder, Template FileName) : Destination;

	/// <summary>Keeps the current file name.</summary>
	public sealed record FolderOnly(AbsPath BaseFolder, Template Folder) : Destination;

	/// <summary>Keeps the current folder. No base folder needed.</summary>
	public sealed record NameOnly(Template FileName) : Destination;
}

/// <summary>
/// Fields that must have a value, checked on the book before rendering. The route is part of the type, so "move failed
/// books" always has a folder.
/// </summary>
public sealed record RequiredFields(IReadOnlyList<Field> Fields, RequiredFields.FailRoute Route)
{
	public abstract record FailRoute
	{
		private FailRoute() { }

		public sealed record Skip : FailRoute;

		/// <summary>
		/// The folder takes the place of the base folder, and the book is reported as failed. A profile that keeps the
		/// current folder puts the file directly in it.
		/// </summary>
		public sealed record MoveUnder(AbsPath Folder) : FailRoute;
	}
}

/// <summary>Empty-folder removal after moves.</summary>
/// <param name="Keep">Folders that stay even when empty.</param>
public sealed record FolderPruning(IReadOnlyList<AbsPath> Keep);

/// <summary>A compiled, valid profile. The engine accepts only this type. Built by <see cref="ProfileSettings.Compile"/>.</summary>
/// <param name="FilelessCovers">Null skips fileless books. Otherwise their custom cover is written as an image in this format.</param>
/// <param name="Pruning">Null keeps empty folders.</param>
public sealed record Profile(
	ProfileId Id,
	string Name,
	ProfileAction Action,
	Destination Destination,
	Filter Filter,
	RequiredFields? Required,
	PathStyle Style,
	CoverFormat? FilelessCovers,
	FolderPruning? Pruning,
	bool DontAskWhenOneValue,
	bool KeepReadProgressOnReplace);

/// <summary>Either a profile, or at least one error.</summary>
public sealed record CompileResult(Profile? Profile, IReadOnlyList<SettingsError> Errors);

/// <param name="Where">The <see cref="ProfileSettings"/> property the error is in, for the form to focus.</param>
/// <param name="Span">Set for a template error: the text to underline.</param>
public sealed record SettingsError(string Where, string Message, TemplateError? Span = null);

internal static class ProfileCompiler
{
	public static CompileResult Compile(ProfileSettings settings)
	{
		var errors = new List<SettingsError>();
		Destination? destination = CompileDestination(settings, errors);
		var filter = new Filter(
			new FolderSet(Folders(settings.ExcludedFolders, nameof(ProfileSettings.ExcludedFolders), errors)),
			settings.RuleMode,
			CompileGroup(settings.Rules, errors));
		RequiredFields? required = CompileRequired(settings, errors);
		IReadOnlyList<AbsPath> keep = Folders(settings.KeepFolders, nameof(ProfileSettings.KeepFolders), errors);
		if (errors.Count > 0 || destination is null)
		{
			return new CompileResult(null, errors);
		}

		var style = new PathStyle(
			settings.IllegalCharacters,
			settings.CollapseSpaces,
			settings.EmptyFolderName,
			// A key this build does not know stays in the settings and is not an error.
			settings.EmptySubstitutes
				.Select(pair => (Field: Fields.ByKey(pair.Key), pair.Value))
				.Where(pair => pair.Field is not null)
				.ToDictionary(pair => pair.Field!, pair => pair.Value),
			settings.MonthNames);
		return new CompileResult(
			new Profile(
				settings.Id, settings.Name, settings.Action, destination, filter, required, style, settings.FilelessCovers,
				settings.PruneEmptyFolders ? new FolderPruning(keep) : null, settings.DontAskWhenOneValue, settings.KeepReadProgressOnReplace),
			[]);
	}

	private static Destination? CompileDestination(ProfileSettings settings, List<SettingsError> errors)
	{
		if (!settings.UseFolder && !settings.UseFileName)
		{
			errors.Add(new SettingsError(nameof(ProfileSettings.UseFolder), "The profile must organize the folder, the file name, or both."));
			return null;
		}
		AbsPath? baseFolder = null;
		Template? folder = null;
		Template? fileName = null;
		if (settings.UseFolder)
		{
			baseFolder = AbsPath.TryParse(settings.BaseFolder);
			if (baseFolder is null)
			{
				errors.Add(new SettingsError(
					nameof(ProfileSettings.BaseFolder),
					string.IsNullOrWhiteSpace(settings.BaseFolder) ? "A base folder is required." : $"'{settings.BaseFolder}' is not a full folder path."));
			}
			folder = ParseTemplate(settings.FolderTemplate, TemplateTarget.Folder, nameof(ProfileSettings.FolderTemplate), errors);
		}
		if (settings.UseFileName)
		{
			fileName = ParseTemplate(settings.FileTemplate, TemplateTarget.FileName, nameof(ProfileSettings.FileTemplate), errors);
		}
		return (settings.UseFolder, settings.UseFileName) switch
		{
			(true, true) when baseFolder is { } root && folder is not null && fileName is not null => new Destination.FolderAndName(root, folder, fileName),
			(true, false) when baseFolder is { } root && folder is not null => new Destination.FolderOnly(root, folder),
			(false, true) when fileName is not null => new Destination.NameOnly(fileName),
			_ => null,
		};
	}

	private static Template? ParseTemplate(string source, TemplateTarget target, string where, List<SettingsError> errors)
	{
		TemplateParse parse = Template.Parse(source, target);
		errors.AddRange(parse.Errors.Select(error => new SettingsError(where, error.Message, error)));
		return parse.Template;
	}

	private static Rule.Group CompileGroup(RuleSettings.Group group, List<SettingsError> errors)
	{
		var children = new List<Rule>();
		foreach (RuleSettings child in group.Children)
		{
			Rule? rule = child switch
			{
				RuleSettings.Group nested => CompileGroup(nested, errors),
				RuleSettings.Condition condition => CompileCondition(condition, errors),
				_ => null,
			};
			if (rule is not null)
			{
				children.Add(rule);
			}
		}
		return new Rule.Group(group.Combinator, children);
	}

	private static Rule.Condition? CompileCondition(RuleSettings.Condition settings, List<SettingsError> errors)
	{
		string? error = $"A rule uses the unknown field '{settings.FieldKey}'.";
		Rule.Condition? condition = Fields.ByKey(settings.FieldKey) is { } field
			? Rule.Condition.Create(field, settings.Operator, settings.RawOperand, out error)
			: null;
		if (condition is null)
		{
			errors.Add(new SettingsError(nameof(ProfileSettings.Rules), error ?? "A rule is not valid."));
		}
		return condition;
	}

	private static RequiredFields? CompileRequired(ProfileSettings settings, List<SettingsError> errors)
	{
		var fields = new List<Field>();
		foreach (string key in settings.RequiredFields)
		{
			Field? field = Fields.ByKey(key);
			if (field is null)
			{
				errors.Add(new SettingsError(nameof(ProfileSettings.RequiredFields), $"The required field '{key}' is unknown."));
			}
			else if (!field.Uses.HasFlag(FieldUses.Required))
			{
				errors.Add(new SettingsError(nameof(ProfileSettings.RequiredFields), $"{field.DisplayName} cannot be a required field."));
			}
			else
			{
				fields.Add(field);
			}
		}
		RequiredFields.FailRoute route = new RequiredFields.FailRoute.Skip();
		if (!string.IsNullOrWhiteSpace(settings.FailedBooksFolder))
		{
			if (AbsPath.TryParse(settings.FailedBooksFolder) is { } folder)
			{
				route = new RequiredFields.FailRoute.MoveUnder(folder);
			}
			else
			{
				errors.Add(new SettingsError(nameof(ProfileSettings.FailedBooksFolder), $"'{settings.FailedBooksFolder}' is not a full folder path."));
			}
		}
		return fields.Count == 0 ? null : new RequiredFields(fields, route);
	}

	private static IReadOnlyList<AbsPath> Folders(IReadOnlyList<string> raw, string where, List<SettingsError> errors)
	{
		var folders = new List<AbsPath>();
		foreach (string text in raw.Where(text => !string.IsNullOrWhiteSpace(text)))
		{
			if (AbsPath.TryParse(text) is { } folder)
			{
				folders.Add(folder);
			}
			else
			{
				errors.Add(new SettingsError(where, $"'{text}' is not a full folder path."));
			}
		}
		return folders;
	}
}
