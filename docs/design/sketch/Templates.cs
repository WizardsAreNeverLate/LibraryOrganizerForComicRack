using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LibraryOrganizer;

public enum TemplateTarget
{
	/// <summary>A literal '\' outside a group starts a folder level.</summary>
	Folder,
	/// <summary>A literal '\' is plain text (cleaned away later, as 2.x's file-name pass did).</summary>
	FileName,
}

/// <summary>A parse error with its source span, for the editor to underline.</summary>
public readonly record struct TemplateError(int Start, int Length, string Message);

public sealed record TemplateParse(Template? Template, IReadOnlyList<TemplateError> Errors)
{
	public bool Ok => Template is not null;
}

/// <summary>
/// A parsed template. Immutable, book-independent, shared across books and threads.
/// </summary>
/// <remarks>
/// Grammar (2.x-compatible lexical rules; tree instead of repeated innermost-regex passes):
/// <code>
/// template := part*
/// part     := literal | '\' (Folder target: FolderBreak) | group
/// group    := '{' part* '&lt;' token '&gt;' part* '}'          -- exactly one token at the group's own level
/// token    := ('?' | '!')? name args
/// name     := [^\d\s(&gt;]+  ('#' allowed: month#, startmonth#, EndMonth#)
/// args     := digits | ('(' [^){}]* ')')*                     -- never both
/// </code>
/// Differences from 2.x, each reported as a positioned error instead of leaking literal text:
/// a group with no token, a group with two tokens at one level, an unknown name, args the field kind does not accept,
/// digits mixed with parentheses, unbalanced braces.
/// </remarks>
public sealed class Template
{
	private Template() => throw new NotImplementedException();

	public string Source { get; }
	public TemplateTarget Target { get; }

	/// <summary>Every field a token references, including FirstLetterOf sources. Feeds Organizer.Demand.</summary>
	public IReadOnlyCollection<Field> Fields { get; }

	/// <summary>Custom-value keys referenced by &lt;Custom(key)&gt;.</summary>
	public IReadOnlyCollection<string> CustomKeys { get; }

	/// <summary>True when any token needs series data (Series fields, Padded.AutoFromLastIssue, series-scope multi-value).</summary>
	public bool UsesSeries { get; }

	/// <summary>
	/// TODO: hand-written recursive descent over the source with a position cursor.
	///  1. Scan parts until '{' or end. '\' in Folder target emits FolderBreak.
	///  2. On '{': parse parts until '&lt;' at this level (nested '{' recurses into a child Group), then the token, then parts until '}'.
	///  3. Token: optional '?'/'!', name = longest run of [^\d\s(&gt;], then args, then '&gt;'.
	///  4. Resolve the token with TokenResolver; its errors carry the token's span.
	///  Collect all errors (do not stop at the first) so the editor can show them together.
	/// </summary>
	public static TemplateParse Parse(string source, TemplateTarget target) => throw new NotImplementedException();

	internal IReadOnlyList<Node> Nodes { get; }
}

internal abstract record Node
{
	private Node() { }

	public sealed record Literal(string Text) : Node;
	public sealed record FolderBreak : Node;

	/// <summary>
	/// Optional text around one value. <see cref="Before"/> and <see cref="After"/> may hold nested groups.
	/// The group's own token decides whether its children appear: the {' ({&lt;month&gt;, }&lt;year&gt;)'} idiom.
	/// </summary>
	public sealed record Group(IReadOnlyList<Node> Before, Token Token, IReadOnlyList<Node> After, int Start) : Node;
}

/// <summary>A resolved token: which field, how to format it, and an optional test. Built only by TokenResolver.</summary>
internal sealed record Token(Field Field, ValueFormat Format, ValueTest? Test, int Start, int Length);

/// <summary>Number padding: fixed width, or "as wide as this field on the last issue of the series" (2.x width 0).</summary>
internal abstract record PadWidth
{
	private PadWidth() { }

	public sealed record Fixed(int Width) : PadWidth;
	public sealed record AutoFromLastIssue : PadWidth;
}

/// <summary>
/// How a token turns a value into text. Chosen at parse time from the field kind and the args, replacing 2.x's
/// render-time get_field_text table. A format that cannot apply to a field does not parse.
/// </summary>
internal abstract record ValueFormat
{
	private ValueFormat() { }

