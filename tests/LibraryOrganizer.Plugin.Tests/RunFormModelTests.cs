using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibraryOrganizer.Plugin.Forms.Run;

namespace LibraryOrganizer.Plugin.Tests;

internal sealed class FakeDisk : IFileProbe
{
	private readonly HashSet<AbsPath> files = [];

	public void With(string path) => files.Add(AbsPath.TryParse(path)!.Value);

	public bool FileExists(AbsPath path) => files.Contains(path);

	public bool FolderExists(AbsPath path) => true;

	public string? ActualName(AbsPath path) => files.Contains(path) ? path.Name : null;

	public long? FileSize(AbsPath path) => files.Contains(path) ? 1 : null;

	public IReadOnlyList<AbsPath> Children(AbsPath folder) => [];
}

internal sealed class RunShelf
{
	public FakeDisk Disk { get; } = new();
	public List<BookRecord> Selection { get; } = [];

	public static Profile Profile(string name = "Library", string fileTemplate = "{<series>}{ #<number>}", Func<ProfileSettings, ProfileSettings>? edit = null)
	{
		var settings = new ProfileSettings
		{
			Name = name,
			BaseFolder = @"D:\Comics",
			FolderTemplate = @"{<publisher>}\{<series>}",
			FileTemplate = fileTemplate,
			PruneEmptyFolders = false,
		};
		CompileResult result = (edit?.Invoke(settings) ?? settings).Compile();
		return result.Profile ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Message)));
	}

	public BookRecord Add(string path, string series, string number, string publisher = "Vertigo", string? writer = null, bool onDisk = true)
	{
		FieldValue[] values = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		values[Fields.Series.Ordinal] = new FieldValue.Text(series);
		values[Fields.Publisher.Ordinal] = new FieldValue.Text(publisher);
		values[Fields.Number.Ordinal] = new FieldValue.Issue(new IssueNumber(number));
		values[Fields.Writer.Ordinal] = FieldValue.ParseItems(writer);
		var book = new BookRecord(
			new BookId(Guid.NewGuid()), new BookLocation.File(AbsPath.TryParse(path)!.Value, Exists: true), $"{series} #{number}", values, new Dictionary<string, string>());
		if (onDisk)
		{
			Disk.With(path);
		}
		Selection.Add(book);
		return book;
	}

	public Plan Plan(Decisions decisions, params Profile[] runOrder)
		=> Organizer.Plan(new OrganizeInput(runOrder, Selection, SeriesIndex.Build(Selection), Disk, new Dictionary<AbsPath, BookId>()), decisions);

	public Plan Plan(params Profile[] runOrder) => Plan(Decisions.None, runOrder);

	/// <summary>Sandman moves, Hellblazer is in place, Preacher meets an existing file, two Transmetropolitan scans collide, one file is gone.</summary>
	public static RunShelf Mixed(out Profile profile)
	{
		var shelf = new RunShelf();
		profile = Profile();
		shelf.Add(@"D:\Incoming\sandman.cbz", "Sandman", "1");
		shelf.Add(@"D:\Comics\Vertigo\Hellblazer\Hellblazer #1.cbz", "Hellblazer", "1");
		shelf.Add(@"D:\Incoming\preacher.cbz", "Preacher", "1");
		shelf.Disk.With(@"D:\Comics\Vertigo\Preacher\Preacher #1.cbz");
		shelf.Add(@"D:\Incoming\trans-a.cbz", "Transmetropolitan", "1");
		shelf.Add(@"D:\Incoming\trans-b.cbz", "Transmetropolitan", "1");
		shelf.Add(@"D:\Incoming\gone.cbz", "Y", "1", onDisk: false);
		return shelf;
	}
}

