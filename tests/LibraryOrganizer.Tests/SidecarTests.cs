using System;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryOrganizer.Tests;

public sealed class SidecarTests : IDisposable
{
	private const string Seven = @"Incoming\seven.pdf";
	private const string SevenXml = @"Incoming\seven.xml";
	private const string Target = @"Comics\Marvel\X-Men\X-Men #7.pdf";
	private const string TargetXml = @"Comics\Marvel\X-Men\X-Men #7.xml";

	private readonly RealShelf shelf = new();

	public void Dispose() => shelf.Dispose();

	[Test]
	public async Task A_sidecar_xml_moves_and_is_renamed_with_its_book()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "metadata of seven");

		Plan plan = shelf.Plan(shelf.Profile());
		shelf.Run(plan);

		using (Assert.Multiple())
		{
			await Assert.That(plan.Operations.Script()).IsEqualTo($"move {shelf.P(Seven)} -> {shelf.P(Target)}\nsidecar {shelf.P(SevenXml)} -> {shelf.P(TargetXml)}");
			await Assert.That(shelf.Files()).IsEqualTo($"{Target}\n{TargetXml}");
			await Assert.That(shelf.Read(TargetXml)).IsEqualTo("metadata of seven");
			await Assert.That(shelf.PathOf(seven)).IsEqualTo(Target);
		}
	}

	[Test]
	public async Task Undo_moves_the_sidecar_back_with_its_book()
	{
		BookRecord seven = shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "metadata of seven");
		RunReport run = shelf.Run(shelf.Plan(shelf.Profile()));

		Plan undo = shelf.PlanUndo(run.Run);
		shelf.Run(undo);

		await Assert.That(undo.Operations.Script()).IsEqualTo($"sidecar {shelf.P(TargetXml)} -> {shelf.P(SevenXml)}\nmove {shelf.P(Target)} -> {shelf.P(Seven)}");
		await Assert.That(shelf.Files()).IsEqualTo($"{Seven}\n{SevenXml}");
		await Assert.That(shelf.PathOf(seven)).IsEqualTo(Seven);
	}

	[Test]
	public async Task A_sidecar_named_after_the_full_file_name_keeps_that_form()
	{
		shelf.Add(Seven, "7");
		shelf.Put(@"Incoming\seven.pdf.xml", "metadata of seven");

		Plan plan = shelf.Plan(shelf.Profile());
		shelf.Run(plan);

		await Assert.That(plan.Operations.Script()).IsEqualTo(
			$"move {shelf.P(Seven)} -> {shelf.P(Target)}\nsidecar {shelf.P(@"Incoming\seven.pdf.xml")} -> {shelf.P(Target + ".xml")}");
		await Assert.That(shelf.Read(Target + ".xml")).IsEqualTo("metadata of seven");
	}

	[Test]
	public async Task Both_sidecar_forms_move_when_both_are_there()
	{
		shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "short form");
		shelf.Put(@"Incoming\seven.pdf.xml", "long form");

		shelf.Run(shelf.Plan(shelf.Profile()));

		await Assert.That(shelf.Files()).IsEqualTo($"{Target}\n{Target}.xml\n{TargetXml}");
		await Assert.That(shelf.Read(TargetXml)).IsEqualTo("short form");
		await Assert.That(shelf.Read(Target + ".xml")).IsEqualTo("long form");
	}

	[Test]
	public async Task A_full_name_sidecar_follows_its_book_even_when_another_file_shares_the_short_name()
	{
		shelf.Add(Seven, "7");
		shelf.Put(@"Incoming\seven.pdf.xml", "metadata of the pdf");
		shelf.Put(@"Incoming\seven.cbz", "another format");

		Plan plan = shelf.Plan(shelf.Profile());

		await Assert.That(plan.Operations.Script()).IsEqualTo(
			$"move {shelf.P(Seven)} -> {shelf.P(Target)}\nsidecar {shelf.P(@"Incoming\seven.pdf.xml")} -> {shelf.P(Target + ".xml")}");
	}

	[Test]
	public async Task A_book_without_a_sidecar_moves_alone()
	{
		shelf.Add(Seven, "7");

		Plan plan = shelf.Plan(shelf.Profile());

		await Assert.That(plan.Operations.Script()).IsEqualTo($"move {shelf.P(Seven)} -> {shelf.P(Target)}");
	}

	[Test]
	public async Task An_xml_shared_by_two_files_of_the_same_name_stays_where_it_is()
	{
		shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "metadata");
		shelf.Put(@"Incoming\seven.cbz", "another format");

		Plan plan = shelf.Plan(shelf.Profile());

		await Assert.That(plan.Operations.Script()).IsEqualTo($"move {shelf.P(Seven)} -> {shelf.P(Target)}");
	}

	[Test]
	public async Task A_taken_sidecar_name_is_a_conflict_and_keep_both_numbers_book_and_sidecar_alike()
	{
		shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "metadata of seven");
		shelf.Put(TargetXml, "an unrelated xml");

		Profile profile = shelf.Profile();
		Plan open = shelf.Plan(profile);
		Conflict conflict = open.Conflicts.Single();
		Plan keepBoth = shelf.Plan(Decisions.None.WithResolution(conflict.Key, new Resolution.KeepBoth()), profile);
		shelf.Run(keepBoth);

		using (Assert.Multiple())
		{
			await Assert.That(conflict.Kind).IsTypeOf<ConflictKind.SidecarTaken>();
			await Assert.That(open.Approve()).IsNull();
			await Assert.That(shelf.Read(TargetXml)).IsEqualTo("an unrelated xml");
			await Assert.That(shelf.Files()).IsEqualTo(
				@"Comics\Marvel\X-Men\X-Men #7 (1).pdf" + "\n" + @"Comics\Marvel\X-Men\X-Men #7 (1).xml" + "\n" + TargetXml);
			await Assert.That(shelf.Read(@"Comics\Marvel\X-Men\X-Men #7 (1).xml")).IsEqualTo("metadata of seven");
		}
	}

	[Test]
	public async Task Replace_recycles_the_replaced_books_sidecar_too()
	{
		shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "metadata of seven");
		shelf.Put(Target, "the old copy");
		shelf.Put(TargetXml, "metadata of the old copy");

		shelf.Run(shelf.Plan(Decisions.None.WithDefault(new Resolution.Replace()), shelf.Profile()));

		using (Assert.Multiple())
		{
			await Assert.That(shelf.Files()).IsEqualTo($"{Target}\n{TargetXml}");
			await Assert.That(shelf.Read(Target)).IsEqualTo(RealShelf.ContentOf(Seven));
			await Assert.That(shelf.Read(TargetXml)).IsEqualTo("metadata of seven");
		}
	}

	[Test]
	public async Task Two_books_whose_sidecars_would_meet_conflict_instead_of_overwriting()
	{
		shelf.Add(Seven, "7");
		shelf.Put(SevenXml, "metadata of seven");
		shelf.Add(@"Other\seven.cbz", "7");
		shelf.Put(@"Other\seven.xml", "metadata of the cbz");

		Plan plan = shelf.Plan(shelf.Profile());

		await Assert.That(plan.Conflicts.Select(c => c.Kind).OfType<ConflictKind.SidecarTaken>().Count()).IsEqualTo(1);
		await Assert.That(plan.Approve()).IsNull();
	}
}
