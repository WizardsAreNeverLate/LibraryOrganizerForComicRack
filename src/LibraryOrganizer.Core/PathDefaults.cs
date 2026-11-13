using System.Collections.Generic;

namespace LibraryOrganizer;

/// <summary>The 2.x defaults a new profile starts from. The settings form, the importer and PathStyle read them from here.</summary>
public static class PathDefaults
{
	public const string FolderTemplate = @"{<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}";

	public const string FileTemplate = "{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}";

	/// <summary>Character to replacement, in the 2.x order (losettings.py:68).</summary>
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