public class ReviewRowTests
{
	[Test]
	public async Task A_planned_row_names_the_action_the_source_and_the_target()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);

		ReviewRow row = ReviewRows.Build(shelf.Plan(profile)).First(row => row.Book == "Sandman #1");

		await Assert.That(row.Outcome).IsEqualTo(ReviewOutcome.Planned);
		await Assert.That(row.OutcomeText).IsEqualTo("Planned");
		await Assert.That(row.Profile).IsEqualTo("Library");
		await Assert.That(row.Action).IsEqualTo("Move");
		await Assert.That(row.From).IsEqualTo(@"D:\Incoming\sandman.cbz");
		await Assert.That(row.To).IsEqualTo(@"D:\Comics\Vertigo\Sandman\Sandman #1.cbz");
		await Assert.That(row.Conflict).IsNull();
	}

	[Test]
	public async Task A_copy_profile_row_says_copy_and_whether_the_library_gets_the_copy()
	{
		var shelf = new RunShelf();
		shelf.Add(@"D:\Incoming\sandman.cbz", "Sandman", "1");
		Profile plain = RunShelf.Profile("Backup", edit: settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: false) });
		Profile added = RunShelf.Profile("Mirror", edit: settings => settings with { Action = new ProfileAction.Copy(AddToLibrary: true), BaseFolder = @"E:\Mirror" });

		IReadOnlyList<ReviewRow> rows = ReviewRows.Build(shelf.Plan(plain, added));

		await Assert.That(rows.Select(row => row.Action).ToArray()).IsEquivalentTo(new[] { "Copy", "Copy and add to library" });
	}

	[Test]
	public async Task Outcomes_that_do_no_work_say_why_and_have_no_paths()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Profile imageOnly = RunShelf.Profile("Image only", edit: settings => settings with
		{
			RuleMode = RuleMode.Only,
			Rules = new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition(Fields.Publisher.Key, Operator.Is, "Image")]),
		});

		IReadOnlyList<ReviewRow> rows = ReviewRows.Build(shelf.Plan(profile, imageOnly));

		ReviewRow inPlace = rows.Single(row => row.Book == "Hellblazer #1" && row.Profile == "Library");
		ReviewRow skipped = rows.Single(row => row.Book == "Sandman #1" && row.Profile == "Image only");
		ReviewRow failed = rows.Single(row => row.Book == "Y #1" && row.Profile == "Library");
		await Assert.That(inPlace.OutcomeText).IsEqualTo("Already in place");
		await Assert.That(skipped.Outcome).IsEqualTo(ReviewOutcome.Skipped);
		await Assert.That(skipped.OutcomeText).StartsWith("Skipped: ");
		await Assert.That(failed.Outcome).IsEqualTo(ReviewOutcome.Failed);
		await Assert.That(failed.OutcomeText).Contains(@"D:\Incoming\gone.cbz");
		await Assert.That(new[] { inPlace, skipped, failed }.All(row => row.From.Length == 0 && row.To.Length == 0 && row.Action.Length == 0)).IsTrue();
	}

	[Test]
	public async Task A_book_a_later_move_profile_takes_is_overridden_and_names_that_profile()
	{
		var shelf = new RunShelf();
		shelf.Add(@"D:\Incoming\saga.cbz", "Saga", "1", publisher: "Image");
		Profile first = RunShelf.Profile("First");
		Profile second = RunShelf.Profile("Second", edit: settings => settings with { BaseFolder = @"D:\Other" });

		IReadOnlyList<ReviewRow> rows = ReviewRows.Build(shelf.Plan(first, second));

		ReviewRow overridden = rows.Single(row => row.Profile == "First");
		await Assert.That(overridden.Outcome).IsEqualTo(ReviewOutcome.Overridden);
		await Assert.That(overridden.OutcomeText).IsEqualTo("Overridden: by Second");
		await Assert.That(rows.Single(row => row.Profile == "Second").Outcome).IsEqualTo(ReviewOutcome.Planned);
	}

	[Test]
	public async Task A_book_that_needs_an_answer_is_waiting_and_names_the_field()
	{
		var shelf = new RunShelf();
		shelf.Add(@"D:\Incoming\x.cbz", "Uncanny X-Men", "1", publisher: "Marvel", writer: "Chris Claremont, Len Wein");
		Profile profile = RunShelf.Profile(fileTemplate: "{<series>}{ (<writer(, )(issue)>)}");

		Plan plan = shelf.Plan(profile);
		ReviewRow row = ReviewRows.Build(plan).Single();

		await Assert.That(row.Outcome).IsEqualTo(ReviewOutcome.Waiting);
		await Assert.That(row.OutcomeText).IsEqualTo("Waiting: needs a choice for Writer");
		await Assert.That(plan.Questions.Single().Waiting).IsEqualTo(1);
	}

	[Test]
	public async Task A_conflict_blocks_its_row_and_offers_only_the_choices_the_plan_allows()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);

		IReadOnlyList<ReviewRow> rows = ReviewRows.Build(shelf.Plan(profile));

		ReviewRow exists = rows.Single(row => row.Book == "Preacher #1");
		ReviewRow collides = rows.Where(row => row.Book == "Transmetropolitan #1").Last();
		await Assert.That(exists.Outcome).IsEqualTo(ReviewOutcome.Blocked);
		await Assert.That(exists.OutcomeText).IsEqualTo("Blocked: A file already exists");
		await Assert.That(exists.Resolution).IsEqualTo("Choose...");
		await Assert.That(exists.Conflict!.Allowed.ToArray()).IsEquivalentTo(new[] { ResolutionChoice.Skip, ResolutionChoice.Replace, ResolutionChoice.KeepBoth, ResolutionChoice.UsePath });
		await Assert.That(collides.Outcome).IsEqualTo(ReviewOutcome.Blocked);
		await Assert.That(collides.Conflict!.Allowed.Contains(ResolutionChoice.Replace)).IsFalse();
		await Assert.That(exists.To).IsEqualTo(@"D:\Comics\Vertigo\Preacher\Preacher #1.cbz");
	}

	[Test]
	public async Task Keep_both_shows_the_new_name_the_file_would_get()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Conflict conflict = ReviewRows.Build(shelf.Plan(profile)).Single(row => row.Book == "Preacher #1").Conflict!;

		await Assert.That(ReviewRows.ChoiceLabel(ResolutionChoice.KeepBoth, conflict)).IsEqualTo("Keep both, as Preacher #1 (1).cbz");
		await Assert.That(ReviewRows.ChoiceLabel(ResolutionChoice.Skip, conflict)).IsEqualTo("Skip this book");
	}

	[Test]
	public async Task Counts_and_filters_follow_the_outcomes_of_the_rows()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		IReadOnlyList<ReviewRow> rows = ReviewRows.Build(shelf.Plan(profile));

		IReadOnlyDictionary<ReviewOutcome, int> counts = ReviewRows.Counts(rows);

		await Assert.That(counts[ReviewOutcome.Planned]).IsEqualTo(2);
		await Assert.That(counts[ReviewOutcome.AlreadyInPlace]).IsEqualTo(1);
		await Assert.That(counts[ReviewOutcome.Blocked]).IsEqualTo(2);
		await Assert.That(counts[ReviewOutcome.Failed]).IsEqualTo(1);
		await Assert.That(counts[ReviewOutcome.Waiting]).IsEqualTo(0);
		await Assert.That(counts.Values.Sum()).IsEqualTo(rows.Count);
		await Assert.That(ReviewRows.Filter(rows, ReviewFilter.All).Length).IsEqualTo(rows.Count);

		int[] blocked = ReviewRows.Filter(rows, new ReviewFilter(new HashSet<ReviewOutcome> { ReviewOutcome.Blocked }, ""));
		await Assert.That(blocked.Select(index => rows[index].Book).ToArray()).IsEquivalentTo(new[] { "Preacher #1", "Transmetropolitan #1" });

		int[] searched = ReviewRows.Filter(rows, new ReviewFilter(new HashSet<ReviewOutcome>(), "SANDMAN"));
		await Assert.That(searched.Length).IsEqualTo(1);
		await Assert.That(ReviewRows.Filter(rows, new ReviewFilter(new HashSet<ReviewOutcome> { ReviewOutcome.Failed }, "sandman")).Length).IsEqualTo(0);
		await Assert.That(ReviewRows.Filter(rows, new ReviewFilter(new HashSet<ReviewOutcome>(), @"comics\vertigo\hellblazer")).Length).IsEqualTo(0);
	}

	[Test]
	public async Task The_filter_text_matches_paths()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		IReadOnlyList<ReviewRow> rows = ReviewRows.Build(shelf.Plan(profile));

		int[] found = ReviewRows.Filter(rows, new ReviewFilter(new HashSet<ReviewOutcome>(), @"incoming\sandman"));

		await Assert.That(found.Select(index => rows[index].Book).ToArray()).IsEquivalentTo(new[] { "Sandman #1" });
	}
}

