using System.Collections.Generic;
using System.Linq;

namespace LibraryOrganizer;

/// <summary>The planning module's public face. Pure: no UI, no writes. Disk is read through IFileProbe only.</summary>
public static class Organizer
{
	/// <summary>
	/// What would happen to every selected book under every profile, given what the user has decided so far.
	/// The same input and decisions always give the same plan. To change a plan, plan again with more decisions.
	/// </summary>
	public static Plan Plan(OrganizeInput input, Decisions decisions) => new Planner(input, decisions).Run();

	/// <summary>
	/// One profile, one book, for the configure form. Ignores other profiles, the disk and conflicts. An unanswered
	/// multi-value token renders as a placeholder. A book the profile would skip or fail still renders its path where it
	/// can, and <see cref="PreviewResult.Problem"/> says why it would not be organized.
	/// </summary>
	public static PreviewResult Preview(Profile profile, BookRecord book, SeriesIndex series, AnswerBook answers)
	{
		FilterVerdict verdict = profile.Filter.Evaluate(book, series);
		var filter = new FilterVerdictText(verdict.Accepted, verdict.Reason ?? "");
		Admission admission = BookPath.Admit(profile, book, series, fileExists: book.Location is not BookLocation.File { Exists: false });
		string? problem = admission switch
		{
			Admission.Skip skip => skip.Reason,
			Admission.Fail fail => fail.Reason,
			Admission.Accept accept => accept.FailReason,
			_ => null,
		};
		var context = new RenderContext(profile.Style, series, answers, profile.DontAskWhenOneValue, Ordinal: 0, Preview: true);
		return BookPath.Render(profile, book, (admission as Admission.Accept)?.FailedRoot, context) switch
		{
			PathRender.Path path => new PreviewResult(
				string.Join(@"\", [path.Root.Value.TrimEnd('\\'), .. path.Levels, path.FileName]), problem, filter, path.Placeholders),
			PathRender.Fails fails => new PreviewResult(null, problem ?? fails.Reason, filter, []),
			_ => new PreviewResult(null, problem, filter, []),
		};
	}
}

/// <param name="RunOrder">Profiles in the order the user chose. The last accepting Move profile wins.</param>
/// <param name="Selection">The books to organize, in selection order: it drives counters and conflict precedence.</param>
/// <param name="LibraryFiles">Every library book's file, so a conflict can name the book that holds the target.</param>
public sealed record OrganizeInput(
	IReadOnlyList<Profile> RunOrder,
	IReadOnlyList<BookRecord> Selection,
	SeriesIndex Series,
	IFileProbe Disk,
	IReadOnlyDictionary<AbsPath, BookId> LibraryFiles,
	int MaxPathLength = 259);

/// <param name="Path">The rendered path, with "‹field?›" where a multi-value token has no answer. Null when it cannot render.</param>
/// <param name="Problem">Why the profile would skip or fail the book. Null when it would organize it.</param>
/// <param name="Unanswered">The multi-value fields shown as placeholders.</param>
public sealed record PreviewResult(string? Path, string? Problem, FilterVerdictText Filter, IReadOnlyList<Field> Unanswered);

public sealed record FilterVerdictText(bool Accepted, string Reason);

/// <summary>Whether a profile takes a book, decided before anything renders.</summary>
internal abstract record Admission
{
	private Admission() { }

	public sealed record Skip(string Reason) : Admission;
	public sealed record Fail(string Reason) : Admission;

	/// <param name="FailedRoot">Set with FailReason: a required field is empty and the profile moves such books under this folder.</param>
	public sealed record Accept(AbsPath? FailedRoot, string? FailReason) : Admission;
}

internal abstract record PathRender
{
	private PathRender() { }

	/// <summary>Names are cleaned. In a preview a name can hold a placeholder, which is not a legal file name.</summary>
	public sealed record Path(AbsPath Root, IReadOnlyList<string> Levels, string FileName, IReadOnlyList<Field> Placeholders) : PathRender;
	public sealed record Asks(IReadOnlyList<Question> Questions) : PathRender;
	public sealed record Fails(string Reason) : PathRender;
}

/// <summary>One profile applied to one book: the checks and the path. Shared by the planner and the preview.</summary>
internal static class BookPath
{
	public static Admission Admit(Profile profile, BookRecord book, SeriesIndex series, bool fileExists)
	{
		if (book.Location is BookLocation.File file && !fileExists)
		{
			return new Admission.Fail($"The file does not exist: {file.Path}");
		}
		FilterVerdict verdict = profile.Filter.Evaluate(book, series);
		if (!verdict.Accepted)
		{
			return new Admission.Skip(verdict.Reason ?? "Filtered out");
		}
		if (book.Location is BookLocation.Fileless fileless)
		{
			if (profile.FilelessCovers is null)
			{
				return new Admission.Skip("The profile does not organize fileless books");
			}
			if (!fileless.HasCustomCover)
			{
				return new Admission.Fail("The fileless book has no custom cover");
			}
		}
		if (profile.Required is { } required)
		{
			List<string> empty = required.Fields.Where(field => IsEmpty(FieldReader.Read(field, book, series))).Select(field => field.DisplayName).ToList();
			if (empty.Count > 0)
			{
				string reason = "Required fields are empty: " + string.Join(", ", empty);
				return required.Route is RequiredFields.FailRoute.MoveUnder moveUnder ? new Admission.Accept(moveUnder.Folder, reason) : new Admission.Fail(reason);
			}
		}
		return new Admission.Accept(null, null);
	}

	public static PathRender Render(Profile profile, BookRecord book, AbsPath? failedRoot, RenderContext context)
	{
		AbsPath? source = (book.Location as BookLocation.File)?.Path;
		(AbsPath? baseFolder, Template? folder, Template? fileName) = profile.Destination switch
		{
			Destination.FolderAndName both => ((AbsPath?)both.BaseFolder, both.Folder, both.FileName),
			Destination.FolderOnly folderOnly => (folderOnly.BaseFolder, folderOnly.Folder, null),
			Destination.NameOnly nameOnly => (null, null, nameOnly.FileName),
			_ => default((AbsPath?, Template?, Template?)),
		};
		var questions = new List<Question>();
		var placeholders = new List<Field>();

		IReadOnlyList<Piece>? Pieces(Template template)
		{
			RenderOutcome outcome = Renderer.Render(template, book, context);
			if (outcome is RenderOutcome.Done done)
			{
				placeholders.AddRange(done.Pieces.OfType<Piece.Placeholder>().Select(piece => Fields.ByTemplateName(piece.Name)).OfType<Field>());
				return done.Pieces;
			}
			if (outcome is RenderOutcome.NeedsAnswers needs)
			{
				questions.AddRange(needs.Questions.Where(question => questions.All(asked => asked.Key != question.Key)));
			}
			return null;
		}

		AbsPath root;
		IReadOnlyList<string> levels = [];
		if (folder is not null && baseFolder is { } configured)
		{
			root = failedRoot ?? configured;
			if (Pieces(folder) is { } pieces)
			{
				levels = context.Style.CleanFolder(pieces);
			}
		}
		else if (source is { } current)
		{
			root = failedRoot ?? current.Parent;
		}
		else
		{
			return new PathRender.Fails("A fileless book has no folder to keep");
		}

		string name = "";
		if (fileName is null)
		{
			if (source is not { } current)
			{
				return new PathRender.Fails("A fileless book has no file name to keep");
			}
			name = current.Name;
		}
		else if (Pieces(fileName) is { } pieces)
		{
			string stem = context.Style.CleanFileName(pieces);
			if (stem.Length == 0)
			{
				return new PathRender.Fails("The file name is blank");
			}
			name = stem + (source?.Extension ?? CoverExtension(profile.FilelessCovers));
		}
		return questions.Count > 0 ? new PathRender.Asks(questions) : new PathRender.Path(root, levels, name, placeholders.Distinct().ToList());
	}

	private static string CoverExtension(CoverFormat? format) => format is { } known ? "." + known.ToString().ToLowerInvariant() : "";

	private static bool IsEmpty(FieldValue value) => value.IsUnset || value is FieldValue.Text text && string.IsNullOrWhiteSpace(text.Value);
}
