using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class FieldsTests
{
	private static readonly string[] TemplateNames2x =
	[
		"series", "number", "count", "volume", "year", "month", "Day", "title", "format", "publisher", "imprint", "altSeries",
		"altNumber", "altCount", "ageRating", "language", "writer", "penciller", "inker", "colorist", "letterer", "coverartist",
		"editor", "genre", "tags", "characters", "teams", "locations", "storyarc", "seriesgroup", "maincharacter", "scaninfo",
		"manga", "seriesComplete", "read", "first", "counter", "startyear", "startmonth", "EndYear", "EndMonth",
		"firstissuenumber", "lastissuenumber", "ReleasedDate", "AddedDate", "Rating", "CommunityRating", "Custom",
	];

	private static readonly string[] RuleEditorNames2x =
	[
		"Age Rating", "Alternate Count", "Alternate Number", "Alternate Series", "Black And White", "Characters", "Count",
		"File Format", "File Name", "File Path", "Format", "Genre", "Imprint", "Language", "Locations", "Main Character Or Team",
		"Manga", "Month", "Notes", "Number", "Publisher", "Rating", "Read Percentage", "Review", "Scan Information", "Series",
		"Series Complete", "Series Group", "Start Month", "Start Year", "Story Arc", "Tags", "Teams", "Title", "Volume", "Web", "Year",
	];

	private static string Duplicates(IEnumerable<string> names)
		=> string.Join(", ", names.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key));

	[Test]
	public async Task Keys_are_unique_and_resolve()
	{
		await Assert.That(Duplicates(Fields.All.Select(f => f.Key))).IsEqualTo("");
		foreach (Field field in Fields.All)
		{
			await Assert.That(Fields.ByKey(field.Key)).IsSameReferenceAs(field);
		}
		await Assert.That(Fields.ByKey("no-such-key")).IsNull();
	}

	[Test]
	public async Task Template_names_are_unique_and_resolve()
	{
		List<Field> templated = Fields.All.Where(f => f.TemplateName != null).ToList();

		await Assert.That(Duplicates(templated.Select(f => f.TemplateName!))).IsEqualTo("");
		foreach (Field field in templated)
		{
			await Assert.That(Fields.ByTemplateName(field.TemplateName!)).IsSameReferenceAs(field);
		}
	}

	[Test]
	public async Task A_field_has_a_template_name_exactly_when_templates_can_use_it()
	{
		string mismatched = string.Join(", ", Fields.All
			.Where(f => f.Uses.HasFlag(FieldUses.Template) != (f.TemplateName != null))
			.Select(f => f.Key));

		await Assert.That(mismatched).IsEqualTo("");
	}

	[Test]
	public async Task Every_2x_template_name_resolves()
	{
		string missing = string.Join(", ", TemplateNames2x.Where(name => Fields.ByTemplateName(name) is null));

		await Assert.That(missing).IsEqualTo("");
	}

	[Test]
	public async Task Template_names_are_case_sensitive()
	{
		await Assert.That(Fields.ByTemplateName("EndYear")).IsSameReferenceAs(Fields.EndYear);
		await Assert.That(Fields.ByTemplateName("endyear")).IsNull();
		await Assert.That(Fields.ByTemplateName("Series")).IsNull();
		await Assert.That(Fields.ByTemplateName("month#")).IsNull();
	}

	[Test]
	public async Task Every_2x_rule_editor_field_is_usable_in_rules()
	{
		string missing = string.Join(", ", RuleEditorNames2x.Where(name => Fields.ByLegacyName(name)?.Uses.HasFlag(FieldUses.Rule) != true));

		await Assert.That(missing).IsEqualTo("");
	}

	[Test]
	public async Task Rules_only_fields_have_no_template_name()
	{
		foreach (Field field in new[] { Fields.FilePath, Fields.FileName, Fields.FileFormat, Fields.Notes, Fields.Review, Fields.Web })
		{
			await Assert.That(field.TemplateName).IsNull();
			await Assert.That(field.Uses).IsEqualTo(FieldUses.Rule);
		}
	}

	[Test]
	[Arguments("ShadowSeries", "series")]
	[Arguments("Series", "series")]
	[Arguments("Released Date", "releasedDate")]
	[Arguments("ReleasedDate", "releasedDate")]
	[Arguments("ReleasedTime", "releasedDate")]
	[Arguments("LanguageISO", "language")]
	[Arguments("LanguageAsText", "language")]
	[Arguments("ShadowNumber", "number")]
	[Arguments("Count", "count")]
	[Arguments("Counter", "counter")]
	[Arguments("Read", "readPercentage")]
	[Arguments("Read Percentage", "readPercentage")]
	[Arguments("StartYear", "startYear")]
	[Arguments("Start Year", "startYear")]
	[Arguments("First Letter", "firstLetter")]
	[Arguments("Main Character Or Team", "mainCharacterOrTeam")]
	public async Task Legacy_2x_names_resolve(string legacyName, string expectedKey)
		=> await Assert.That(Fields.ByLegacyName(legacyName)?.Key).IsEqualTo(expectedKey);

	[Test]
	public async Task A_legacy_name_belongs_to_one_field()
	{
		await Assert.That(Duplicates(Fields.All.SelectMany(f => f.LegacyNames))).IsEqualTo("");
		await Assert.That(Fields.ByLegacyName("NoSuchProperty")).IsNull();
	}

	[Test]
	public async Task Ordinals_index_All()
	{
		for (int i = 0; i < Fields.All.Count; i++)
		{
			await Assert.That(Fields.All[i].Ordinal).IsEqualTo(i);
		}
	}

	[Test]
	public async Task Only_book_fields_with_a_value_bind_to_a_host_property()
	{
		string mismatched = string.Join(", ", Fields.All
			.Where(f => (f.HostProperty != null) != (f.Source == FieldSource.Book && f.Kind != FieldKind.Synthetic))
			.Select(f => f.Key));

		await Assert.That(mismatched).IsEqualTo("");
	}

	[Test]
	public async Task Series_fields_derive_from_a_book_field_of_the_same_kind()
	{
		foreach (Field field in Fields.All)
		{
			if (field.Source == FieldSource.Series)
			{
				await Assert.That(field.DerivedFrom?.Source).IsEqualTo(FieldSource.Book);
				await Assert.That(field.DerivedFrom?.Kind).IsEqualTo(field.Kind);
			}
			else
			{
				await Assert.That(field.DerivedFrom).IsNull();
			}
		}
		await Assert.That(Fields.StartYear.DerivedFrom).IsSameReferenceAs(Fields.Year);
		await Assert.That(Fields.EndMonth.DerivedFrom).IsSameReferenceAs(Fields.Month);
		await Assert.That(Fields.LastIssueNumber.DerivedFrom).IsSameReferenceAs(Fields.Number);
	}

	[Test]
	public async Task Only_book_fields_can_be_required()
	{
		string wrong = string.Join(", ", Fields.All
			.Where(f => f.Uses.HasFlag(FieldUses.Required) && (f.Source != FieldSource.Book || f.Kind == FieldKind.Synthetic))
			.Select(f => f.Key));

		await Assert.That(wrong).IsEqualTo("");
		await Assert.That(Fields.Series.Uses.HasFlag(FieldUses.Required)).IsTrue();
	}

	[Test]
	public async Task Raw_text_is_culture_independent()
	{
		await Assert.That(FieldValue.Unset.Value.ToRawText()).IsEqualTo("");
		await Assert.That(new FieldValue.Text("Trade: Paperback").ToRawText()).IsEqualTo("Trade: Paperback");
		await Assert.That(new FieldValue.Int(1992).ToRawText()).IsEqualTo("1992");
		await Assert.That(new FieldValue.Decimal(4.5m).ToRawText()).IsEqualTo("4.5");
		await Assert.That(new FieldValue.Decimal(4.0m).ToRawText()).IsEqualTo("4");
		await Assert.That(new FieldValue.Issue(new IssueNumber("7.1")).ToRawText()).IsEqualTo("7.1");
		await Assert.That(new FieldValue.YesNo(YesNoValue.Yes).ToRawText()).IsEqualTo("Yes");
		await Assert.That(new FieldValue.YesNo(YesNoValue.No).ToRawText()).IsEqualTo("No");
		await Assert.That(new FieldValue.YesNo(YesNoValue.YesRightToLeft).ToRawText()).IsEqualTo("YesAndRightToLeft");
		await Assert.That(new FieldValue.Date(new DateTime(1992, 3, 9, 14, 30, 0)).ToRawText()).IsEqualTo("1992-03-09");
	}

	[Test]
	public async Task Items_split_on_commas_and_keep_the_raw_text()
	{
		var items = (FieldValue.Items)FieldValue.ParseItems(" Chris Claremont, Jim Lee ,, ");

		await Assert.That(string.Join("|", items.Values)).IsEqualTo("Chris Claremont|Jim Lee");
		await Assert.That(items.ToRawText()).IsEqualTo("Chris Claremont, Jim Lee ,,");
		await Assert.That(FieldValue.ParseItems(" , ").IsUnset).IsTrue();
		await Assert.That(FieldValue.ParseItems(null).IsUnset).IsTrue();
	}

	[Test]
	public async Task A_book_record_needs_one_value_per_field()
		=> await Assert.That(() => new BookRecord(
			new BookId(Guid.NewGuid()), new BookLocation.Fileless(HasCustomCover: false), "x", new FieldValue[3], new Dictionary<string, string>()))
			.Throws<ArgumentException>();
}