	/// <summary>Raw text. Int/Issue unset render empty. Month renders its name from PathStyle.MonthNames.</summary>
	public sealed record Plain : ValueFormat;

	/// <summary>&lt;number3&gt;, &lt;volume0&gt;, &lt;firstissuenumber2&gt;. Int, Issue, Series-number fields only.</summary>
	public sealed record Padded(PadWidth Width) : ValueFormat;

	/// <summary>&lt;month#&gt;, &lt;startmonth#2&gt;. Month kind with the '#' suffix.</summary>
	public sealed record MonthNumber(PadWidth? Width) : ValueFormat;

	/// <summary>&lt;manga(Manga)&gt; writes Text on Yes/YesRightToLeft; &lt;manga(Western)(!)&gt; writes Text on No. Unset writes nothing.</summary>
	public sealed record YesNoText(string Text, bool OnNo) : ValueFormat;

	/// <summary>&lt;read(Read)(&gt;)(90)&gt;. Integer compare of ReadPercentage.</summary>
	public sealed record ReadThreshold(string Text, Comparison Op, int Percent) : ValueFormat;

	/// <summary>
	/// &lt;first(Series)&gt;. Upper-cased first letter of Source after a leading article.
	/// Fixes 2.x: one-character values work; elided articles (L', D') are skipped without a following space.
	/// </summary>
	public sealed record FirstLetterOf(Field Source) : ValueFormat;

	/// <summary>&lt;counter(1)(1)(3)&gt;. State lives in the render context, keyed by (profile, token start).</summary>
	public sealed record Counter(int Start, int Step, int Pad) : ValueFormat;

	/// <summary>&lt;ReleasedDate(yyyy-MM)&gt;. Null pattern = invariant "yyyy-MM-dd" (2.x used the current culture, which emits '/' and ':').</summary>
	public sealed record DateFormat(string? Pattern) : ValueFormat;

	/// <summary>&lt;Custom(key)&gt;. Cleaned like every other value (2.x let a '\' in a custom value split folders).</summary>
	public sealed record CustomKey(string Key) : ValueFormat;

	/// <summary>&lt;writer(, )(issue)&gt; / &lt;writer( &amp; )(series)&gt;. The answer may ask for one folder per item.</summary>
	public sealed record MultiValue(string Separator, AnswerScopeKind Scope) : ValueFormat;
}

public enum Comparison { Less, Equal, Greater }

public enum AnswerScopeKind { Issue, Series }

/// <summary>
/// '?' and '!' tests. Compared against the RAW value (before cleaning), so &lt;?format(Trade: Paperback)&gt; can match.
/// A passing test writes the group's Before and After text without the value; a failing one writes nothing.
/// </summary>
internal abstract record ValueTest
{
	private ValueTest() { }

	/// <summary>&lt;?f(x)&gt;</summary>
	public sealed record EqualsText(string Text) : ValueTest;
	/// <summary>&lt;?f(!re)&gt;. Anchored at the start, like Python re.match. An empty pattern never matches (2.x).</summary>
	public sealed record MatchesStart(Regex Pattern) : ValueTest;
	/// <summary>&lt;!f(x)&gt;</summary>
	public sealed record NotEqualsText(string Text) : ValueTest;
	/// <summary>&lt;!f(!re)&gt;</summary>
	public sealed record NotMatchesStart(Regex Pattern) : ValueTest;
	/// <summary>&lt;!f&gt;</summary>
	public sealed record IsEmpty : ValueTest;
}

/// <summary>
/// Turns name + args into Field, ValueFormat and ValueTest. The rule table lives here, keyed by FieldKind.
/// </summary>
internal static class TokenResolver
{
	/// <summary>
	/// TODO:
	///  - '!' prefix: if args end in a paren group, pop it as the test arg; test = IsEmpty / NotEqualsText / NotMatchesStart.
	///  - '?' prefix: the last paren group is required; pop it; test = EqualsText / MatchesStart.
	///  - strip a trailing '#' from the name -> month-number form (error unless the field kind is Month).
	///  - look up Fields.ByTemplateName; unknown -> error "Unknown field 'x'. Did you mean 'y'?" (edit distance over Fields.All).
	///  - pick the format from (kind, remaining args):
	///      digits          -> Padded / MonthNumber   (Int, Issue, Month#, Series numbers)
	///      none            -> Plain (Date -> DateFormat(null))
	///      (t) | (t)(!)    -> YesNoText                (YesNo, MangaYesNo)
	///      (t)(op)(n)      -> ReadThreshold            (read)
	///      (field)         -> FirstLetterOf            (first; accepts display or template name)
	///      (s)(i)(p)       -> Counter                  (counter)
	///      (fmt)           -> DateFormat               (Date)
	///      (key)           -> CustomKey                (Custom)
	///      (sep)(issue|series) -> MultiValue           (Items)
	///    anything else -> error naming what this field accepts.
	/// </summary>
	public static (Token? Token, IReadOnlyList<TemplateError> Errors) Resolve(string rawName, string rawArgs, int start, int length)
		=> throw new NotImplementedException();
}

