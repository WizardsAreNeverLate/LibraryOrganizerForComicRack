using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LibraryOrganizer.Plugin.Forms;

namespace LibraryOrganizer.FormPreview;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);

		if (Array.IndexOf(args, "--review") >= 0)
		{
			RunScreenshots.Interactive();
			return 0;
		}
		int flag = Array.IndexOf(args, "--screenshot");
		using var form = new ConfigForm(Sample(), customKeys: ["Reading list", "Source"])
		{
			Preview = SamplePreview,
		};
		if (flag < 0)
		{
			return form.ShowDialog() == DialogResult.OK ? 0 : 1;
		}
		if (flag + 1 >= args.Length)
		{
			Console.Error.WriteLine("--screenshot needs a file name, as in --screenshot shots\\config.png");
			return 2;
		}
		Screenshots(form, args[flag + 1]);
		return 0;
	}

	private static string SamplePreview(ProfileSettings profile)
		=> profile.UseFolder && !profile.UseFileName ? @"D:\Comics\DC Comics\Vertigo\Sandman (1989)" : "Sandman Vol.1 #012 (of 75) (Jan, 1990)";

	private static Settings Sample()
	{
		var tidy = new ProfileSettings
		{
			Name = "Tidy library",
			BaseFolder = @"D:\Comics",
			RequiredFields = [Fields.Series.Key, Fields.Number.Key],
			EmptySubstitutes = new System.Collections.Generic.Dictionary<string, string> { [Fields.Publisher.Key] = "Unknown Publisher" },
			ExcludedFolders = [@"D:\Comics\Unsorted", @"D:\Comics\Incoming"],
			KeepFolders = [@"D:\Comics\Incoming"],
			RuleMode = RuleMode.Except,
			Rules = new RuleSettings.Group(
				Combinator.Any,
				[
					new RuleSettings.Condition(Fields.Publisher.Key, Operator.Is, "Panini"),
					new RuleSettings.Group(
						Combinator.All,
						[
							new RuleSettings.Condition(Fields.Manga.Key, Operator.Is, "YesAndRightToLeft"),
							new RuleSettings.Condition(Fields.Year.Key, Operator.LessThan, "2005"),
						]),
					new RuleSettings.Condition(Fields.Tags.Key, Operator.IsEmpty, ""),
				]),
		};
		var broken = new ProfileSettings
		{
			Name = "Manga copies",
			Action = new ProfileAction.Copy(AddToLibrary: true),
			BaseFolder = @"D:\Manga",
			FolderTemplate = @"{<publisher>}\{<seriez>}{ (<startyear>}",
			FileTemplate = "{<series>}{ #<number3>}{ (<manga(Manga>)}",
			Rules = new RuleSettings.Group(
				Combinator.All,
				[
					new RuleSettings.Condition(Fields.Manga.Key, Operator.Is, "YesAndRightToLeft"),
					new RuleSettings.Condition(Fields.Year.Key, Operator.GreaterThan, "twenty"),
					new RuleSettings.Condition(Fields.Series.Key, Operator.MatchesRegex, "(unclosed"),
				]),
			RuleMode = RuleMode.Only,
		};
		return new Settings([tidy, broken], [], RunPreferences.Default);
	}

	private static void Screenshots(ConfigForm form, string file)
	{
		string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".", Path.GetFileNameWithoutExtension(file));
		Directory.CreateDirectory(Path.GetDirectoryName(stem) ?? ".");
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(-32000, 0);
		form.Show();
		Application.DoEvents();

		foreach (ConfigTab tab in Enum.GetValues(typeof(ConfigTab)))
		{
			form.ShowTab(tab);
			Capture(form, $"{stem}-{tab.ToString().ToLowerInvariant()}.png");
		}
		foreach (Field field in new[] { Fields.Writer, Fields.StartMonth, Fields.ReleasedDate, Fields.ReadPercentage })
		{
			form.ShowField(field);
			Capture(form, $"{stem}-insert-{field.Key}.png");
		}
		form.SelectProfile(1);
		form.ShowTab(ConfigTab.Templates);
		Capture(form, stem + "-templates-errors.png");
		form.ShowTab(ConfigTab.Rules);
		Capture(form, stem + "-rules-errors.png");
		form.CheckAll();
		Capture(form, stem + "-problems.png");
		RunScreenshots.Render(stem, Capture);
	}

	private static void Capture(Form form, string path)
	{
		for (int i = 0; i < 5; i++)
		{
			Application.DoEvents();
			System.Threading.Thread.Sleep(60);
		}
		using var bitmap = new Bitmap(form.Width, form.Height);
		form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
		bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
		Console.Out.WriteLine(path);
	}
}