public class ReviewDecisionTests
{
	[Test]
	public async Task Skip_turns_the_blocked_row_into_a_skipped_one()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Conflict conflict = ReviewRows.Build(shelf.Plan(profile)).Single(row => row.Book == "Preacher #1").Conflict!;

		Decisions decisions = ReviewDecisions.Resolve(Decisions.None, conflict, ResolutionChoice.Skip);
		ReviewRow row = ReviewRows.Build(shelf.Plan(decisions, profile)).Single(row => row.Book == "Preacher #1");

		await Assert.That(row.Outcome).IsEqualTo(ReviewOutcome.Skipped);
		await Assert.That(row.Resolution).IsEqualTo("Skip");
	}

	[Test]
	public async Task Keep_both_plans_the_move_to_the_numbered_name()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Conflict conflict = ReviewRows.Build(shelf.Plan(profile)).Single(row => row.Book == "Preacher #1").Conflict!;

		ReviewRow row = ReviewRows.Build(shelf.Plan(ReviewDecisions.Resolve(Decisions.None, conflict, ResolutionChoice.KeepBoth), profile)).Single(row => row.Book == "Preacher #1");

		await Assert.That(row.Outcome).IsEqualTo(ReviewOutcome.Planned);
		await Assert.That(row.To).IsEqualTo(@"D:\Comics\Vertigo\Preacher\Preacher #1 (1).cbz");
		await Assert.That(row.Resolution).IsEqualTo("Keep both: Preacher #1 (1).cbz");
	}

	[Test]
	public async Task Replace_reads_as_replace_then_move()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Conflict conflict = ReviewRows.Build(shelf.Plan(profile)).Single(row => row.Book == "Preacher #1").Conflict!;

		ReviewRow row = ReviewRows.Build(shelf.Plan(ReviewDecisions.Resolve(Decisions.None, conflict, ResolutionChoice.Replace), profile)).Single(row => row.Book == "Preacher #1");

		await Assert.That(row.Action).IsEqualTo("Replace, then move");
		await Assert.That(row.From).IsEqualTo(@"D:\Incoming\preacher.cbz");
	}

	[Test]
	public async Task A_typed_path_is_where_the_book_goes()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Conflict conflict = ReviewRows.Build(shelf.Plan(profile)).Single(row => row.Book == "Preacher #1").Conflict!;
		AbsPath typed = AbsPath.TryParse(@"D:\Comics\Vertigo\Preacher\Preacher #1 scan.cbz")!.Value;

		ReviewRow row = ReviewRows.Build(shelf.Plan(ReviewDecisions.Resolve(Decisions.None, conflict, ResolutionChoice.UsePath, typed), profile)).Single(row => row.Book == "Preacher #1");

		await Assert.That(row.Outcome).IsEqualTo(ReviewOutcome.Planned);
		await Assert.That(row.To).IsEqualTo(typed.Value);
	}

	[Test]
	public async Task A_choice_the_conflict_does_not_allow_is_refused_and_a_typed_path_needs_a_path()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		Conflict collides = ReviewRows.Build(shelf.Plan(profile)).Last(row => row.Book == "Transmetropolitan #1").Conflict!;
		Conflict exists = ReviewRows.Build(shelf.Plan(profile)).Single(row => row.Book == "Preacher #1").Conflict!;

		await Assert.That(() => ReviewDecisions.Resolve(Decisions.None, collides, ResolutionChoice.Replace)).Throws<ArgumentException>();
		await Assert.That(() => ReviewDecisions.Resolve(Decisions.None, exists, ResolutionChoice.UsePath)).Throws<ArgumentException>();
	}

	[Test]
	public async Task The_default_for_all_conflicts_resolves_every_open_conflict_and_the_plan_can_be_approved()
	{
		RunShelf shelf = RunShelf.Mixed(out Profile profile);
		await Assert.That(shelf.Plan(profile).Approve()).IsNull();
		Decisions decisions = ReviewDecisions.Default(Decisions.None, ConflictDefault.KeepBoth);

		Plan plan = shelf.Plan(decisions, profile);

		await Assert.That(ReviewDecisions.DefaultOf(decisions)).IsEqualTo(ConflictDefault.KeepBoth);
		await Assert.That(ReviewRows.Counts(ReviewRows.Build(plan))[ReviewOutcome.Blocked]).IsEqualTo(0);
		await Assert.That(ReviewDecisions.DefaultOf(ReviewDecisions.Default(decisions, ConflictDefault.Ask))).IsEqualTo(ConflictDefault.Ask);
		await Assert.That(ReviewDecisions.DefaultOf(Decisions.None)).IsEqualTo(ConflictDefault.Ask);
	}

	[Test]
	public async Task An_answer_unblocks_the_waiting_row_and_the_question_leaves_the_list()
	{
		var shelf = new RunShelf();
		shelf.Add(@"D:\Incoming\x.cbz", "Uncanny X-Men", "1", publisher: "Marvel", writer: "Chris Claremont, Len Wein");
		Profile profile = RunShelf.Profile(fileTemplate: "{<series>}{ (<writer(, )(issue)>)}");
		Question question = shelf.Plan(profile).Questions.Single();
		var answer = new ItemSelection(question.Choices, []);
		answer.Add(["Len Wein", "Chris Claremont"]);

		Plan plan = shelf.Plan(Decisions.None.WithAnswer(question.Key, answer.ToAnswer(question.Key.Scope, asFolders: false, everyIssue: false, saveAsPreset: false, askWhenMore: false)), profile);

		await Assert.That(plan.Questions.Count).IsEqualTo(0);
		await Assert.That(ReviewRows.Build(plan).Single().To).IsEqualTo(@"D:\Comics\Marvel\Uncanny X-Men\Uncanny X-Men (Len Wein, Chris Claremont).cbz");
		await Assert.That(plan.Approve()).IsNotNull();
	}
}