/// <summary>A rendered path before cleaning: text pieces and structural folder breaks.</summary>
internal abstract record Piece
{
	private Piece() { }

	public sealed record Text(string Raw) : Piece;
	public sealed record Break : Piece;
}

/// <summary>Result of rendering one template for one book.</summary>
internal abstract record RenderOutcome
{
	private RenderOutcome() { }

	public sealed record Done(IReadOnlyList<Piece> Pieces, CounterState Counters) : RenderOutcome;

	/// <summary>A multi-value token had no answer. Rendering stops; the plan entry waits on this key.</summary>
	public sealed record NeedsAnswer(QuestionKey Key, Question Question) : RenderOutcome;
}

/// <summary>Immutable counter positions keyed by (profile, token start). Advanced only when the caller keeps the render.</summary>
internal sealed record CounterState(IReadOnlyDictionary<(ProfileId Profile, int TokenStart), int> Next)
{
	public static readonly CounterState Empty = new(new Dictionary<(ProfileId, int), int>());
}

/// <summary>Everything a render reads besides the book. Built once per profile per plan.</summary>
internal sealed record RenderContext(Profile Profile, SeriesIndex Series, AnswerBook Answers, CounterState Counters, bool Preview);

internal static class Renderer
{
	/// <summary>
	/// TODO, per group (children rendered lazily, so a discarded child never asks a question or advances a counter):
	///   raw = FieldReader.Read(token.Field, book, series)
	///   if token.Test is not null: return Test(raw) ? render(Before) + render(After) : nothing
	///   text = format(raw)                      // may return NeedsAnswer (MultiValue) or advance a counter
	///   if text is empty: return Profile.Style.EmptySubstitutes.TryGet(field) as Text, or nothing
	///   return render(Before) + text pieces + render(After)
	/// MultiValue: answer lookup order = AnswerBook issue/series answer -> one item and DontAskWhenOneValue -> preset match
	/// (largest preset fully present; with AskWhenMore false use it, else preselect) -> NeedsAnswer. With Preview=true,
	/// a missing answer renders the placeholder "‹writer?›" instead of NeedsAnswer.
	/// AsFolders answer: items joined with Separator, with Break pieces between items (structural, not a '\' character).
	/// </summary>
	public static RenderOutcome Render(Template template, BookRecord book, RenderContext context) => throw new NotImplementedException();
}

/// <summary>
/// Profile-level text policy. The ONE place values become path text.
/// </summary>
public sealed record PathStyle(
	IReadOnlyList<KeyValuePair<string, string>> IllegalCharacters,
	bool CollapseSpaces,
	string EmptyFolderName,
	IReadOnlyDictionary<Field, string> EmptySubstitutes,
	IReadOnlyList<string> MonthNames)
{
	/// <summary>2.x defaults: ? / \ * removed, ':' -> ' - ', '&lt;' '&gt;' -> '[' ']', '|' -> '!', '"' -> "'". Months 1-12 plus 4 seasons.</summary>
	public static PathStyle Default => throw new NotImplementedException();

	/// <summary>
	/// TODO: split pieces on Break into levels; per level: concat text, replace illegal chars (longest key first),
	/// collapse whitespace runs, trim whitespace and '.', blank -> EmptyFolderName, still blank -> drop the level.
	/// Any '\' or '/' left in text is replaced by the table, so only Break creates levels.
	/// </summary>
	internal IReadOnlyList<string> CleanFolder(IReadOnlyList<Piece> pieces) => throw new NotImplementedException();

	/// <summary>Breaks become nothing; same text cleaning; blank stays blank (the planner fails the entry).</summary>
	internal string CleanFileName(IReadOnlyList<Piece> pieces) => throw new NotImplementedException();
}
