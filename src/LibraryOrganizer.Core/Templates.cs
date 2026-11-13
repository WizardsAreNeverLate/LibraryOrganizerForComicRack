using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LibraryOrganizer;

public enum TemplateTarget
{
	/// <summary>A literal '\' starts a folder level.</summary>
	Folder,
	/// <summary>A literal '\' is plain text, which cleaning removes.</summary>
	FileName,
}

/// <summary>A parse error with its source span, for the editor to underline.</summary>
public readonly record struct TemplateError(int Start, int Length, string Message);

public sealed record TemplateParse(Template? Template, IReadOnlyList<TemplateError> Errors)
{
	public bool Ok => Template is not null;
}

/// <summary>A parsed template. Immutable, book-independent, shared across books and threads.</summary>
/// <remarks>
/// Grammar (2.x lexical rules, parsed into a tree):
/// <code>
/// template := part*
/// part     := literal | '\' (Folder target: a folder break) | group
/// group    := '{' part* '&lt;' token '&gt;' part* '}'          -- exactly one token at the group's own level
/// token    := ('?' | '!')? name '#'? args
/// name     := [^\d\s(&gt;]+
/// args     := digits | ('(' [^){}]* ')')*                     -- never both
/// </code>
/// Text outside every group is literal, '&lt;' and '&gt;' included. Where 2.x left a group it could not render in the
/// output, this parser reports a positioned error.
/// </remarks>
public sealed class Template
{
	private Template(string source, TemplateTarget target, IReadOnlyList<Node> nodes)
	{
		Source = source;
		Target = target;
		Nodes = nodes;
		List<Token> tokens = Tokens(nodes).ToList();
		Fields = tokens
			.SelectMany(token => token.Format is ValueFormat.FirstLetterOf first ? new[] { token.Field, first.Source } : [token.Field])
			.Distinct()
			.ToList();
		CustomKeys = tokens.Select(token => token.Format).OfType<ValueFormat.CustomKey>().Select(custom => custom.Key).Distinct().ToList();
		UsesSeries = Fields.Any(field => field.Source == FieldSource.Series) || tokens.Any(token => token.Format switch
		{
			ValueFormat.Padded padded => padded.Width is PadWidth.AutoFromLastIssue,
			ValueFormat.MonthNumber month => month.Width is PadWidth.AutoFromLastIssue,
			ValueFormat.MultiValue multi => multi.Scope == AnswerScopeKind.Series,
			_ => false,
		});
	}

	public string Source { get; }
	public TemplateTarget Target { get; }

	/// <summary>Every field a token references, including the field a first-letter token reads.</summary>
	public IReadOnlyCollection<Field> Fields { get; }

	/// <summary>Custom-value keys referenced by &lt;Custom(key)&gt;.</summary>
	public IReadOnlyCollection<string> CustomKeys { get; }

	/// <summary>True when any token needs series data: a series field, automatic padding, or a series-scope multi-value.</summary>
	public bool UsesSeries { get; }

	internal IReadOnlyList<Node> Nodes { get; }

	/// <summary>Never throws. Reports every error it finds, so the editor can underline them together.</summary>
	public static TemplateParse Parse(string source, TemplateTarget target)
	{
		var parser = new Parser(source ?? "", target);
		IReadOnlyList<Node> nodes = parser.ParseTemplate();
		return parser.Errors.Count > 0
			? new TemplateParse(null, parser.Errors.OrderBy(error => error.Start).ToList())
			: new TemplateParse(new Template(source ?? "", target, nodes), []);
	}

	private static IEnumerable<Token> Tokens(IEnumerable<Node> nodes)
		=> nodes.OfType<Node.Group>().SelectMany(group => Tokens(group.Before).Append(group.Token).Concat(Tokens(group.After)));

	private sealed class Parser
	{
		private readonly string source;
		private readonly TemplateTarget target;
		private readonly int end;
		private int pos;

