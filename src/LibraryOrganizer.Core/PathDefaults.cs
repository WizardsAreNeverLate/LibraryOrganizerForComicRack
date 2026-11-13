using System.Collections.Generic;

namespace LibraryOrganizer;

public static class PathDefaults
{
	public const string FolderTemplate = @"{<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}";

	public const string FileTemplate = "{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}";

	public static IReadOnlyList<KeyValuePair<string, string>> IllegalCharacters { get; } =
	[
		new("?", ""),
		new("/", ""),
		new(@"\", ""),
		new("*", ""),
		new(":", " - "),
		new("<", "["),
		new(">", "]"),
		new("|", "!"),
		new("\"", "'"),
	];

	/// <summary>Index 0 is month 1. Months 13 to 16 are the seasons.</summary>
	public static IReadOnlyList<string> MonthNames { get; } =
	[
		"January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
		"Spring", "Summer", "Fall", "Winter",
	];
}