public class PathCheckTests
{
	[Test]
	[Arguments(@"D:\Comics\Series\Book.cbz", null)]
	[Arguments("", "Type the full path of the file.")]
	[Arguments(@"Series\Book.cbz", @"The path must be a full path to a file, such as D:\Comics\Series\Book.cbz.")]
	[Arguments(@"D:\Comics\Se*ries\Book.cbz", "The path cannot contain < > | * ? or \".")]
	[Arguments(@"D:\Comics\Series: One\Book.cbz", "The path cannot contain : after the drive letter.")]
	[Arguments(@"D:\", @"The path must be a full path to a file, such as D:\Comics\Series\Book.cbz.")]
	public async Task The_editor_accepts_a_full_legal_path_and_says_what_is_wrong_otherwise(string text, string? expected)
	{
		string? error = PathCheck.Validate(text, 259, out AbsPath? path);

		await Assert.That(error).IsEqualTo(expected);
		await Assert.That(path.HasValue).IsEqualTo(expected is null);
	}

	[Test]
	public async Task A_path_over_the_limit_reports_its_length_and_the_limit()
	{
		string text = @"D:\Comics\" + new string('a', 40) + ".cbz";

		string? error = PathCheck.Validate(text, 40, out AbsPath? path);

		await Assert.That(error).IsEqualTo($"The path has {text.Length} characters, and the limit is 40.");
		await Assert.That(path.HasValue).IsFalse();
		await Assert.That(PathCheck.Validate(text, text.Length, out _)).IsNull();
	}

	[Test]
	public async Task A_folder_part_of_248_characters_or_more_is_refused_even_when_the_whole_path_fits()
	{
		string text = @"D:\" + new string('a', 250) + @"\b.cbz";

		string? error = PathCheck.Validate(text, 400, out _);

		await Assert.That(error).IsEqualTo("The folder part has 253 characters, and the limit is 247.");
	}
}

public class ItemSelectionTests
{
	private static readonly string[] Writers = ["Neil Gaiman", "Mike Dringenberg", "Sam Kieth"];

	[Test]
	public async Task Preselected_items_start_selected_and_the_rest_are_available_sorted()
	{
		var items = new ItemSelection(Writers, ["Sam Kieth", "Nobody Known"]);

		await Assert.That(items.Selected.ToArray()).IsEquivalentTo(new[] { "Sam Kieth" });
		await Assert.That(items.Available.ToArray()).IsEquivalentTo(new[] { "Mike Dringenberg", "Neil Gaiman" });
	}

	[Test]
	public async Task Adding_keeps_the_users_order_and_removing_returns_the_item_to_available()
	{
		var items = new ItemSelection(Writers, []);

		items.Add(["Sam Kieth", "Neil Gaiman", "sam kieth"]);
		items.Remove(["Sam Kieth"]);
		items.Add(["Sam Kieth"]);

		await Assert.That(items.Selected.ToArray()).IsEquivalentTo(new[] { "Neil Gaiman", "Sam Kieth" });
		await Assert.That(items.Available.ToArray()).IsEquivalentTo(new[] { "Mike Dringenberg" });
	}

	[Test]
	public async Task Move_up_and_down_reorder_the_selection_and_stop_at_the_edges()
	{
		var items = new ItemSelection(Writers, Writers);

		items.Shift(["Sam Kieth"], -1);
		await Assert.That(items.Selected.ToArray()).IsEquivalentTo(new[] { "Neil Gaiman", "Sam Kieth", "Mike Dringenberg" });
		items.Shift(["Neil Gaiman"], -1);
		await Assert.That(items.Selected[0]).IsEqualTo("Neil Gaiman");
		items.Shift(["Mike Dringenberg"], 1);
		await Assert.That(items.Selected[2]).IsEqualTo("Mike Dringenberg");
		items.Shift(["Neil Gaiman"], 1);
		await Assert.That(items.Selected.ToArray()).IsEquivalentTo(new[] { "Sam Kieth", "Neil Gaiman", "Mike Dringenberg" });
	}

	[Test]
	public async Task An_issue_answer_can_save_a_preset_and_a_series_answer_can_cover_every_issue()
	{
		var items = new ItemSelection(Writers, ["Neil Gaiman"]);
		AnswerScope issue = new AnswerScope.Issue(new BookId(Guid.NewGuid()));
		AnswerScope series = new AnswerScope.Series(new SeriesKey("Vertigo", "Sandman", null));

		Answer forIssue = items.ToAnswer(issue, asFolders: true, everyIssue: true, saveAsPreset: true, askWhenMore: true);
		Answer forSeries = items.ToAnswer(series, asFolders: false, everyIssue: true, saveAsPreset: true, askWhenMore: true);

		await Assert.That(AnswerComparer.Instance.Equals(forIssue, new Answer(["Neil Gaiman"], AsFolders: true, EveryIssue: false, SaveAsPreset: true, PresetAskWhenMore: true))).IsTrue();
		await Assert.That(AnswerComparer.Instance.Equals(forSeries, new Answer(["Neil Gaiman"], AsFolders: false, EveryIssue: true, SaveAsPreset: false, PresetAskWhenMore: false))).IsTrue();
	}

	[Test]
	public async Task An_empty_selection_saves_no_preset_and_a_preset_without_the_box_does_not_ask_again()
	{
		AnswerScope issue = new AnswerScope.Issue(new BookId(Guid.NewGuid()));

		Answer none = new ItemSelection(Writers, []).ToAnswer(issue, asFolders: false, everyIssue: false, saveAsPreset: true, askWhenMore: true);
		Answer preset = new ItemSelection(Writers, ["Sam Kieth"]).ToAnswer(issue, asFolders: false, everyIssue: false, saveAsPreset: true, askWhenMore: false);

		await Assert.That(none.Selected.Count).IsEqualTo(0);
		await Assert.That(none.SaveAsPreset).IsFalse();
		await Assert.That(none.PresetAskWhenMore).IsFalse();
		await Assert.That(preset.SaveAsPreset).IsTrue();
		await Assert.That(preset.PresetAskWhenMore).IsFalse();
	}
}

public class ProfileOrderTests
{
	private static ProfileSettings Named(string name) => new() { Name = name };

	[Test]
	public async Task The_picker_starts_from_the_last_run_order_and_drops_profiles_that_are_gone()
	{
		ProfileSettings a = Named("A");
		ProfileSettings b = Named("B");
		ProfileSettings c = Named("C");

		IReadOnlyList<ProfileSettings> start = ProfileOrder.Start([a, b, c], [c.Id, new ProfileId(Guid.NewGuid()), a.Id, c.Id]);

		await Assert.That(start.Select(profile => profile.Name).ToArray()).IsEquivalentTo(new[] { "C", "A" });
	}

	[Test]
	public async Task With_no_usable_last_order_the_first_profile_is_chosen_so_OK_is_possible()
	{
		ProfileSettings a = Named("A");
		ProfileSettings b = Named("B");

		await Assert.That(ProfileOrder.Start([a, b], []).Single().Name).IsEqualTo("A");
		await Assert.That(ProfileOrder.Start([a, b], [new ProfileId(Guid.NewGuid())]).Single().Name).IsEqualTo("A");
		await Assert.That(ProfileOrder.Start([], []).Count).IsEqualTo(0);
	}

	[Test]
	public async Task Shifting_moves_the_selected_block_and_leaves_an_edge_block_alone()
	{
		List<string> items = ["a", "b", "c", "d"];

		int[] up = ProfileOrder.Shift(items, [1, 2], -1);
		await Assert.That(string.Concat(items)).IsEqualTo("bcad");
		await Assert.That(up).IsEquivalentTo(new[] { 0, 1 });

		int[] stuck = ProfileOrder.Shift(items, [0, 1], -1);
		await Assert.That(string.Concat(items)).IsEqualTo("bcad");
		await Assert.That(stuck).IsEquivalentTo(new[] { 0, 1 });

		int[] down = ProfileOrder.Shift(items, [2], 1);
		await Assert.That(string.Concat(items)).IsEqualTo("bcda");
		await Assert.That(down).IsEquivalentTo(new[] { 3 });
		await Assert.That(ProfileOrder.Shift(items, [3], 1)).IsEquivalentTo(new[] { 3 });
	}
}

/// <summary>Answer holds a list, so the record's own equality compares list references.</summary>
internal sealed class AnswerComparer : IEqualityComparer<Answer>
{
	public static AnswerComparer Instance { get; } = new();

	public bool Equals(Answer? x, Answer? y)
		=> x is not null && y is not null
			&& x.Selected.SequenceEqual(y.Selected)
			&& x.AsFolders == y.AsFolders
			&& x.EveryIssue == y.EveryIssue
			&& x.SaveAsPreset == y.SaveAsPreset
			&& x.PresetAskWhenMore == y.PresetAskWhenMore;

	public int GetHashCode(Answer obj) => obj.Selected.Count;
}
