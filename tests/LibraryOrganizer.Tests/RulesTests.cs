using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class RulesTests
{
	private static BookRecord Book(params (Field Field, FieldValue Value)[] values)
	{
		FieldValue[] all = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		all[Fields.Series.Ordinal] = new FieldValue.Text("X-Men");
		all[Fields.Publisher.Ordinal] = new FieldValue.Text("Marvel");
		foreach ((Field field, FieldValue value) in values)
		{
			all[field.Ordinal] = value;
		}
		return new BookRecord(
			new BookId(Guid.NewGuid()),
			new BookLocation.File(AbsPath.TryParse(@"D:\Incoming\x.cbz")!.Value, Exists: true),
			"x",
			all,
			new Dictionary<string, string>());
	}

	private static Rule.Condition Cond(Field field, Operator op, string operand = "")
	{
		Rule.Condition? condition = Rule.Condition.Create(field, op, operand, out string? error);
		return condition ?? throw new InvalidOperationException(error);
	}

	private static Rule.Group All(params Rule[] children) => new(Combinator.All, children);

	private static Rule.Group Any(params Rule[] children) => new(Combinator.Any, children);

	private static FilterVerdict Run(Rule.Group root, RuleMode mode, BookRecord book, FolderSet? excluded = null, SeriesIndex? series = null)
		=> new Filter(excluded ?? FolderSet.None, mode, root).Evaluate(book, series ?? SeriesIndex.Empty);

	private static bool Matches(Rule.Condition condition, BookRecord book) => Run(Any(condition), RuleMode.Only, book).Accepted;

	[Test]
	public async Task Operators_follow_the_field_kind()
	{
		await Assert.That(Operators.For(FieldKind.Text).Contains(Operator.StartsWith)).IsTrue();
		await Assert.That(Operators.For(FieldKind.Text).Contains(Operator.GreaterThan)).IsFalse();
		await Assert.That(Operators.For(FieldKind.Items).Contains(Operator.ContainsItem)).IsTrue();
		await Assert.That(Operators.For(FieldKind.Items).Contains(Operator.Is)).IsFalse();
		await Assert.That(Operators.For(FieldKind.Date).Contains(Operator.Before)).IsTrue();
		await Assert.That(Operators.For(FieldKind.Synthetic).Count).IsEqualTo(0);
	}

	[Test]
	public async Task Every_rule_field_offers_at_least_one_operator()
	{
		foreach (Field field in Fields.All.Where(f => f.Uses.HasFlag(FieldUses.Rule)))
		{
			await Assert.That(Operators.For(field.Kind).Count).IsGreaterThan(0);
		}
	}

	[Test]
	public async Task Create_rejects_an_operator_the_field_does_not_offer()
	{
		Rule.Condition? condition = Rule.Condition.Create(Fields.Year, Operator.Contains, "19", out string? error);

		await Assert.That(condition).IsNull();
		await Assert.That(error).IsNotNull();
	}

	[Test]
	[Arguments("abc")]
	[Arguments("")]
	public async Task Create_rejects_a_number_operand_that_does_not_parse(string operand)
	{
		Rule.Condition? condition = Rule.Condition.Create(Fields.Year, Operator.LessThan, operand, out string? error);

		await Assert.That(condition).IsNull();
		await Assert.That(error).IsNotNull();
	}

	[Test]
	public async Task Create_rejects_a_bad_pattern_and_a_field_that_rules_cannot_use()
	{
		await Assert.That(Rule.Condition.Create(Fields.Series, Operator.MatchesRegex, "(", out _)).IsNull();
		await Assert.That(Rule.Condition.Create(Fields.Counter, Operator.Is, "1", out _)).IsNull();
	}

	[Test]
	public async Task Create_keeps_the_operand_text_as_typed()
	{
		Rule.Condition condition = Cond(Fields.Rating, Operator.GreaterThan, " 3.50 ");

		await Assert.That(condition.RawOperand).IsEqualTo(" 3.50 ");
		await Assert.That(condition.Operand).IsEqualTo(new Operand.Number(3.5m));
	}

	[Test]
	public async Task Yes_no_fields_accept_the_2x_label_only_for_manga()
	{
		await Assert.That(Rule.Condition.Create(Fields.Manga, Operator.Is, "Yes (Right to Left)", out _)).IsNotNull();
		await Assert.That(Rule.Condition.Create(Fields.BlackAndWhite, Operator.Is, "Yes (Right to Left)", out _)).IsNull();
	}

	[Test]
	[Arguments(Operator.Is, "marvel", true)]
	[Arguments(Operator.Is, "Marve", false)]
	[Arguments(Operator.IsNot, "DC", true)]
	[Arguments(Operator.IsNot, "MARVEL", false)]
	[Arguments(Operator.Contains, "ARV", true)]
	[Arguments(Operator.DoesNotContain, "arv", false)]
	[Arguments(Operator.StartsWith, "mar", true)]
	[Arguments(Operator.StartsWith, "vel", false)]
	[Arguments(Operator.MatchesRegex, "^m.rvel$", true)]
	[Arguments(Operator.MatchesRegex, "^DC", false)]
	public async Task Text_operators_ignore_case(Operator op, string operand, bool expected)
		=> await Assert.That(Matches(Cond(Fields.Publisher, op, operand), Book())).IsEqualTo(expected);

	[Test]
	public async Task Empty_operators_cover_unset_and_blank_text()
	{
		BookRecord blank = Book((Fields.Imprint, new FieldValue.Text("  ")));

		await Assert.That(Matches(Cond(Fields.Imprint, Operator.IsEmpty), Book())).IsTrue();
		await Assert.That(Matches(Cond(Fields.Imprint, Operator.IsEmpty), blank)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Publisher, Operator.IsEmpty), Book())).IsFalse();
		await Assert.That(Matches(Cond(Fields.Publisher, Operator.IsNotEmpty), Book())).IsTrue();
	}

	[Test]
	public async Task An_unset_value_never_matches_a_positive_or_ordering_operator()
	{
		BookRecord noYear = Book();

		await Assert.That(Matches(Cond(Fields.Year, Operator.LessThan, "2000"), noYear)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Year, Operator.GreaterThan, "-5"), noYear)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Year, Operator.Is, "0"), noYear)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Imprint, Operator.Contains, "x"), noYear)).IsFalse();
	}

	[Test]
	public async Task An_unset_value_satisfies_the_negative_operators()
	{
		BookRecord noYear = Book();

		await Assert.That(Matches(Cond(Fields.Year, Operator.IsNot, "1999"), noYear)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Imprint, Operator.DoesNotContain, "x"), noYear)).IsTrue();
	}

	[Test]
	[Arguments(1999, Operator.LessThan, "2000", true)]
	[Arguments(2000, Operator.LessThan, "2000", false)]
	[Arguments(2000, Operator.GreaterThan, "1999.5", true)]
	[Arguments(2000, Operator.Is, "2000.0", true)]
	[Arguments(2000, Operator.IsNot, "2000", false)]
	public async Task Numbers_compare_as_decimals(int year, Operator op, string operand, bool expected)
		=> await Assert.That(Matches(Cond(Fields.Year, op, operand), Book((Fields.Year, new FieldValue.Int(year))))).IsEqualTo(expected);

	[Test]
	public async Task Decimal_fields_compare_with_fractions()
	{
		BookRecord rated = Book((Fields.Rating, new FieldValue.Decimal(3.75m)));

		await Assert.That(Matches(Cond(Fields.Rating, Operator.GreaterThan, "3.5"), rated)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Rating, Operator.LessThan, "3.75"), rated)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Rating, Operator.Is, "3.75"), rated)).IsTrue();
	}

	[Test]
	[Arguments("10", Operator.GreaterThan, "9", true)]
	[Arguments("9", Operator.GreaterThan, "10", false)]
	[Arguments("12", Operator.LessThan, "12.1", true)]
	[Arguments("12a", Operator.GreaterThan, "12", true)]
	[Arguments("7", Operator.Is, "07", true)]
	[Arguments("7", Operator.GreaterThan, "07", false)]
	[Arguments("7b", Operator.Is, "7B", true)]
	public async Task Issue_numbers_use_natural_order(string number, Operator op, string operand, bool expected)
		=> await Assert.That(Matches(Cond(Fields.Number, op, operand), Book((Fields.Number, new FieldValue.Issue(new IssueNumber(number)))))).IsEqualTo(expected);

	[Test]
	public async Task Contains_item_compares_whole_items_and_contains_compares_raw_text()
	{
		BookRecord book = Book((Fields.Writer, FieldValue.ParseItems("Alan Moore, Jim Lee")));

		await Assert.That(Matches(Cond(Fields.Writer, Operator.ContainsItem, "alan moore"), book)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Writer, Operator.ContainsItem, "Moore"), book)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Writer, Operator.DoesNotContainItem, "Moore"), book)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Writer, Operator.Contains, "Moore"), book)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Writer, Operator.ContainsItem, "Jim Lee"), book)).IsTrue();
	}

	[Test]
	public async Task Yes_no_rules_compare_values_and_unknown_means_unset()
	{
		BookRecord rtl = Book((Fields.Manga, new FieldValue.YesNo(YesNoValue.YesRightToLeft)));
		BookRecord no = Book((Fields.Manga, new FieldValue.YesNo(YesNoValue.No)));

		await Assert.That(Matches(Cond(Fields.Manga, Operator.Is, "Yes (Right to Left)"), rtl)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Manga, Operator.Is, "Yes"), rtl)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Manga, Operator.IsNot, "No"), rtl)).IsTrue();
		await Assert.That(Matches(Cond(Fields.Manga, Operator.IsNot, "No"), no)).IsFalse();
		await Assert.That(Matches(Cond(Fields.Manga, Operator.Is, "Unknown"), Book())).IsTrue();
		await Assert.That(Matches(Cond(Fields.Manga, Operator.Is, "Unknown"), no)).IsFalse();
	}

	[Test]
	public async Task Date_rules_compare_the_day()
	{
		BookRecord book = Book((Fields.ReleasedDate, new FieldValue.Date(new DateTime(2020, 6, 15, 13, 0, 0))));

		await Assert.That(Matches(Cond(Fields.ReleasedDate, Operator.After, "2020-06-14"), book)).IsTrue();
		await Assert.That(Matches(Cond(Fields.ReleasedDate, Operator.After, "2020-06-15"), book)).IsFalse();
		await Assert.That(Matches(Cond(Fields.ReleasedDate, Operator.Before, "2021-01-01"), book)).IsTrue();
		await Assert.That(Matches(Cond(Fields.ReleasedDate, Operator.On, "2020-06-15"), book)).IsTrue();
	}

	[Test]
	public async Task Series_fields_read_the_series_index()
	{
		BookRecord first = TestBooks.Book(number: "1", year: 1991);
		BookRecord later = TestBooks.Book(number: "9", year: 1995);
		SeriesIndex series = SeriesIndex.Build([first, later]);
		Rule.Group rule = Any(Cond(Fields.StartYear, Operator.Is, "1991"));

		await Assert.That(Run(rule, RuleMode.Only, later, series: series).Accepted).IsTrue();
		await Assert.That(Run(rule, RuleMode.Only, later).Accepted).IsFalse();
	}

	[Test]
	public async Task Nested_groups_combine_with_any_and_all()
	{
		BookRecord book = Book((Fields.Year, new FieldValue.Int(1985)), (Fields.Format, new FieldValue.Text("Annual")));
		Rule.Condition publisherIsDc = Cond(Fields.Publisher, Operator.Is, "DC");
		Rule.Condition annual = Cond(Fields.Format, Operator.Contains, "Annual");
		Rule.Condition old = Cond(Fields.Year, Operator.LessThan, "1990");
		Rule.Condition newer = Cond(Fields.Year, Operator.GreaterThan, "1990");

		await Assert.That(Run(Any(publisherIsDc, All(annual, old)), RuleMode.Only, book).Accepted).IsTrue();
		await Assert.That(Run(Any(publisherIsDc, All(annual, newer)), RuleMode.Only, book).Accepted).IsFalse();
		await Assert.That(Run(All(annual, Any(publisherIsDc, old)), RuleMode.Only, book).Accepted).IsTrue();
		await Assert.That(Run(All(annual, Any(publisherIsDc, newer)), RuleMode.Only, book).Accepted).IsFalse();
	}

	[Test]
	[Arguments(RuleMode.Only, true)]
	[Arguments(RuleMode.Except, true)]
	public async Task An_empty_root_accepts_every_book_in_both_modes(RuleMode mode, bool expected)
		=> await Assert.That(Run(Any(), mode, Book()).Accepted).IsEqualTo(expected);

	[Test]
	public async Task Empty_groups_have_no_opinion_and_their_parent_skips_them()
	{
		Rule.Condition publisherIsDc = Cond(Fields.Publisher, Operator.Is, "DC");
		BookRecord marvel = Book();

		await Assert.That(Run(All(Any(), All()), RuleMode.Only, marvel).Accepted).IsTrue();
		await Assert.That(Run(All(Any(), publisherIsDc), RuleMode.Only, marvel).Accepted).IsFalse();
		await Assert.That(Run(Any(All(), publisherIsDc), RuleMode.Except, marvel).Accepted).IsTrue();
	}

	[Test]
	[Arguments(RuleMode.Only, true, true)]
	[Arguments(RuleMode.Only, false, false)]
	[Arguments(RuleMode.Except, true, false)]
	[Arguments(RuleMode.Except, false, true)]
	public async Task Mode_decides_what_a_match_means(RuleMode mode, bool matches, bool accepted)
	{
		BookRecord book = Book();
		Rule.Condition rule = Cond(Fields.Publisher, Operator.Is, matches ? "Marvel" : "DC");

		await Assert.That(Run(Any(rule), mode, book).Accepted).IsEqualTo(accepted);
	}

	[Test]
	public async Task Reasons_name_the_deciding_rule()
	{
		BookRecord marvel = Book();
		Rule.Condition isMarvel = Cond(Fields.Publisher, Operator.Is, "Marvel");
		Rule.Condition isDc = Cond(Fields.Publisher, Operator.Is, "DC");
		Rule.Condition recent = Cond(Fields.Year, Operator.GreaterThan, "2000");

		await Assert.That(Run(Any(isMarvel), RuleMode.Only, marvel).Reason).IsEqualTo("Matches rule: Publisher is Marvel");
		await Assert.That(Run(Any(isDc), RuleMode.Only, marvel).Reason).IsEqualTo("Does not match: Publisher is DC");
		await Assert.That(Run(Any(isMarvel), RuleMode.Except, marvel).Reason).IsEqualTo("Excluded by rule: Publisher is Marvel");
		await Assert.That(Run(Any(isDc, isMarvel), RuleMode.Only, marvel).Reason).IsEqualTo("Matches rule: Publisher is Marvel");
		await Assert.That(Run(Any(isDc, recent), RuleMode.Only, marvel).Reason).IsEqualTo("Does not match: Publisher is DC or Year greater than 2000");
		await Assert.That(Run(Any(isDc, All(isMarvel, recent)), RuleMode.Only, marvel).Reason).IsEqualTo("Does not match: Publisher is DC or Year greater than 2000");
	}

	[Test]
	public async Task A_group_that_decides_early_reports_only_the_deciding_rule()
	{
		BookRecord marvel = Book();
		Rule.Condition isDc = Cond(Fields.Publisher, Operator.Is, "DC");
		Rule.Condition isMarvel = Cond(Fields.Publisher, Operator.Is, "Marvel");
		Rule.Condition isImage = Cond(Fields.Publisher, Operator.Is, "Image");

		await Assert.That(Run(All(isMarvel, isDc, isImage), RuleMode.Only, marvel).Reason).IsEqualTo("Does not match: Publisher is DC");
	}

	[Test]
	public async Task A_book_under_an_excluded_folder_is_rejected_with_the_folder_named()
	{
		AbsPath manga = AbsPath.TryParse(@"D:\Manga")!.Value;
		FolderSet excluded = new([manga]);
		BookRecord inside = BookAt(@"d:\manga\Berserk\v1.cbz");

		FilterVerdict verdict = Run(Any(), RuleMode.Except, inside, excluded);

		await Assert.That(verdict.Accepted).IsFalse();
		await Assert.That(verdict.Reason).IsEqualTo(@"In excluded folder D:\Manga");
	}

	[Test]
	public async Task An_excluded_folder_matches_whole_segments_only()
	{
		FolderSet excluded = new([AbsPath.TryParse(@"D:\Manga")!.Value]);

		await Assert.That(Run(Any(), RuleMode.Except, BookAt(@"D:\Manga Extra\x.cbz"), excluded).Accepted).IsTrue();
		await Assert.That(Run(Any(), RuleMode.Except, BookAt(@"D:\Manga\x.cbz"), excluded).Accepted).IsFalse();
		await Assert.That(Run(Any(), RuleMode.Except, BookAt(@"D:\Other\Manga\x.cbz"), excluded).Accepted).IsTrue();
	}

	[Test]
	public async Task A_fileless_book_is_never_in_an_excluded_folder()
	{
		FieldValue[] values = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		var fileless = new BookRecord(new BookId(Guid.NewGuid()), new BookLocation.Fileless(false), "f", values, new Dictionary<string, string>());
		FolderSet excluded = new([AbsPath.TryParse(@"D:\Manga")!.Value]);

		await Assert.That(Run(Any(), RuleMode.Except, fileless, excluded).Accepted).IsTrue();
	}

	private static BookRecord BookAt(string path)
	{
		FieldValue[] values = Fields.All.Select(_ => (FieldValue)FieldValue.Unset.Value).ToArray();
		return new BookRecord(
			new BookId(Guid.NewGuid()),
			new BookLocation.File(AbsPath.TryParse(path)!.Value, Exists: true),
			"b",
			values,
			new Dictionary<string, string>());
	}
}