		public Parser(string source, TemplateTarget target)
		{
			this.source = source;
			this.target = target;
			end = source.Length;
			if (target == TemplateTarget.Folder)
			{
				while (pos < end && IsEdge(source[pos]))
				{
					pos++;
				}
				while (end > pos && IsEdge(source[end - 1]))
				{
					end--;
				}
			}
		}

		public List<TemplateError> Errors { get; } = [];

		public IReadOnlyList<Node> ParseTemplate()
		{
			var nodes = new List<Node>();
			while (pos < end)
			{
				ParseParts(nodes, stopAtToken: false, inGroup: false);
				if (pos < end)
				{
					Error(pos, 1, "This '}' closes nothing. Remove it, or open a group with '{'.");
					pos++;
				}
			}
			return nodes;
		}

		private void ParseParts(List<Node> nodes, bool stopAtToken, bool inGroup)
		{
			var text = new StringBuilder();
			void Flush()
			{
				if (text.Length > 0)
				{
					nodes.Add(new Node.Literal(text.ToString()));
					text.Clear();
				}
			}

			while (pos < end)
			{
				char c = source[pos];
				if (c == '}' || (c == '<' && stopAtToken))
				{
					break;
				}
				if (c == '{')
				{
					Flush();
					if (ParseGroup() is { } group)
					{
						nodes.Add(group);
					}
				}
				else if (c == '\\' && target == TemplateTarget.Folder)
				{
					Flush();
					nodes.Add(new Node.FolderBreak());
					pos++;
				}
				else if (c == '<' && inGroup)
				{
					int start = pos++;
					SkipToTokenEnd();
					Error(start, pos - start, "A group holds one field. Put this one in its own { }.");
				}
				else
				{
					text.Append(c);
					pos++;
				}
			}
			Flush();
		}

		private Node.Group? ParseGroup()
		{
			int start = pos;
			pos++;
			var before = new List<Node>();
			ParseParts(before, stopAtToken: true, inGroup: true);
			if (pos >= end)
			{
				Error(start, 1, "This '{' has no matching '}'.");
				return null;
			}
			if (source[pos] == '}')
			{
				pos++;
				Error(start, pos - start, "A group needs a field, as in {<series>}.");
				return null;
			}
			Token? token = ParseToken();
			var after = new List<Node>();
			ParseParts(after, stopAtToken: false, inGroup: true);
			if (pos >= end)
			{
				Error(start, 1, "This '{' has no matching '}'.");
				return null;
			}
			pos++;
			return token is null ? null : new Node.Group(before, token, after, start);
		}

		private Token? ParseToken()
		{
			int start = pos;
			pos++;
			int nameStart = pos;
			while (pos < end && !char.IsDigit(source[pos]) && !char.IsWhiteSpace(source[pos]) && "(><{}".IndexOf(source[pos]) < 0)
			{
				pos++;
			}
			string name = source.Substring(nameStart, pos - nameStart);

			string? digits = null;
			var arguments = new List<string>();
			if (pos < end && char.IsDigit(source[pos]))
			{
				int digitsStart = pos;
				while (pos < end && char.IsDigit(source[pos]))
				{
					pos++;
				}
				digits = source.Substring(digitsStart, pos - digitsStart);
			}
			else
			{
				while (pos < end && source[pos] == '(')
				{
					int close = pos + 1;
					while (close < end && "){}".IndexOf(source[close]) < 0)
					{
						close++;
					}
					if (close >= end || source[close] != ')')
					{
						SkipToTokenEnd();
						Error(start, pos - start, "This '(' has no matching ')'. An argument cannot contain '{' or '}'.");
						return null;
					}
					arguments.Add(source.Substring(pos + 1, close - pos - 1));
					pos = close + 1;
				}
			}

			if (pos < end && source[pos] == '>')
			{
				pos++;
				(Token? token, IReadOnlyList<TemplateError> errors) = TokenResolver.Resolve(name, digits, arguments, start, pos - start);
				Errors.AddRange(errors);
				return token;
			}

			bool mixed = pos < end && ((digits != null && source[pos] == '(') || (arguments.Count > 0 && char.IsDigit(source[pos])));
			SkipToTokenEnd();
			Error(start, pos - start, mixed
				? "A field takes a padding width or arguments in parentheses, not both."
				: "This field is not closed. Expected '>' after the name and its arguments.");
			return null;
		}

