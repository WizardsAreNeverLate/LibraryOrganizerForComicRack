using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

public abstract record AnswerScope
{
	private AnswerScope() { }

	public sealed record Issue(BookId Book) : AnswerScope;
	public sealed record Series(SeriesKey Key) : AnswerScope;
}

public readonly record struct QuestionKey(Field Field, AnswerScope Scope);

/// <param name="Choices">Issue scope: the book's items. Series scope: distinct items across the series.</param>
/// <param name="Preselected">From the largest matching preset, in preset order.</param>
/// <param name="ExampleCaption">The book that raised the question.</param>
/// <param name="Waiting">How many entries this answer unblocks, for the dialog title. A render reports 1; the planner sums.</param>
public sealed record Question(QuestionKey Key, string FieldName, IReadOnlyList<string> Choices, IReadOnlyList<string> Preselected, string ExampleCaption, int Waiting);

/// <param name="Selected">Chosen items in the user's order. Empty = write nothing.</param>
/// <param name="AsFolders">One folder level per item.</param>
/// <param name="EveryIssue">Series scope: write every selected item even on issues that lack it.</param>
/// <param name="SaveAsPreset">Issue scope: add a preset for this item set ("use for every issue that has these").</param>
/// <param name="PresetAskWhenMore">With SaveAsPreset: still ask when a book has items beyond the preset.</param>
public sealed record Answer(IReadOnlyList<string> Selected, bool AsFolders, bool EveryIssue, bool SaveAsPreset, bool PresetAskWhenMore);

/// <summary>A remembered item set for a field (2.x "always use").</summary>
public sealed record Preset(Field Field, IReadOnlyList<string> Items, bool AskWhenMore, bool AsFolders);

/// <summary>Immutable answers: per issue (by BookId), per series (by SeriesKey), and presets per field.</summary>
public sealed class AnswerBook
{
	private readonly Dictionary<QuestionKey, Answer> answers;
	private readonly List<Preset> presets;

	private AnswerBook(Dictionary<QuestionKey, Answer> answers, List<Preset> presets)
	{
		this.answers = answers;
		this.presets = presets;
	}

	public static AnswerBook Empty { get; } = new([], []);

	public IReadOnlyList<(QuestionKey Key, Answer Answer)> Remembered => answers.Select(pair => (pair.Key, pair.Value)).ToList();

	public IReadOnlyList<Preset> Presets => presets.AsReadOnly();

	/// <summary>Stores the answer. An issue answer with SaveAsPreset and at least one item also saves its preset, replacing one with the same items.</summary>
	public AnswerBook With(QuestionKey key, Answer answer)
	{
		var newAnswers = new Dictionary<QuestionKey, Answer>(answers) { [key] = answer };
		if (!answer.SaveAsPreset || key.Scope is not AnswerScope.Issue || answer.Selected.Count == 0)
		{
			return new AnswerBook(newAnswers, presets);
		}
		var preset = new Preset(key.Field, answer.Selected.ToList(), answer.PresetAskWhenMore, answer.AsFolders);
		return new AnswerBook(newAnswers, presets.Where(existing => !SameItems(existing, preset)).Append(preset).ToList());
	}

	public AnswerBook Forget(QuestionKey key)
	{
		var newAnswers = new Dictionary<QuestionKey, Answer>(answers);
		return newAnswers.Remove(key) ? new AnswerBook(newAnswers, presets) : this;
	}

	public AnswerBook ForgetPreset(Preset preset) => new(answers, presets.Where(existing => !SameItems(existing, preset)).ToList());

	/// <summary>
	/// An issue answer is valid only while all its selected items are still on the book. A stale one is ignored, so the
	/// question comes back instead of writing items the book no longer has.
	/// </summary>
	internal Answer? Find(QuestionKey key, IReadOnlyList<string> currentItems)
	{
		if (!answers.TryGetValue(key, out Answer? answer))
		{
			return null;
		}
		return key.Scope is AnswerScope.Issue && !answer.Selected.All(item => currentItems.Contains(item, StringComparer.OrdinalIgnoreCase))
			? null
			: answer;
	}

	/// <summary>The preset with the most items among those whose items are all on the book.</summary>
	internal Preset? BestPreset(Field field, IReadOnlyList<string> items)
		=> presets
			.Where(preset => preset.Field == field && preset.Items.All(item => items.Contains(item, StringComparer.OrdinalIgnoreCase)))
			.OrderByDescending(preset => preset.Items.Count)
			.FirstOrDefault();

	private static bool SameItems(Preset a, Preset b)
		=> a.Field == b.Field && new HashSet<string>(a.Items, StringComparer.OrdinalIgnoreCase).SetEquals(b.Items);
}
