using System;
using System.Collections.Generic;

namespace LibraryOrganizer;

/// <summary>
/// Rule operators. Which ones a field offers comes from its kind (<see cref="Operators.For"/>), so the editor and the evaluator agree.
/// </summary>
public enum Operator
{
	Is, IsNot, Contains, DoesNotContain, StartsWith, MatchesRegex,
	GreaterThan, LessThan,
	ContainsItem, DoesNotContainItem,
	Before, After, On,
	IsEmpty, IsNotEmpty,
}

public static class Operators
{
	/// <summary>
	/// Text: Is, IsNot, Contains, DoesNotContain, StartsWith, MatchesRegex, IsEmpty, IsNotEmpty (case-insensitive).
	/// Int/Decimal/Issue/Month: Is, IsNot, GreaterThan, LessThan, IsEmpty, IsNotEmpty (numeric; Issue natural order; Unset never compares).
	/// YesNo/MangaYesNo: Is, IsNot (operand is a YesNoValue or "Unknown").
	/// Items: ContainsItem, DoesNotContainItem (whole item, case-insensitive), Contains, DoesNotContain (substring of raw text), IsEmpty, IsNotEmpty.
	/// Date: Before, After, On, IsEmpty, IsNotEmpty.
	/// Synthetic: none (not offered in the rule editor).
	/// </summary>
	public static IReadOnlyList<Operator> For(FieldKind kind) => throw new NotImplementedException();
}

/// <summary>A parsed operand, typed by the field kind at the settings boundary.</summary>
public abstract record Operand
{
	private Operand() { }

	public sealed record None : Operand;
	public sealed record Text(string Value) : Operand;
	public sealed record Number(decimal Value) : Operand;
	public sealed record Issue(IssueNumber Value) : Operand;
	public sealed record YesNo(YesNoValue? Value) : Operand;
	public sealed record Date(DateTime Value) : Operand;
	public sealed record Pattern(System.Text.RegularExpressions.Regex Value) : Operand;
}

public enum Combinator { Any, All }

/// <summary>A rule tree node. Closed: a Condition or a Group.</summary>
public abstract record Rule
{
	private Rule() { }

	public sealed record Group(Combinator Combinator, IReadOnlyList<Rule> Children) : Rule;

	public sealed record Condition : Rule
	{
		private Condition(Field field, Operator op, Operand operand) => throw new NotImplementedException();

		public Field Field { get; }
		public Operator Operator { get; }
		public Operand Operand { get; }

		/// <summary>Boundary: rejects an operator the field kind does not offer and an operand that does not parse for the kind.</summary>
		public static Condition? Create(Field field, Operator op, string rawOperand, out string? error) => throw new NotImplementedException();

		/// <summary>The text the editor shows and settings.xml stores.</summary>
		public string RawOperand { get; }
	}
}

public enum RuleMode
{
	/// <summary>Organize only books that match.</summary>
	Only,
	/// <summary>Organize every book except those that match (2.x "Do not").</summary>
	Except,
}

/// <summary>Excluded folders as normalized paths; a book is excluded when its file is under one of them.</summary>
public sealed record FolderSet(IReadOnlyList<AbsPath> Folders)
{
	public static readonly FolderSet None = new([]);
	public bool Contains(AbsPath file) => throw new NotImplementedException();
}

/// <summary>A profile's filter. An empty root group accepts every book in both modes, as 2.x.</summary>
public sealed record Filter(FolderSet ExcludedFolders, RuleMode Mode, Rule.Group Root)
{
	public static readonly Filter AcceptAll = new(FolderSet.None, RuleMode.Except, new Rule.Group(Combinator.Any, []));

	/// <summary>
	/// TODO: excluded folder first, then the tree. Evaluation short-circuits (2.x did not).
	/// An empty group is "no opinion" and is skipped by its parent, as 2.x's None.
	/// Returns the reason for the review grid: "In excluded folder D:\Comics\Unsorted", "Matches rule: Publisher is DC", "Does not match: ...".
	/// </summary>
	internal FilterVerdict Evaluate(BookRecord book, SeriesIndex series) => throw new NotImplementedException();
}

internal readonly record struct FilterVerdict(bool Accepted, string? Reason);
