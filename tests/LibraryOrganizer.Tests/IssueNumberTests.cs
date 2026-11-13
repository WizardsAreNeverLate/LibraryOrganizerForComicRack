using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public class IssueNumberTests
{
	[Test]
	[Arguments("7", 3, "007")]
	[Arguments("7.1", 3, "007.1")]
	[Arguments("-1.5", 3, "-001.5")]
	[Arguments("-1.5", 2, "-01.5")]
	[Arguments("123", 2, "123")]
	[Arguments("½", 3, "½")]
	[Arguments("7b", 3, "7b")]
	[Arguments("", 3, "")]
	public async Task Pad_follows_the_2x_rules(string text, int width, string expected)
		=> await Assert.That(new IssueNumber(text).Pad(width)).IsEqualTo(expected);

	[Test]
	public async Task Sorts_numbers_by_value_not_by_text()
	{
		string[] sorted = new[] { "10", "9", "100", "1", "12.1", "12", "12a", "-1", "0", "½", "Annual", "1½" }
			.Select(text => new IssueNumber(text))
			.OrderBy(issue => issue)
			.Select(issue => issue.Text)
			.ToArray();

		await Assert.That(string.Join(" ", sorted)).IsEqualTo("-1 0 ½ 1 1½ 9 10 12 12a 12.1 100 Annual");
	}

	[Test]
	[Arguments("9", "10")]
	[Arguments("12", "12.1")]
	[Arguments("12", "12a")]
	[Arguments("12a", "12b")]
	[Arguments("7", "7.1")]
	[Arguments("120", "Annual")]
	public async Task Lower_issue_compares_below_higher(string lower, string higher)
	{
		await Assert.That(new IssueNumber(lower).CompareTo(new IssueNumber(higher))).IsLessThan(0);
		await Assert.That(new IssueNumber(higher).CompareTo(new IssueNumber(lower))).IsGreaterThan(0);
	}

	[Test]
	public async Task Compares_as_equal_only_when_the_text_is_equal()
	{
		await Assert.That(new IssueNumber("7").CompareTo(new IssueNumber("7"))).IsEqualTo(0);
		await Assert.That(new IssueNumber("07").CompareTo(new IssueNumber("7"))).IsNotEqualTo(0);
		await Assert.That(new IssueNumber("07")).IsNotEqualTo(new IssueNumber("7"));
	}
}
