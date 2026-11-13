using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class TemplateParseTests
{
	private static string Spans(string source, TemplateTarget target = TemplateTarget.FileName)
		=> string.Join(" ", Template.Parse(source, target).Errors.Select(error => $"{error.Start}+{error.Length}"));

	[Test]
	public async Task The_2x_default_templates_parse()
	{
		await Assert.That(Template.Parse(PathDefaults.FolderTemplate, TemplateTarget.Folder).Ok).IsTrue();
		await Assert.That(Template.Parse(PathDefaults.FileTemplate, TemplateTarget.FileName).Ok).IsTrue();
	}

	[Test]
	public async Task An_unknown_field_is_underlined_and_the_closest_name_suggested()
	{
		TemplateParse parse = Template.Parse("{<serie>}", TemplateTarget.FileName);

		await Assert.That(parse.Ok).IsFalse();
		await Assert.That(Spans("{<serie>}")).IsEqualTo("1+7");
		await Assert.That(parse.Errors[0].Message).Contains("'series'");
	}

	[Test]
	public async Task Field_names_are_case_sensitive()
	{
		await Assert.That(Spans("{<Series>}")).IsEqualTo("1+8");
	}

	[Test]
	public async Task Digits_mixed_with_parentheses_are_an_error_on_the_whole_token()
	{
		await Assert.That(Spans("{<number2(x)>}")).IsEqualTo("1+12");
		await Assert.That(Spans("{<!month#2(!0[1-6])>}")).IsEqualTo("1+19");
		await Assert.That(Spans("{<writer(, )2>}")).IsEqualTo("1+13");
	}

	[Test]
	public async Task An_unclosed_group_is_reported_at_its_opening_brace()
	{
		await Assert.That(Spans("{<series>")).IsEqualTo("0+1");
		await Assert.That(Spans("A {<series>}{ #<number>")).IsEqualTo("12+1");
	}

	[Test]
	public async Task A_closing_brace_with_no_group_is_reported()
	{
		await Assert.That(Spans("{<series>}}")).IsEqualTo("10+1");
	}

	[Test]
	public async Task A_group_with_no_field_is_reported_over_the_whole_group()
	{
		await Assert.That(Spans("x{abc}")).IsEqualTo("1+5");
		await Assert.That(Spans("{}")).IsEqualTo("0+2");
	}

	[Test]
	public async Task A_second_field_in_one_group_is_reported()
	{
		await Assert.That(Spans("{<series> <number>}")).IsEqualTo("10+8");
	}

	[Test]
	public async Task Every_error_is_reported_in_source_order()
	{
		await Assert.That(Spans("{<serie>}{abc}{<number2(x)>}")).IsEqualTo("1+7 9+5 15+12");
	}

	[Test]
	public async Task An_error_in_a_nested_group_keeps_its_own_position()
	{
		await Assert.That(Spans("{ ({<mnth>, }<year>)}")).IsEqualTo("4+6");
	}

	[Test]
	[Arguments("{<series2>}")]
	[Arguments("{<series(x)>}")]
	[Arguments("{<year#>}")]
	[Arguments("{<month#(x)>}")]
	[Arguments("{<?format>}")]
	[Arguments("{<?number2>}")]
	[Arguments("{<?format(!()>}")]
	[Arguments("{<manga(a)(b)>}")]
	[Arguments("{<writer(, )>}")]
	[Arguments("{<writer(, )(book)>}")]
	[Arguments("{<read(Read)(~)(90)>}")]
	[Arguments("{<read(Read)(>)(many)>}")]
	[Arguments("{<first>}")]
	[Arguments("{<first(Nothing)>}")]
	[Arguments("{<counter>}")]
	[Arguments("{<counter(1)(1)(x)>}")]
	[Arguments("{<Custom>}")]
	[Arguments("{<ReleasedDate(yyyy)(MM)>}")]
	[Arguments("{<Rating2>}")]
	[Arguments("{<number999>}")]
	[Arguments("{<>}")]
	[Arguments("{< series>}")]
	[Arguments("{<series}")]
	[Arguments("{<writer(, >}")]
	public async Task Arguments_a_field_does_not_accept_are_one_error(string source)
	{
		TemplateParse parse = Template.Parse(source, TemplateTarget.FileName);

		await Assert.That(parse.Ok).IsFalse();
		await Assert.That(parse.Errors.Count).IsEqualTo(1);
	}

	[Test]
	[Arguments("{<number3>}")]
	[Arguments("{<number0>}")]
	[Arguments("{<startmonth#2>}")]
	[Arguments("{<EndMonth#>}")]
	[Arguments("{<month2>}")]
	[Arguments("{ ({<month>, }<year>)}")]
	[Arguments("{<manga(Manga)>}")]
	[Arguments("{<blackAndWhite(BW)(!)>}")]
	[Arguments("{[Read]<?read(x)(>)(90)(x)>}")]
	[Arguments("{<!read(x)(<)(10)(x)>}")]
	[Arguments("{<first(Series)>}")]
	[Arguments("{<first(ShadowSeries)>}")]
	[Arguments("{<first(Start Year)>}")]
	[Arguments("{<counter(1)(1)(3)>}")]
	[Arguments("{<ReleasedDate>}")]
	[Arguments("{<AddedDate(yyyy-MM)>}")]
	[Arguments("{<Custom(My Key)>}")]
	[Arguments("{<writer(, )(issue)>}")]
	[Arguments("{<characters( & )(series)>}")]
	[Arguments("{<!number>}")]
	[Arguments("{<?format(!Trade.*)>}")]
	[Arguments("{<?format(!)>}")]
	[Arguments("plain <text> outside a group")]
	[Arguments("")]
	public async Task The_2x_token_forms_parse(string source)
	{
		TemplateParse parse = Template.Parse(source, TemplateTarget.FileName);

		await Assert.That(string.Join("; ", parse.Errors.Select(error => error.Message))).IsEqualTo("");
		await Assert.That(parse.Ok).IsTrue();
	}

	[Test]
	[Arguments("{")]
	[Arguments("}")]
	[Arguments("{<")]
	[Arguments("{<a(")]
	[Arguments("{<number2(")]
	[Arguments("}{")]
	[Arguments("{<?>}")]
	[Arguments("{<!>}")]
	[Arguments("{<#>}")]
	[Arguments("{{{<series>")]
	[Arguments("{<series>{")]
	[Arguments("{<series><")]
	[Arguments(@"\\\")]
	public async Task Broken_input_gives_errors_inside_the_source_and_never_throws(string source)
	{
		foreach (TemplateTarget target in new[] { TemplateTarget.Folder, TemplateTarget.FileName })
		{
			TemplateParse parse = Template.Parse(source, target);

			await Assert.That(parse.Ok).IsEqualTo(parse.Errors.Count == 0);
			foreach (TemplateError error in parse.Errors)
			{
				await Assert.That(error.Start >= 0 && error.Length >= 1 && error.Start + error.Length <= source.Length).IsTrue();
			}
		}
	}

	[Test]
	public async Task A_template_lists_the_fields_and_custom_keys_it_reads()
	{
		Template template = TemplateFixture.Parse("{<series>}{ <first(Publisher)>}{ <Custom(Shelf)>}{ <series>}", TemplateTarget.FileName);

		await Assert.That(string.Join(" ", template.Fields.Select(field => field.Key).OrderBy(key => key, System.StringComparer.Ordinal)))
			.IsEqualTo("custom firstLetter publisher series");
		await Assert.That(template.CustomKeys.Single()).IsEqualTo("Shelf");
		await Assert.That(template.Source).IsEqualTo("{<series>}{ <first(Publisher)>}{ <Custom(Shelf)>}{ <series>}");
		await Assert.That(template.Target).IsEqualTo(TemplateTarget.FileName);
	}

	[Test]
	[Arguments("{<series>}{ #<number3>}", false)]
	[Arguments("{<writer(, )(issue)>}", false)]
	[Arguments("{<startyear>}", true)]
	[Arguments("{<number0>}", true)]
	[Arguments("{<month#0>}", true)]
	[Arguments("{<writer(, )(series)>}", true)]
	[Arguments("{<first(Start Year)>}", true)]
	public async Task A_template_says_whether_it_needs_series_data(string source, bool usesSeries)
	{
		await Assert.That(TemplateFixture.Parse(source, TemplateTarget.FileName).UsesSeries).IsEqualTo(usesSeries);
	}
}
