using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static LibraryOrganizer.Tests.MemoryFileSystem;

namespace LibraryOrganizer.Tests;

public class PlannerTests
{
	private const string Incoming = @"D:\Incoming\seven.cbz";
	private const string Target = @"D:\Comics\Marvel\X-Men\X-Men #7.cbz";

	private static readonly RuleSettings.Group PublisherIsDc = new(Combinator.Any, [new RuleSettings.Condition("publisher", Operator.Is, "DC")]);

	private static Profile WriterInName(string name = "Main", ProfileAction? action = null)
		=> TestProfiles.Make(name, edit: settings => settings with
		{
			FileTemplate = "{<series>}{ #<number>}{ by <writer(, )(issue)>}",
			Action = action ?? new ProfileAction.Move(),
		});

	[Test]
	public async Task A_move_profile_moves_the_file_to_the_rendered_path()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);

		Plan plan = shelf.Plan(TestProfiles.Make());

		await Assert.That(plan.Operations.Script()).IsEqualTo($"move {Incoming} -> {Target}");
		await Assert.That(plan.Conflicts.Count).IsEqualTo(0);
		await Assert.That(plan.Questions.Count).IsEqualTo(0);
	}

	[Test]
	public async Task The_last_accepting_move_profile_wins_and_earlier_ones_are_overridden()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		Profile general = TestProfiles.Make("General", @"D:\General");
		Profile specific = TestProfiles.Make("Specific", @"D:\Specific");

		Plan plan = shelf.Plan(general, specific);

		await Assert.That(plan.OutcomeOf(book, general)).IsEqualTo(new EntryOutcome.Overridden("Specific"));
		await Assert.That(plan.Operations.Script()).IsEqualTo($@"move {Incoming} -> D:\Specific\Marvel\X-Men\X-Men #7.cbz");
		await Assert.That(plan.Tally().Single(tally => tally.Profile == "General").Skipped).IsEqualTo(1);
	}

	[Test]
	public async Task A_later_move_profile_that_filters_the_book_out_leaves_it_to_the_earlier_one()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		Profile general = TestProfiles.Make("General", @"D:\General");
		Profile dcOnly = TestProfiles.Make("DC", @"D:\DC", settings => settings with { RuleMode = RuleMode.Only, Rules = PublisherIsDc });

		Plan plan = shelf.Plan(general, dcOnly);

		await Assert.That(plan.OutcomeOf(book, dcOnly)).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(plan.TargetOf(book, general)).IsEqualTo(@"D:\General\Marvel\X-Men\X-Men #7.cbz");
	}

	[Test]
	public async Task When_the_winning_profile_renders_a_blank_name_the_previous_move_profile_moves_the_book()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		Profile general = TestProfiles.Make("General", @"D:\General");
		Profile blank = TestProfiles.Make("Blank", @"D:\Blank", settings => settings with { FileTemplate = "{<title>}" });

		Plan plan = shelf.Plan(general, blank);

		await Assert.That(plan.OutcomeOf(book, blank)).IsTypeOf<EntryOutcome.Failed>();
		await Assert.That(plan.Operations.Script()).IsEqualTo($@"move {Incoming} -> D:\General\Marvel\X-Men\X-Men #7.cbz");
	}

	[Test]
	public async Task A_blank_file_name_fails_the_book()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		Profile blank = TestProfiles.Make(edit: settings => settings with { FileTemplate = "{<title>}" });

		Plan plan = shelf.Plan(blank);

		await Assert.That(plan.OutcomeOf(book, blank)).IsTypeOf<EntryOutcome.Failed>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Every_copy_profile_copies_and_the_copies_run_before_the_move()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);
		Profile main = TestProfiles.Make();
		Profile backup = TestProfiles.Copy("Backup", @"E:\Backup", addToLibrary: true);
		Profile mirror = TestProfiles.Copy("Mirror", @"F:\Mirror");

		Plan plan = shelf.Plan(main, backup, mirror);

		await Assert.That(plan.Operations.Script()).IsEqualTo(string.Join("\n",
			$@"copy {Incoming} -> E:\Backup\Marvel\X-Men\X-Men #7.cbz +library",
			$@"copy {Incoming} -> F:\Mirror\Marvel\X-Men\X-Men #7.cbz",
			$"move {Incoming} -> {Target}"));
		await Assert.That(string.Join("|", shelf.Execute(plan.Approve()!)))
			.IsEqualTo($@"D:\Comics\Marvel\X-Men\X-Men #7.cbz|E:\Backup\Marvel\X-Men\X-Men #7.cbz|F:\Mirror\Marvel\X-Men\X-Men #7.cbz");
	}

	[Test]
	public async Task A_book_already_at_its_path_is_in_place()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Target);
		Profile main = TestProfiles.Make();

		Plan plan = shelf.Plan(main);

		await Assert.That(plan.OutcomeOf(book, main)).IsTypeOf<EntryOutcome.AlreadyInPlace>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
		await Assert.That(plan.Conflicts.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_path_that_differs_only_in_case_is_a_case_rename()
	{
		const string lowerCase = @"D:\Comics\marvel\x-men\x-men #7.cbz";
		var shelf = new Shelf();
		shelf.Add(lowerCase);

		Plan plan = shelf.Plan(TestProfiles.Make());

		await Assert.That(plan.Operations.Script()).IsEqualTo($"case {lowerCase} -> {Target}");
		await Assert.That(plan.Conflicts.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Two_books_with_one_target_conflict_on_the_later_one()
	{
		var shelf = new Shelf();
		BookRecord first = shelf.Add(@"D:\Incoming\a.cbz");
		BookRecord second = shelf.Add(@"D:\Incoming\b.cbz");
		Profile main = TestProfiles.Make();

		Plan plan = shelf.Plan(main);

		await Assert.That(plan.TargetOf(first, main)).IsEqualTo(Target);
		Conflict conflict = plan.Conflicts.Single();
		await Assert.That(conflict.Key).IsEqualTo(new ConflictKey(new EntryKey(second.Id, main.Id), P(Target)));
		await Assert.That(conflict.Kind).IsEqualTo(new ConflictKind.SameTargetAs(new EntryKey(first.Id, main.Id)));
		await Assert.That(string.Join(",", conflict.Allowed)).IsEqualTo("Skip,KeepBoth,UsePath");
		await Assert.That(conflict.IsOpen).IsTrue();
		var blocked = (EntryOutcome.Blocked)plan.OutcomeOf(second, main);
		await Assert.That(blocked.Conflict).IsEqualTo(conflict.Key);
		await Assert.That(new[] { blocked.Pending }.Script()).IsEqualTo($@"move D:\Incoming\b.cbz -> {Target}");
		await Assert.That(plan.Operations.Script()).IsEqualTo($@"move D:\Incoming\a.cbz -> {Target}");
	}

	[Test]
	public async Task A_file_at_the_target_is_a_conflict_that_names_the_library_book()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		BookId? existing = shelf.Occupy(Target, inLibrary: true);
		Profile main = TestProfiles.Make();

		Plan plan = shelf.Plan(main);

		Conflict conflict = plan.Conflicts.Single();
		await Assert.That(conflict.Key).IsEqualTo(new ConflictKey(new EntryKey(book.Id, main.Id), P(Target)));
		await Assert.That(conflict.Kind).IsEqualTo(new ConflictKind.TargetExists(existing));
		await Assert.That(string.Join(",", conflict.Allowed)).IsEqualTo("Skip,Replace,KeepBoth,UsePath");
		await Assert.That(conflict.KeepBothPath?.Value).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 (1).cbz");
		await Assert.That(plan.OutcomeOf(book, main)).IsTypeOf<EntryOutcome.Blocked>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_file_at_the_target_that_is_not_in_the_library_names_no_book()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);
		shelf.Occupy(Target);

		Plan plan = shelf.Plan(TestProfiles.Make());

		await Assert.That(plan.Conflicts.Single().Kind).IsEqualTo(new ConflictKind.TargetExists(null));
	}

	[Test]
	public async Task A_selected_book_that_stays_where_it_is_still_holds_its_path()
	{
		var shelf = new Shelf();
		BookRecord staying = shelf.Add(@"D:\Shelf\X-Men #1.cbz", number: "1", publisher: "DC");
		BookRecord incoming = shelf.Add(@"D:\Shelf\new.cbz", number: "1", publisher: "Marvel");
		Profile notDc = TestProfiles.Make(edit: settings => settings with { UseFolder = false, RuleMode = RuleMode.Except, Rules = PublisherIsDc });

		Plan plan = shelf.Plan(notDc);

		await Assert.That(plan.ConflictOf(incoming, notDc).Kind).IsEqualTo(new ConflictKind.TargetExists(staying.Id));
	}

	[Test]
	public async Task Skip_leaves_the_book_where_it_is()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		shelf.Occupy(Target);
		Profile main = TestProfiles.Make();
		ConflictKey key = shelf.Plan(main).Conflicts.Single().Key;

		Plan plan = shelf.Plan(Decisions.None.WithResolution(key, new Resolution.Skip()), main);

		await Assert.That(plan.OutcomeOf(book, main)).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
		await Assert.That(plan.Conflicts.Single().Chosen).IsEqualTo(new Resolution.Skip());
		await Assert.That(plan.Approve()).IsNotNull();
	}

	[Test]
	public async Task Replace_recycles_the_existing_file_before_the_move_and_hands_over_its_read_progress()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		BookId? existing = shelf.Occupy(Target, inLibrary: true);
		Profile main = TestProfiles.Make();
		ConflictKey key = shelf.Plan(main).Conflicts.Single().Key;

		Plan plan = shelf.Plan(Decisions.None.WithResolution(key, new Resolution.Replace()), main);

		await Assert.That(plan.Operations.Script()).IsEqualTo($"recycle {Target}\nmove {Incoming} -> {Target}");
		var recycle = (Operation.RecycleFile)plan.Operations[0];
		await Assert.That(recycle.ExistingBook).IsEqualTo(existing);
		await Assert.That(recycle.TransferReadProgressTo).IsEqualTo(book.Id);
		await Assert.That(string.Join("|", shelf.Execute(plan.Approve()!))).IsEqualTo(Target);
		await Assert.That(shelf.Disk.Recycled.Single()).IsEqualTo(P(Target));
	}

	[Test]
	public async Task Replace_hands_over_no_read_progress_when_the_profile_does_not_keep_it()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);
		BookId? existing = shelf.Occupy(Target, inLibrary: true);
		Profile main = TestProfiles.Make(edit: settings => settings with { KeepReadProgressOnReplace = false });

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), main);

		var recycle = (Operation.RecycleFile)plan.Operations[0];
		await Assert.That(recycle.ExistingBook).IsEqualTo(existing);
		await Assert.That(recycle.TransferReadProgressTo).IsNull();
	}

	[Test]
	public async Task Replace_of_a_file_outside_the_library_names_no_book()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);
		shelf.Occupy(Target);

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), TestProfiles.Make());

		var recycle = (Operation.RecycleFile)plan.Operations[0];
		await Assert.That(recycle.ExistingBook).IsNull();
		await Assert.That(recycle.TransferReadProgressTo).IsNull();
	}

	[Test]
	public async Task Keep_both_moves_to_the_first_numbered_name_that_is_free_on_disk()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		shelf.Occupy(Target);
		shelf.Occupy(@"D:\Comics\Marvel\X-Men\X-Men #7 (1).cbz");
		Profile main = TestProfiles.Make();
		ConflictKey key = shelf.Plan(main).Conflicts.Single().Key;

		Plan plan = shelf.Plan(Decisions.None.WithResolution(key, new Resolution.KeepBoth()), main);

		await Assert.That(plan.TargetOf(book, main)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 (2).cbz");
		await Assert.That(plan.Conflicts.Single().KeepBothPath?.Value).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 (2).cbz");
		await Assert.That(plan.Approve()).IsNotNull();
	}

	[Test]
	public async Task Keep_both_replaces_a_number_the_name_already_ends_with()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(@"D:\Incoming\x (12).cbz");
		shelf.Occupy(@"D:\Comics\Marvel\X-Men\x (12).cbz");
		Profile keepsName = TestProfiles.Make(edit: settings => settings with { UseFileName = false });

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.KeepBoth()), keepsName);

		await Assert.That(plan.TargetOf(book, keepsName)).IsEqualTo(@"D:\Comics\Marvel\X-Men\x (1).cbz");
	}

	[Test]
	public async Task Keep_both_skips_a_name_the_plan_already_uses()
	{
		var shelf = new Shelf();
		BookRecord a = shelf.Add(@"D:\Incoming\a.cbz");
		BookRecord b = shelf.Add(@"D:\Incoming\b.cbz");
		BookRecord c = shelf.Add(@"D:\Incoming\c.cbz");
		Profile main = TestProfiles.Make();

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.KeepBoth()), main);

		await Assert.That(plan.TargetOf(a, main)).IsEqualTo(Target);
		await Assert.That(plan.TargetOf(b, main)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 (1).cbz");
		await Assert.That(plan.TargetOf(c, main)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 (2).cbz");
		await Assert.That(shelf.Execute(plan.Approve()!).Count).IsEqualTo(3);
	}

	[Test]
	public async Task A_typed_path_is_used_when_it_is_free()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		shelf.Occupy(Target);
		Profile main = TestProfiles.Make();
		ConflictKey key = shelf.Plan(main).Conflicts.Single().Key;

		Plan plan = shelf.Plan(Decisions.None.WithResolution(key, new Resolution.UsePath(P(@"D:\Comics\free.cbz"))), main);

		await Assert.That(plan.TargetOf(book, main)).IsEqualTo(@"D:\Comics\free.cbz");
		await Assert.That(plan.Approve()).IsNotNull();
	}

	[Test]
	public async Task A_typed_path_that_is_taken_conflicts_again_under_its_own_key()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		shelf.Occupy(Target);
		shelf.Occupy(@"D:\Comics\taken.cbz");
		Profile main = TestProfiles.Make();
		ConflictKey key = shelf.Plan(main).Conflicts.Single().Key;

		Plan plan = shelf.Plan(Decisions.None.WithResolution(key, new Resolution.UsePath(P(@"D:\Comics\taken.cbz"))), main);

		await Assert.That(plan.Conflicts.Count).IsEqualTo(2);
		await Assert.That(plan.Conflicts[0].IsOpen).IsFalse();
		await Assert.That(plan.Conflicts[1].Key).IsEqualTo(new ConflictKey(new EntryKey(book.Id, main.Id), P(@"D:\Comics\taken.cbz")));
		await Assert.That(plan.Conflicts[1].IsOpen).IsTrue();
		await Assert.That(plan.Approve()).IsNull();
	}

	[Test]
	public async Task A_resolution_the_conflict_does_not_allow_is_ignored()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Incoming\a.cbz");
		shelf.Add(@"D:\Incoming\b.cbz");
		Profile main = TestProfiles.Make();
		ConflictKey sameTarget = shelf.Plan(main).Conflicts.Single().Key;

		Plan withExplicit = shelf.Plan(Decisions.None.WithResolution(sameTarget, new Resolution.Replace()), main);
		Plan withDefault = shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), main);
		Plan withBoth = shelf.Plan(Decisions.None.WithResolution(sameTarget, new Resolution.Replace()).WithDefault(new Resolution.Skip()), main);

		await Assert.That(withExplicit.Conflicts.Single().IsOpen).IsTrue();
		await Assert.That(withDefault.Conflicts.Single().IsOpen).IsTrue();
		await Assert.That(withBoth.Conflicts.Single().Chosen).IsEqualTo(new Resolution.Skip());
	}

	[Test]
	public async Task An_explicit_resolution_beats_the_default()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		shelf.Occupy(Target);
		Profile main = TestProfiles.Make();
		ConflictKey key = shelf.Plan(main).Conflicts.Single().Key;

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.Skip()).WithResolution(key, new Resolution.KeepBoth()), main);

		await Assert.That(plan.TargetOf(book, main)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 (1).cbz");
	}

	[Test]
	public async Task The_default_resolution_applies_to_every_conflict()
	{
		var shelf = new Shelf();
		BookRecord xMen = shelf.Add(Incoming);
		BookRecord hulk = shelf.Add(@"D:\Incoming\hulk.cbz", series: "Hulk");
		shelf.Occupy(Target);
		shelf.Occupy(@"D:\Comics\Marvel\Hulk\Hulk #7.cbz");
		Profile main = TestProfiles.Make();

		Plan open = shelf.Plan(main);
		Plan skipped = shelf.Plan(Decisions.None.WithDefault(ConflictDefault.Skip.ToResolution()), main);

		await Assert.That(open.Conflicts.Count(conflict => conflict.IsOpen)).IsEqualTo(2);
		await Assert.That(skipped.OutcomeOf(xMen, main)).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(skipped.OutcomeOf(hulk, main)).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(skipped.Approve()).IsNotNull();
	}

	[Test]
	public async Task The_stored_conflict_default_maps_to_a_resolution()
	{
		await Assert.That(ConflictDefault.Ask.ToResolution()).IsNull();
		await Assert.That(ConflictDefault.Skip.ToResolution()).IsEqualTo(new Resolution.Skip());
		await Assert.That(ConflictDefault.Replace.ToResolution()).IsEqualTo(new Resolution.Replace());
		await Assert.That(ConflictDefault.KeepBoth.ToResolution()).IsEqualTo(new Resolution.KeepBoth());
	}

	[Test]
	public async Task A_move_into_a_path_another_move_vacates_goes_after_it()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Shelf\X-Men #2.cbz", number: "1");
		shelf.Add(@"D:\Shelf\X-Men #1.cbz", number: "3");

		Plan plan = shelf.Plan(TestProfiles.Rename());

		await Assert.That(plan.Conflicts.Count).IsEqualTo(0);
		await Assert.That(plan.Operations.Script()).IsEqualTo(string.Join("\n",
			@"move D:\Shelf\X-Men #1.cbz -> D:\Shelf\X-Men #3.cbz",
			@"move D:\Shelf\X-Men #2.cbz -> D:\Shelf\X-Men #1.cbz"));
		await Assert.That(string.Join("|", shelf.Execute(plan.Approve()!))).IsEqualTo(@"D:\Shelf\X-Men #1.cbz|D:\Shelf\X-Men #3.cbz");
	}

	[Test]
	public async Task A_chain_of_moves_runs_from_the_free_end_whatever_the_selection_order()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Shelf\X-Men #3.cbz", number: "2");
		shelf.Add(@"D:\Shelf\X-Men #1.cbz", number: "4");
		shelf.Add(@"D:\Shelf\X-Men #2.cbz", number: "1");

		Plan plan = shelf.Plan(TestProfiles.Rename());

		await Assert.That(plan.Conflicts.Count).IsEqualTo(0);
		await Assert.That(string.Join("|", shelf.Execute(plan.Approve()!)))
			.IsEqualTo(@"D:\Shelf\X-Men #1.cbz|D:\Shelf\X-Men #2.cbz|D:\Shelf\X-Men #4.cbz");
	}

	[Test]
	public async Task A_copy_does_not_vacate_the_path_it_reads()
	{
		var shelf = new Shelf();
		BookRecord copied = shelf.Add(@"D:\Comics\Marvel\X-Men\X-Men #7.cbz", publisher: "DC");
		BookRecord incoming = shelf.Add(Incoming);
		Profile copyDc = TestProfiles.Make("Copy DC", @"E:\DC", settings => settings with
		{
			Action = new ProfileAction.Copy(AddToLibrary: false),
			RuleMode = RuleMode.Only,
			Rules = PublisherIsDc,
		});
		Profile moveMarvel = TestProfiles.Make(edit: settings => settings with { RuleMode = RuleMode.Except, Rules = PublisherIsDc });

		Plan plan = shelf.Plan(copyDc, moveMarvel);

		await Assert.That(plan.OutcomeOf(copied, copyDc)).IsTypeOf<EntryOutcome.Planned>();
		await Assert.That(plan.ConflictOf(incoming, moveMarvel).Kind).IsEqualTo(new ConflictKind.TargetExists(copied.Id));
	}

	[Test]
	public async Task Two_books_that_swap_paths_are_a_target_exists_conflict()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Shelf\X-Men #2.cbz", number: "1");
		shelf.Add(@"D:\Shelf\X-Men #1.cbz", number: "2");
		Profile rename = TestProfiles.Rename();

		Plan plan = shelf.Plan(rename);

		await Assert.That(plan.Conflicts.Count).IsGreaterThan(0);
		await Assert.That(plan.Conflicts.All(conflict => conflict.Kind is ConflictKind.TargetExists)).IsTrue();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
		await Assert.That(plan.Approve()).IsNull();
	}

	[Test]
	public async Task Keep_both_untangles_a_swap_into_an_order_that_runs()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Shelf\X-Men #2.cbz", number: "1");
		shelf.Add(@"D:\Shelf\X-Men #1.cbz", number: "2");

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.KeepBoth()), TestProfiles.Rename());

		IReadOnlyList<string> files = shelf.Execute(plan.Approve()!);
		await Assert.That(files.Count).IsEqualTo(2);
		await Assert.That(files.Count(file => file is @"D:\Shelf\X-Men #1.cbz" or @"D:\Shelf\X-Men #2.cbz")).IsEqualTo(1);
		await Assert.That(plan.Operations.Count).IsEqualTo(2);
	}

	[Test]
	public async Task An_empty_required_field_fails_the_book_when_the_profile_has_no_failed_folder()
	{
		var shelf = new Shelf();
		BookRecord noYear = shelf.Add(Incoming);
		BookRecord withYear = shelf.Add(@"D:\Incoming\hulk.cbz", series: "Hulk", year: 1992);
		Profile main = TestProfiles.Make(edit: settings => settings with { RequiredFields = ["year"] });

		Plan plan = shelf.Plan(main);

		await Assert.That(plan.OutcomeOf(noYear, main)).IsTypeOf<EntryOutcome.Failed>();
		await Assert.That(plan.Operations.Script()).IsEqualTo(@"move D:\Incoming\hulk.cbz -> D:\Comics\Marvel\Hulk\Hulk #7.cbz");
		await Assert.That(plan.OutcomeOf(withYear, main)).IsTypeOf<EntryOutcome.Planned>();
	}

	[Test]
	public async Task Blank_text_counts_as_an_empty_required_field()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming, publisher: "  ");
		Profile main = TestProfiles.Make(edit: settings => settings with { RequiredFields = ["publisher"] });

		await Assert.That(shelf.Plan(main).OutcomeOf(book, main)).IsTypeOf<EntryOutcome.Failed>();
	}

	[Test]
	public async Task An_empty_required_field_moves_the_book_under_the_failed_folder_and_reports_it_failed()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming);
		Profile main = TestProfiles.Make(edit: settings => settings with { RequiredFields = ["year"], FailedBooksFolder = @"D:\Failed" });

		Plan plan = shelf.Plan(main);

		var outcome = (EntryOutcome.PlannedAsFailed)plan.OutcomeOf(book, main);
		await Assert.That(outcome.Reason).Contains("Year");
		await Assert.That(plan.Operations.Script()).IsEqualTo($@"move {Incoming} -> D:\Failed\Marvel\X-Men\X-Men #7.cbz");
		await Assert.That(plan.Tally().Single().Failed).IsEqualTo(1);
		await Assert.That(plan.Approve()).IsNotNull();
	}

	[Test]
	public async Task A_fileless_book_is_skipped_when_the_profile_has_no_cover_format()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.AddFileless(hasCustomCover: true);
		Profile main = TestProfiles.Make();

		Plan plan = shelf.Plan(main);

		await Assert.That(plan.OutcomeOf(book, main)).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_fileless_book_without_a_custom_cover_fails()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.AddFileless(hasCustomCover: false);
		Profile covers = TestProfiles.Make(edit: settings => settings with { FilelessCovers = CoverFormat.Png });

		Plan plan = shelf.Plan(covers);

		await Assert.That(plan.OutcomeOf(book, covers)).IsTypeOf<EntryOutcome.Failed>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_fileless_book_writes_its_cover_with_the_formats_extension()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.AddFileless(hasCustomCover: true);
		Profile covers = TestProfiles.Make(edit: settings => settings with { FilelessCovers = CoverFormat.Png });

		Plan plan = shelf.Plan(covers);

		await Assert.That(plan.Operations.Script()).IsEqualTo(@"cover D:\Comics\Marvel\X-Men\X-Men #7.png as Png");
		await Assert.That(plan.Operations.Single().Entry).IsEqualTo(new EntryKey(book.Id, covers.Id));
	}

	[Test]
	public async Task A_book_whose_file_is_missing_fails_under_every_profile()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.AddMissing(Incoming);
		Profile main = TestProfiles.Make();
		Profile backup = TestProfiles.Copy("Backup", @"E:\Backup");

		Plan plan = shelf.Plan(main, backup);

		await Assert.That(plan.OutcomeOf(book, main)).IsTypeOf<EntryOutcome.Failed>();
		await Assert.That(plan.OutcomeOf(book, backup)).IsTypeOf<EntryOutcome.Failed>();
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_book_in_an_excluded_folder_is_skipped()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(@"D:\Incoming\Unsorted\seven.cbz");
		Profile main = TestProfiles.Make(edit: settings => settings with { ExcludedFolders = [@"D:\Incoming\Unsorted"] });

		await Assert.That(shelf.Plan(main).OutcomeOf(book, main)).IsTypeOf<EntryOutcome.Skipped>();
	}

	[Test]
	public async Task An_unanswered_multi_value_token_waits_and_the_question_counts_every_waiting_entry()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming, writer: "Chris Claremont, Jim Lee");
		Profile main = WriterInName();
		Profile backup = WriterInName("Backup", new ProfileAction.Copy(AddToLibrary: false));
		QuestionKey key = TemplateFixture.IssueKey(Fields.Writer, book);

		Plan plan = shelf.Plan(main, backup);

		var waiting = (EntryOutcome.WaitingForAnswer)plan.OutcomeOf(book, main);
		await Assert.That(waiting.Questions.Single()).IsEqualTo(key);
		await Assert.That(plan.OutcomeOf(book, backup)).IsTypeOf<EntryOutcome.WaitingForAnswer>();
		Question question = plan.Questions.Single();
		await Assert.That(question.Key).IsEqualTo(key);
		await Assert.That(question.Waiting).IsEqualTo(2);
		await Assert.That(string.Join("|", question.Choices)).IsEqualTo("Chris Claremont|Jim Lee");
		await Assert.That(plan.Operations.Count).IsEqualTo(0);
		await Assert.That(plan.Approve()).IsNull();
	}

	[Test]
	public async Task An_answer_in_the_decisions_renders_the_waiting_entry()
	{
		var shelf = new Shelf();
		BookRecord book = shelf.Add(Incoming, writer: "Chris Claremont, Jim Lee");
		Profile main = WriterInName();
		Decisions answered = Decisions.None.WithAnswer(shelf.Plan(main).Questions.Single().Key, TemplateFixture.Pick("Jim Lee"));

		Plan plan = shelf.Plan(answered, main);

		await Assert.That(plan.Questions.Count).IsEqualTo(0);
		await Assert.That(plan.TargetOf(book, main)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men #7 by Jim Lee.cbz");
		await Assert.That(plan.Answers.Remembered.Single().Key).IsEqualTo(TemplateFixture.IssueKey(Fields.Writer, book));
		await Assert.That(plan.Approve()).IsNotNull();
	}

	[Test]
	public async Task A_counter_numbers_only_the_books_the_profile_renders()
	{
		var shelf = new Shelf();
		BookRecord first = shelf.Add(@"D:\Incoming\a.cbz");
		shelf.Add(@"D:\Incoming\b.cbz", publisher: "DC");
		BookRecord third = shelf.Add(@"D:\Incoming\c.cbz");
		Profile counted = TestProfiles.Make(edit: settings => settings with
		{
			FileTemplate = "{<series>}{ <counter(1)(1)(2)>}",
			RuleMode = RuleMode.Except,
			Rules = PublisherIsDc,
		});

		Plan plan = shelf.Plan(counted);

		await Assert.That(plan.TargetOf(first, counted)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men 01.cbz");
		await Assert.That(plan.TargetOf(third, counted)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men 02.cbz");
		await Assert.That(shelf.Plan(counted).Operations.Script()).IsEqualTo(plan.Operations.Script());
	}

	[Test]
	public async Task A_counter_number_stays_with_its_book_when_a_decision_skips_an_earlier_one()
	{
		var shelf = new Shelf();
		BookRecord first = shelf.Add(@"D:\Incoming\a.cbz");
		BookRecord second = shelf.Add(@"D:\Incoming\b.cbz");
		shelf.Occupy(@"D:\Comics\Marvel\X-Men\X-Men 01.cbz");
		Profile counted = TestProfiles.Make(edit: settings => settings with { FileTemplate = "{<series>}{ <counter(1)(1)(2)>}" });

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.Skip()), counted);

		await Assert.That(plan.OutcomeOf(first, counted)).IsTypeOf<EntryOutcome.Skipped>();
		await Assert.That(plan.TargetOf(second, counted)).IsEqualTo(@"D:\Comics\Marvel\X-Men\X-Men 02.cbz");
	}

	[Test]
	public async Task Moves_by_a_pruning_profile_predict_one_prune_as_the_last_operation()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Incoming\A\one.cbz", number: "1", year: 1991);
		shelf.Add(@"D:\Incoming\A\two.cbz", number: "2", year: 1991);
		shelf.Add(@"D:\Incoming\B\three.cbz", number: "3", year: 1991);
		Profile pruning = TestProfiles.Make(edit: settings => settings with
		{
			PruneEmptyFolders = true,
			KeepFolders = [@"D:\Incoming\Keep"],
			RequiredFields = ["year"],
			FailedBooksFolder = @"D:\Failed",
		});

		Plan plan = shelf.Plan(pruning);

		await Assert.That(plan.Operations.Count).IsEqualTo(4);
		var prune = (Operation.PruneFolders)plan.Operations[plan.Operations.Count - 1];
		await Assert.That(string.Join("|", prune.Candidates)).IsEqualTo(@"D:\Incoming\A|D:\Incoming\B");
		await Assert.That(string.Join("|", prune.StopAt.OrderBy(folder => folder.Value))).IsEqualTo(@"D:\|D:\Comics|D:\Failed");
		await Assert.That(string.Join("|", prune.Keep)).IsEqualTo(@"D:\Incoming\Keep");
	}

	[Test]
	public async Task Only_moves_by_a_profile_that_prunes_are_prune_candidates()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);
		shelf.Add(@"D:\Incoming\DC\bat.cbz", series: "Batman", publisher: "DC");
		Profile keeps = TestProfiles.Make("Keeps", edit: settings => settings with { RuleMode = RuleMode.Except, Rules = PublisherIsDc });
		Profile prunes = TestProfiles.Make("Prunes", @"E:\DC", settings => settings with { PruneEmptyFolders = true, RuleMode = RuleMode.Only, Rules = PublisherIsDc });

		Plan both = shelf.Plan(keeps, prunes);
		Plan none = shelf.Plan(keeps);

		Operation.PruneFolders prune = both.Operations.OfType<Operation.PruneFolders>().Single();
		await Assert.That(string.Join("|", prune.Candidates)).IsEqualTo(@"D:\Incoming\DC");
		await Assert.That(string.Join("|", prune.StopAt.OrderBy(folder => folder.Value))).IsEqualTo(@"D:\|D:\Comics|E:\DC");
		await Assert.That(none.Operations.OfType<Operation.PruneFolders>().Count()).IsEqualTo(0);
	}

	[Test]
	public async Task A_plan_with_an_open_conflict_is_not_approved_and_says_what_blocks_it()
	{
		var shelf = new Shelf();
		shelf.Add(Incoming);
		shelf.Occupy(Target);
		Profile main = TestProfiles.Make();

		Plan open = shelf.Plan(main);
		Plan resolved = shelf.Plan(Decisions.None.WithDefault(new Resolution.KeepBoth()), main);

		await Assert.That(open.Approve()).IsNull();
		await Assert.That(open.Blockers.Count).IsEqualTo(1);
		await Assert.That(open.Blockers[0]).Contains(Target);
		ApprovedPlan approved = resolved.Approve()!;
		await Assert.That(resolved.Blockers.Count).IsEqualTo(0);
		await Assert.That(approved.Kind).IsEqualTo(new PlanKind.Organize());
		await Assert.That(approved.Operations.Script()).IsEqualTo($@"move {Incoming} -> D:\Comics\Marvel\X-Men\X-Men #7 (1).cbz");
		await Assert.That(approved.Entries.Count).IsEqualTo(1);
		await Assert.That(approved.Operations.Single().Id).IsEqualTo(new OpId(1));
	}

	[Test]
	public async Task The_tally_counts_each_outcome_per_profile()
	{
		var shelf = new Shelf();
		shelf.Add(@"D:\Incoming\planned.cbz");
		shelf.Add(@"D:\Comics\Marvel\Hulk\Hulk #7.cbz", series: "Hulk");
		shelf.Add(@"D:\Incoming\filtered.cbz", publisher: "DC");
		shelf.AddMissing(@"D:\Incoming\missing.cbz");
		shelf.Add(@"D:\Incoming\blocked.cbz");
		shelf.Add(@"D:\Incoming\waiting.cbz", series: "Thor", writer: "Walt Simonson, Sal Buscema");
		Profile main = TestProfiles.Make(edit: settings => settings with
		{
			FileTemplate = "{<series>}{ #<number>}{ by <writer(, )(issue)>}",
			RuleMode = RuleMode.Except,
			Rules = PublisherIsDc,
		});

		IReadOnlyList<ProfileTally> tally = shelf.Plan(main).Tally();

		await Assert.That(tally.Single()).IsEqualTo(new ProfileTally("Main", Planned: 1, InPlace: 1, Skipped: 1, Failed: 1, Waiting: 1, Blocked: 1));
	}

	[Test]
	public async Task A_path_over_the_limit_is_a_conflict_that_only_a_shorter_path_or_skip_resolves()
	{
		var shelf = new Shelf { MaxPathLength = Target.Length - 1 };
		BookRecord book = shelf.Add(Incoming);
		Profile main = TestProfiles.Make();

		Plan plan = shelf.Plan(Decisions.None.WithDefault(new Resolution.KeepBoth()), main);

		Conflict conflict = plan.Conflicts.Single();
		await Assert.That(conflict.Kind).IsEqualTo(new ConflictKind.PathTooLong(Target.Length, Target.Length - 1));
		await Assert.That(string.Join(",", conflict.Allowed)).IsEqualTo("Skip,UsePath");
		await Assert.That(conflict.KeepBothPath).IsNull();
		await Assert.That(conflict.IsOpen).IsTrue();

		Plan shortened = shelf.Plan(Decisions.None.WithResolution(conflict.Key, new Resolution.UsePath(P(@"D:\Comics\x7.cbz"))), main);
		await Assert.That(shortened.TargetOf(book, main)).IsEqualTo(@"D:\Comics\x7.cbz");
	}

	[Test]
	public async Task A_path_exactly_at_the_limit_is_not_a_conflict()
	{
		var shelf = new Shelf { MaxPathLength = Target.Length };
		shelf.Add(Incoming);

		await Assert.That(shelf.Plan(TestProfiles.Make()).Conflicts.Count).IsEqualTo(0);
	}
}
