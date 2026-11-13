using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>The tabs of the profile editor, in display order.</summary>
public enum ConfigTab { General, Templates, Rules, Options }

public sealed record ProfileProblem(ProfileId Profile, string ProfileName, ConfigTab Tab, string Message);

/// <summary>The checks run when the dialog closes with OK and when the user switches profile. All problems are reported together.</summary>
internal static class ProfileValidator
{
	public static IReadOnlyList<ProfileProblem> Validate(ProfileSettings profile)
	{
		var problems = new List<ProfileProblem>();
		void Add(ConfigTab tab, string message) => problems.Add(new ProfileProblem(profile.Id, profile.Name, tab, message));

		if (!profile.UseFolder && !profile.UseFileName)
		{
			Add(ConfigTab.General, "Turn on at least one of \"organize into folders\" and \"rename files\". With both off there is nothing to do.");
		}
		if (profile.UseFolder)
		{
			if (profile.BaseFolder.Trim().Length == 0)
			{
				Add(ConfigTab.General, "Choose a base folder, or turn off \"organize into folders\".");
			}
			else if (!Path.IsPathRooted(profile.BaseFolder.Trim()))
			{
				Add(ConfigTab.General, "The base folder must be a full path, such as D:\\Comics.");
			}
			foreach (TemplateError error in Template.Parse(profile.FolderTemplate, TemplateTarget.Folder).Errors)
			{
				Add(ConfigTab.Templates, $"Folder template, character {error.Start + 1}: {error.Message}");
			}
		}
		if (profile.UseFileName)
		{
			if (profile.FileTemplate.Trim().Length == 0)
			{
				Add(ConfigTab.Templates, "The file template is empty. Write one, or turn off \"rename files\".");
			}
			foreach (TemplateError error in Template.Parse(profile.FileTemplate, TemplateTarget.FileName).Errors)
			{
				Add(ConfigTab.Templates, $"File template, character {error.Start + 1}: {error.Message}");
			}
		}

		foreach (string message in RuleProblems(profile.Rules))
		{
			Add(ConfigTab.Rules, message);
		}

		var seen = new HashSet<string>();
		foreach (KeyValuePair<string, string> pair in profile.IllegalCharacters)
		{
			if (pair.Key.Length != 1)
			{
				Add(ConfigTab.Options, $"An illegal character must be exactly one character, not \"{pair.Key}\".");
			}
			else if (!seen.Add(pair.Key))
			{
				Add(ConfigTab.Options, $"The illegal character \"{pair.Key}\" is listed twice.");
			}
		}
		return problems;
	}

	public static IReadOnlyList<ProfileProblem> ValidateAll(IEnumerable<ProfileSettings> profiles) => [.. profiles.SelectMany(Validate)];

	/// <summary>The message for one rule row, or null when the row is valid.</summary>
	public static string? ConditionError(RuleSettings.Condition condition)
	{
		Field? field = Fields.ByKey(condition.FieldKey);
		if (field is null)
		{
			return $"Unknown field '{condition.FieldKey}'. Choose another field.";
		}
		Rule.Condition.Create(field, condition.Operator, condition.RawOperand, out string? error);
		return error;
	}

	private static IEnumerable<string> RuleProblems(RuleSettings.Group group)
	{
		foreach (RuleSettings child in group.Children)
		{
			switch (child)
			{
				case RuleSettings.Group nested:
					foreach (string message in RuleProblems(nested))
					{
						yield return message;
					}
					break;
				case RuleSettings.Condition condition when ConditionError(condition) is { } error:
					Field? field = Fields.ByKey(condition.FieldKey);
					yield return $"Rule \"{field?.DisplayName ?? condition.FieldKey} {Operators.Describe(condition.Operator)} {condition.RawOperand}\": {error}";
					break;
			}
		}
	}
}