		private void SkipToTokenEnd()
		{
			while (pos < end && "{}".IndexOf(source[pos]) < 0)
			{
				if (source[pos++] == '>')
				{
					return;
				}
			}
		}

		private void Error(int start, int length, string message) => Errors.Add(new TemplateError(start, length, message));

		private static bool IsEdge(char c) => c == '\\' || char.IsWhiteSpace(c);
	}
}

internal abstract record Node
{
	private Node() { }

	public sealed record Literal(string Text) : Node;
	public sealed record FolderBreak : Node;

	/// <summary>
	/// Optional text around one value. <see cref="Before"/> and <see cref="After"/> may hold nested groups.
	/// The group's own token decides whether its children appear: the { ({&lt;month&gt;, }&lt;year&gt;)} idiom.
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
/// How a token turns a value into text. Chosen at parse time from the field kind and the args, so a format that cannot
/// apply to a field does not parse.
/// </summary>
internal abstract record ValueFormat
{
	private ValueFormat() { }

	/// <summary>Raw text. Unset renders empty. A month renders its name.</summary>
	public sealed record Plain : ValueFormat;

	/// <summary>&lt;number3&gt;, &lt;volume0&gt;, &lt;firstissuenumber2&gt;.</summary>
	public sealed record Padded(PadWidth Width) : ValueFormat;

	/// <summary>&lt;month#&gt;, &lt;startmonth#2&gt;.</summary>
	public sealed record MonthNumber(PadWidth? Width) : ValueFormat;

	/// <summary>&lt;manga(Manga)&gt; writes Text on Yes; &lt;manga(Western)(!)&gt; writes Text on No. Unset writes nothing.</summary>
	public sealed record YesNoText(string Text, bool OnNo) : ValueFormat;

	/// <summary>&lt;read(Read)(&gt;)(90)&gt;. Integer compare of the read percentage.</summary>
	public sealed record ReadThreshold(string Text, Comparison Op, int Percent) : ValueFormat;

	/// <summary>&lt;first(Series)&gt;. Upper-cased first letter of Source after a leading article.</summary>
	public sealed record FirstLetterOf(Field Source) : ValueFormat;

	/// <summary>&lt;counter(1)(1)(3)&gt;. Renders Start + Step * the book's ordinal, so the token keeps no state.</summary>
	public sealed record Counter(int Start, int Step, int Pad) : ValueFormat;

	/// <summary>&lt;ReleasedDate(yyyy-MM)&gt;. Null pattern = invariant "yyyy-MM-dd" (2.x used the current culture, which emits '/' and ':').</summary>
	public sealed record DateFormat(string? Pattern) : ValueFormat;

	/// <summary>&lt;Custom(key)&gt;.</summary>
	public sealed record CustomKey(string Key) : ValueFormat;

	/// <summary>&lt;writer(, )(issue)&gt; / &lt;writer( &amp; )(series)&gt;. The answer may ask for one folder per item.</summary>
	public sealed record MultiValue(string Separator, AnswerScopeKind Scope) : ValueFormat;
}

public enum Comparison { Less, Equal, Greater }

public enum AnswerScopeKind { Issue, Series }

/// <summary>
/// '?' and '!' tests. Compared against the formatted value before cleaning, so &lt;?format(Trade: Paperback)&gt; can match.
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

	public bool Passes(string value) => this switch
	{
		EqualsText equals => value == equals.Text,
		NotEqualsText notEquals => value != notEquals.Text,
		MatchesStart matches => matches.Pattern.IsMatch(value),
		NotMatchesStart notMatches => !notMatches.Pattern.IsMatch(value),
		_ => value.Length == 0,
	};
}
