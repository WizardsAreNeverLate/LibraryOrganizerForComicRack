# Library Organizer for ComicRack CE (.NET): candidate design "plan is a value"

## Problem

The user wants a C# ComicRack CE plugin, built on the `DotNetPlugin` API, that moves and renames comic files into folders built from templates and rules, with the feature set of the Python Library Organizer 2.1.13. Compatibility is optional, and breaking changes that make the plugin better are approved.

Four facts from the grounding make the shape non-obvious.

1. ComicRack calls every hook on the UI thread, and the live `ComicBook` is a mutable, event-firing object (`FilePath`'s setter fires `BookChanged` and `FileRenamed`). The engine must not hold a live book on a background thread or write one there.
2. 2.x mixes three concerns in one pass: rendering, asking the user (multi-value dialog via `form.Invoke` from inside the template engine), and faking the disk for Simulate (`CreatedPaths`, `MovedBooks`). Most of the defects in `07-rebuild-guide.md` come from that mix.
3. The 2.x renderer chooses a code path from the argument count at render time (`get_field_text`, 13 rows). An invalid token is discovered per book and silently left in the output.
4. `ComicBook.RenameFile` in CE returns early when the new path equals the old one ignoring case, so the 2.x case-fix path does nothing in CE. `ComicBook.CopyFrom` copies `Id` and `FilePath`, so it cannot create a copy's library entry as is.

## Usage (caller's view)

The Core library (`LibraryOrganizer.Core`, no ComicRack or WinForms reference) has five public entry points. Everything else is `internal`.

```csharp
// 1. Templates parse once, with positioned errors. The editor calls this on every keystroke.
TemplateParse parse = Template.Parse("{<series>}{ #<number0>}{ (<startyear>)}", TemplateTarget.FileName);
foreach (TemplateError e in parse.Errors) editor.Underline(e.Start, e.Length, e.Message);

// 2. A run is input -> Plan. The Plan is a value. Answers and conflict choices produce a new Plan.
Plan plan = Organizer.Plan(new OrganizeInput(profiles, snapshot.Selection, snapshot.Series, answers, fileProbe));
while (plan.Questions.FirstOrDefault() is { } q)
    plan = plan.WithAnswer(q.Key, ui.Ask(q));            // the engine never calls UI
plan = plan.WithDefaultResolution(Resolution.KeepBoth);  // "do this for all" is a table default, not a sticky flag
plan = plan.WithResolution(plan.Conflicts[0].Key, Resolution.Skip);

// 3. Only an approved plan executes. Approval is a type gate.
if (plan.Approve() is ApprovedPlan approved)
{
    RunReport report = await new Executor(fileSystem, libraryGateway, journals).RunAsync(approved, progress, ct);
}

// 4. Undo is a plan built from a journal, reviewed and executed by the same code.
Plan undo = Organizer.PlanUndo(journals.Read(runId), fileProbe);

// 5. Live preview of one profile on one book, with placeholders for unanswered multi-value tokens.
PreviewResult p = Organizer.Preview(profile, book, series, answers);
label.Text = p.Path ?? p.Problem;  // "D:\Comics\Marvel\X-Men (1991)\X-Men #007 ‹writer?›.cbz"
```

Call site in the plugin, the Books hook (UI thread):

```csharp
void IBooks.Execute(ComicBook[] books) => Session.For(ComicRack, ScriptPath).Organize(books, RunStyle.Review);

// Session.Organize
IReadOnlyList<Profile> profiles = ProfilePicker.Pick(owner, settings);           // ordered, last move profile wins
LibrarySnapshot snap = library.Snapshot(books, Organizer.Demand(profiles));      // UI thread, reads only the fields the profiles use
RunWindow.Run(owner, async (ui, ct) =>
{
    Plan? plan = await Task.Run(() => Organizer.Plan(new OrganizeInput(profiles, snap.Selection, snap.Series, answers.Load(), disk)), ct);
    plan = await ui.Ask(plan);                         // re-plans on a worker after each answer; null = cancelled
    plan = plan is null ? null : await ui.Review(plan);                    // one grid: every book, every op, every conflict; null = closed
    if (plan?.Approve() is not ApprovedPlan ok) return;
    answers.Save(plan.Answers);
    ui.ShowReport(await new Executor(disk, snap.Gateway, journals).RunAsync(ok, ui.Progress, ct));  // file I/O on the pool, library writes back on the UI thread
});
```

Call site in a test (TUnit, no ComicRack):

```csharp
var book = TestBooks.Book(series: "X-Men", volume: 2, number: "7", publisher: "Marvel", year: 1992, month: 3);
var series = SeriesIndex.Build([book, .. TestBooks.Issues("X-Men", 2, "Marvel", 1..120, firstYear: 1991)]);
Profile profile = TestProfiles.Default(baseFolder: @"D:\Comics");   // 2.x default templates, compiled from ProfileSettings
PreviewResult r = Organizer.Preview(profile, book, series, AnswerBook.Empty);
await Assert.That(r.Path).IsEqualTo(@"D:\Comics\Marvel\X-Men (1991)\X-Men Vol.2 #07 (of 12) (March, 1992).cbz");
```

## Shape

### Data structures first

**Field registry** (`Fields.cs`). One `Field` per comic attribute: stable settings key, template name, display name, `FieldKind`, `FieldSource` (book, series, run), the ComicRack property it snapshots from, the 2.x legacy names for import, and the UI group. `Fields.All` is the one list. Template names, rule operators (`Operators.For(kind)`), required-field and empty-substitute pickers, the UI insert tabs, the importer's name maps, and the host snapshot all derive from it, per model-the-domain. The host adapter binds `HostProperty` to `ComicBook` by reflection once, and a plugin test asserts every property resolves, so a new field is one registry line.

**BookRecord** (`Books.cs`). A frozen snapshot taken on the UI thread: `BookId` (the ComicRack `Guid`), a `BookLocation` (`File(path)` or `Fileless(hasCustomCover)`), a dense `FieldValue[]` indexed by `Field.Ordinal`, and the custom values. `FieldValue` is a closed union: `Unset`, `Text`, `Int`, `Decimal`, `Issue` (natural-ordered issue number), `YesNo`, `Date`, `Items`. "Unset" is its own case, so `Year < 2000` never matches a book with no year, per type-system-discipline. The snapshot reads only the fields `Organizer.Demand(profiles)` names, so a 100k-book library costs three reads per book for the series key and a full read only for the selection.

**SeriesIndex**. Built per run from the snapshot: `SeriesKey` (publisher, series, volume; case-insensitive) to issues sorted by year, month, natural number. First and last issue are defined once here and fix the 2.x defects (year typo, string sort, `12.1` never last). It is rebuilt per run and per preview session, so it never goes stale.

**Template tree** (`Templates.cs`). `Template.Parse` builds `Node = Literal | FolderBreak | Group(before, Token, after)` and resolves each token's arguments into a typed `ValueFormat` at parse time: `Padded(Fixed n | AutoFromLastIssue)`, `MonthName`, `MonthNumber`, `YesNoText`, `ReadThreshold`, `FirstLetterOf(Field)`, `Counter`, `DateFormat`, `CustomKey`, `MultiValue(separator, scope)`. The 13-row renderer table of 2.x becomes parse rules keyed by `FieldKind`, so a bad token is a positioned parse error, not a silent literal at render time. The `?`/`!` prefixes plus the last argument become a typed `Test` (`Equals`, `NotEquals`, `MatchesStart`, `NotMatchesStart`, `IsEmpty`). The tokenizer keeps the 2.x lexical rules (names have no digits, args are a digit run or parenthesized groups), so `<number3>`, `<startmonth#2>` and `{ ({<month>, }<year>)}` parse as before.

**Rendered path**. Rendering emits `Piece = Text(raw) | Break`, never a string with backslashes. Only a literal `\` in a folder template and the multi-value "one folder per value" answer emit `Break`. `PathStyle.Clean` is the one place values are cleaned: split on `Break`, apply the illegal-character table, collapse spaces, trim dots and spaces, substitute the empty-folder name. Tests compare raw values, so `<?format(Trade: Paperback)>` matches.

**Rules** (`Rules.cs`). `Rule = Group(Any|All, children) | Condition(Field, Operator, Operand)`. `Condition.Create` parses the operand for the field's kind at the settings boundary and rejects an operator the kind does not support, so the rule editor and the evaluator cannot disagree. `Filter = (FolderSet excluded, RuleMode Only|Except, Group root)`. `FolderSet` tests normalized, case-insensitive folder prefixes.

**Profile** (`Profiles.cs`). Two types with one mapping. `ProfileSettings` is the editable, storable text form. `ProfileSettings.Compile()` returns a `Profile` with parsed templates and rules, or the list of errors. The engine only accepts `Profile`, per boundary-discipline. `Destination` is a closed union (`FolderAndName`, `FolderOnly`, `NameOnly`) so "both off" cannot exist. `RequiredFields` carries its own route (`Skip` or `MoveUnder(folder)`), so "move failed with no folder" cannot exist. `ProfileId` is a `Guid`, names are labels.

**Plan** (`Planning.cs`). `Plan` holds `Entries` (one per book and profile, with an `EntryOutcome`), `Questions`, `Conflicts`, and the `AnswerBook` it used. A `Plan` stores its own input, so `WithAnswer`, `WithResolution`, and `WithDefaultResolution` re-derive a whole new plan. `Approve()` returns an `ApprovedPlan` only when nothing is pending. `ApprovedPlan.Operations` is the only thing the executor reads.

**Operation**. A closed set: `MoveFile`, `RenameCase`, `CopyFile`, `WriteCover`, `RecycleFile`, `PruneFolders`. Each has `IsSatisfied(probe)` (its end state already holds) and `Inverse()` (its undo operation, or `null` when it cannot be undone).

**Journal** (`Execution.cs`). One file per run under `%APPDATA%\LibraryOrganizer.NET\runs\`. The whole approved plan is written first, one XML element per line, then `done`/`failed` lines are appended and flushed per operation. A torn last line is ignored. Multiple runs are kept.

### Data flow

```
UI thread                         worker                              UI thread
ComicBook[] --Snapshot(demand)--> BookRecord[] + SeriesIndex
                                  Organizer.Plan  --> Plan (questions?) --> ask --> plan.WithAnswer ...
                                                       (conflicts?)    --> review grid --> plan.WithResolution ...
                                  plan.Approve() --> ApprovedPlan
Executor loop (UI context): journal.Begin(op) -> await Task.Run(file op) -> gateway.Apply(op) on UI -> journal.Done(op)
```

The executor loop runs on the UI `SynchronizationContext` inside a modal run window, the same pattern as the sibling plugin's `ScrapeHost`. File I/O runs on the thread pool. Each `await` returns to the UI thread, where the gateway writes `ComicBook.FilePath`, `AddNewBook`, `RemoveBook`, and `LastPageRead`. No `Invoke`, no lock, no library write off the UI thread, per separate-before-serializing-shared-state (one writer, the UI thread, by construction).

### Planning rules, in one place

For each book, in selection order, `Organizer.Plan` does this.

1. Evaluate every profile's filter and required fields on the record. Nothing renders yet.
2. Move profiles: render only the last accepting one. If its render fails, fall back to the previous accepting move profile. Earlier ones get `Overridden(by)`.
3. Copy profiles: each accepting one renders and adds a `CopyFile`.
4. Target equals source: `AlreadyInPlace`. Equal ignoring case: `RenameCase` (two-step move through a temporary name, because CE's `RenameFile` skips case-only changes).
5. A multi-value token with no answer and no matching preset yields `NeedsAnswer(key)` for this entry and a `Question` on the plan.

Then across the whole plan, it finds conflicts: two entries with one target, a target that exists on disk and that the plan does not move away first, and a path over the length limit. A target occupied by a file the plan moves elsewhere is not a conflict. It is an ordering constraint, and a two-book swap goes through a temporary name. Finally, it predicts which source folders the moves will empty and adds one `PruneFolders` operation, so the review shows folder removal (2.x Simulate never did).

Counters are threaded through the fold, keyed by profile and token position, and advance only when a render is used. A book filtered out, overridden, or waiting on an answer does not consume a number. This fixes the shared counter defect and keeps numbering deterministic after re-planning.

### Load-bearing decisions

- **Simulate is no longer a profile mode.** The review grid is the simulation: every run shows the plan, and the user presses **Run** or closes it. A profile is `Move` or `Copy(addToLibrary)`. "Organize now" executes without the grid when the plan has no questions and no conflicts. The importer maps `Simulate` to `Move` and notes it. This deletes the virtual file system 2.x needed, per experience-first and subtract-before-you-add.
- **Questions and conflicts are plan data.** The engine never calls UI, so the preview cannot open a dialog while the user types, and quick runs, startup runs, and tests use the same code with a different answer source.
- **Undo is a plan.** `PlanUndo` reads the journal, checks the disk for each operation's end state (not the journal's status flags), and emits the inverses in reverse order. It goes through the same conflict review and executor, writes its own journal, and can itself be undone. It looks books up by `BookId`, never by profile name.
- **Every operation is idempotent.** The executor checks `IsSatisfied` before acting, so "finish an interrupted run" is the same `RunAsync` call on the same approved plan, per make-operations-idempotent.
- **Answers persist.** Series and issue answers and "always use" presets live in `answers.xml`, keyed by `SeriesKey` and `BookId`. Re-organizing a series does not ask again. Configure lists remembered choices so the user can forget one.
- **Settings live in `%APPDATA%\LibraryOrganizer.NET\`** (`settings.xml` with a schema version, `answers.xml`, `runs\`), not in the plugin folder, so a reinstall or a read-only Program Files folder cannot lose them. The 2.x `losettingsx.dat` and `.lop` files (roots `Profiles`, `Profile`, `Settings`, `Setting`) go through `LegacyImport`, which runs ordered migrations and returns warnings for each changed behavior.

### Interface depth

The public surface is `Template.Parse`, `Organizer.Plan/PlanUndo/Preview/Demand`, the `Plan` value with three `With*` methods and `Approve`, `Executor.RunAsync`, `SettingsStore`, `AnswerStore`, `JournalStore`, `LegacyImport.Read`, the registry, and the domain records. Behind it sit the tokenizer, the format resolver, the renderer, the cleaner, the series lookups, conflict detection, rename-candidate search, ordering with swap handling, prune prediction, journal parsing, and undo derivation. The host implements two small ports: `IFileSystem` (with its read-only half `IFileProbe`) and `ILibraryGateway` (five methods). The caller never assembles a pipeline: there is one way to plan, one way to change a plan, and one way to execute.

## Synthesis decision

Four runners (opus, fable, sonnet, haiku) sketched independently. All four converged on the same shape: frozen book
snapshots, one field registry, a tree template parser, multi-value questions and conflicts as plan data, and a journal
written before any file moves. The opus candidate is the base. A fable cross-judge scored it highest on all six rubric
criteria and picked it too.

**This section overrides the sketch in `sketch/` wherever they disagree.** The sketch files are the base candidate as
written; implement against this section first.

Grafts and simplifications:

1. **Planning is one function, not a plan that re-plans itself** (from fable). `Organizer.Plan(OrganizeInput input,
   Decisions decisions) -> Plan`. `Decisions` is an immutable record: `AnswerBook Answers`, resolutions by `ConflictKey`,
   and an optional default `Resolution`. `Plan` drops `WithAnswer`/`WithResolution`/`WithDefaultResolution` and does not
   hold its input or an `IFileProbe`, so a re-plan is an explicit call the host makes on a worker. `Plan.Approve()` stays.
2. **Counters are derived from position** (from fable). A counter token renders `Start + Step * ordinal`, where the ordinal
   is the book's index among the entries that profile actually renders, in selection order. Key: (profile, template
   target, token start). No `CounterState` threading. Fixes the cross-template key collision the judge found.
3. **A render returns every unanswered question at once** (from fable), not only the first.
4. **`FieldUses` flags on each field** (from fable): `Template`, `Rule`, `Required`. Rules-only fields such as file path
   and file name live in the registry with `Rule` only.
5. **Reserved Windows names** (CON, PRN, AUX, NUL, COM1-9, LPT1-9) get a trailing `_` in `PathStyle` (from fable).
6. **Journal marks have two phases** (from sonnet): `file` after the disk change, `applied` after the library write, so
   resume knows which half is missing.
7. **Library path index** (judge risk 3). `OrganizeInput` carries `IReadOnlyDictionary<AbsPath, BookId> LibraryFiles`,
   built from `App.GetLibraryBooks()`. Never use `App.GetBook(path)`: it returns a temporary non-library book for any file.
8. **No library roots** (judge risk 2). `IPluginEnvironment.LibraryPaths` is the script search path, not the comic
   folders. Pruning stops at profile base folders, the failed-books folder, the keep list, and the drive root.
9. **Snapshot everything, no `Demand`.** The snapshot reads every registry field for the selection and for library books in
   the selection's series. `Organizer.Demand` and `FieldDemand` are dropped until a measured library says otherwise.
10. **Answers last for the ComicRack session**, held by the plugin `Session`. `AnswerStore`/`answers.xml` is deferred.
11. **The executor is synchronous** (from fable). It runs on a worker. The plugin's `ILibraryGateway` marshals each call to
    the UI thread with `Control.Invoke`. Tests call it directly.
12. **No swap cycles.** A target held by a file the plan moves away is ordered vacate-first. A cycle becomes a
    `TargetExists` conflict. Rare, and the user can pick Keep both.
13. **Simulate is the review grid.** There is no Simulate mode. Every Organize run shows the plan, and closing the grid is
    a simulation. The importer maps 2.x `Simulate` to `Move` and warns. A Quick command executes without the grid when the
    plan has no questions, conflicts or failures.
14. **Commands**: `OrganizeCommand` (`IBooks` + `IConfig`, key `library-organizer-net`), `OrganizeQuickCommand`
    (`IBooks`), `HistoryCommand` (`ILibrary`: past runs and undo). The startup run is deferred.

Rejected from the losers: sonnet's folder-break marker inside strings (a value could forge it), haiku's raw-argument nodes
and `Execute` accepting an unapproved plan, fable's `required` properties on `BookData` (a second field list).

## Tradeoffs accepted

- We accept re-planning the whole selection after each answer (pure, about 1 to 5 microseconds per render, so tens of milliseconds for 10k books) in exchange for one planning function with deterministic counters. Per-book memoization can come later behind the same signature.
- We accept dropping per-profile Simulate in exchange for a review step that every run gets and no fake disk.
- We accept that `RecycleFile` (Replace) cannot be undone, because the Recycle Bin has no reliable restore API. Undo reports it as "not reversible" instead of guessing.
- We accept snapshotting the selection on the UI thread before planning starts, a short pause on a large selection, in exchange for an engine that never touches a live `ComicBook`.
- We accept two profile types (`ProfileSettings` text, `Profile` compiled) in exchange for an engine that never sees an invalid template or rule.
- We accept persisted answers (a new file and a "remembered choices" page) in exchange for not asking the same series question every run.
- We accept that a 2.x template the new parser rejects (two tokens in one group, padding on a text field, a group with no field) becomes an import warning with a position, where 2.x left the text in the output.

## Alternatives considered

- **The engine asks through a callback (`IAnswerSource.Ask(question)`).** The shape 2.x has and the easiest port. It keeps a UI call inside rendering, so the preview and the startup run need fake askers, and the planner cannot list all questions or show them as data. Lost on interface depth: the caller must supply a behavior, not data.
- **An `IBook` interface with one property per field, implemented over a live `ComicBook`.** It is a second field list that must stay in sync with the registry, and it reads a live UI object from a worker thread. The `BookRecord` snapshot hides the same data behind one indexer.
- **Keep the 2.x multi-pass regex substitution for exact compatibility.** It keeps the reparse bug (a value with `<` becomes syntax), gives no error positions, and needs the loop guard. The tree with the same tokenizer rules reproduces every documented 2.x output except the defects.
- **Separate `Planner`, `ConflictResolver`, and `Simulator` services.** Temporal decomposition: three modules that each re-derive targets and disk occupancy. `Plan.With*` keeps that knowledge in one module.

## Implementation reconciliation

Accepted deviations, with the unit that made them. Each was reviewed by the orchestrator before merge.

**U2 (fields, values, series).** `Field.CanBeRequired` became `Uses.HasFlag(FieldUses.Required)`. `TemplateName` is null for
rules-only fields. `AbsPath.IsRoot` added; `TryParse` upper-cases the drive letter and resolves `.`/`..`. An issue with no
leading number never becomes `Last` unless the series has no numbered issue. Dates render raw as invariant `yyyy-MM-dd`.
`FieldReader` falls back to the book itself when the index has no entry for its series.

**U3 (templates).** `Piece` has a third case, `Placeholder`, so preview markers survive cleaning. A multi-value AsFolders
answer puts a folder break between items instead of the separator; in a file name it joins with the separator. A preset
with AskWhenMore is used silently when the book has no extra items. Series-scope questions ignore presets (as 2.x).
Cleaning is one left-to-right pass, longest key first, then strips any remaining Windows-forbidden character. Reserved
names get `_` before the first dot. `<month2>` is a padded month number. Padding on a Decimal field and widths over 50
are parse errors.

**U4/U6 (rules, settings, import).** Negated operators (`IsNot`, `DoesNotContain`, `DoesNotContainItem`) match an unset
value; positive comparisons never do. `ProfileId`, `ProfileAction`, `CoverFormat`, `ReviewWhen` live in `Profiles.cs`.
`RunPreferences.DefaultResolution` is a `ConflictDefault` enum (Ask, Skip, Replace, KeepBoth) that the planner maps to
`Resolution`. A newer or unreadable schema is moved aside like a corrupt file. Imported 2.x profiles keep their own
template text; only a brand-new profile gets `PathDefaults`.

**U7 (planner).** `Plan.Operations` is public and holds the operations planned so far in execution order. `OpId` values
start at 1 and follow that order. `EntryOutcome.WaitingForAnswer` carries every question key the entry needs.
`ProfileTally` has a `Blocked` count; `Overridden` counts as skipped and `PlannedAsFailed` as failed. `PreviewResult.Filter`
is never null. `FolderPruning` and `PruneFolders` have no `DeleteJunkFiles`, because no setting feeds it. `Operation` has
no `IsSatisfied` or `Inverse` yet; they come with the executor.
Planning order: a book is planned as a unit, and a book whose target is held by another selected book's file plans that
book first. So `SameTargetAs.Earlier` means earlier in planning order, which is selection order except for such a chain.
A blocked entry keeps its claim on its target. A book's copies are emitted before its move. A cycle is a `TargetExists`
conflict that does not allow Replace, because the file belongs to a book still being planned. An explicit resolution the
conflict does not allow falls through to the default. A typed path that leads back to a path already refused is ignored.
A copy whose target is its own source is `AlreadyInPlace`. A book selected twice is planned once.
The planner asks `IFileProbe` whether a book's file exists; `BookLocation.File.Exists` is read only by `Preview`, which
has no disk. A counter ordinal is taken by a render that produces a path or waits for an answer, so numbers do not move
when an answer or a resolution arrives; a failed render, a filtered book and an overridden profile take none.
`RecycleFile.TransferReadProgressTo` is set only for a Move profile. `PruneFolders` is added only when a pruning profile
moves a file; its candidates are the source folders of those moves, and the executor decides which are empty.
Under `MoveUnder`, a profile that keeps the current folder puts the file directly in the failed folder. A fileless book
needs a profile that renders both folder and name. `Compile` does not read a template or base folder the Use flags
switch off, ignores an empty-substitute key it does not know, and sets `SettingsError.Where` to the `ProfileSettings`
property name.

**U8 (executor).** `Executor.Run` and `Executor.Resume(RunId)` are synchronous. `Resume` on a finished run changes
nothing and returns what the journal recorded. A cancelled run is finished, so it is not resumed. An exception from the
gateway or the journal ends the run and leaves the journal interrupted. `Run` and `Resume` call `JournalStore.Trim` at
the end.
Which operations are already done is decided once per run, latest operation first, on a `DiskOverlay` that takes each
done operation back. Asking each operation in turn against the real disk is wrong after a move into a vacated path or a
Replace: the recycle would look undone and take the file that replaced it. A move also counts as done when its target is
there and the journal has its `file` mark, although something new is at the source. Undo turns that state into the
occupied-path conflict. The only other place a mark decides is a fill behind a recycle (see Review fixes).
`Operation.IsSatisfied` for a move has no size check, because the operation carries no size. A copy compares sizes while
the source can still be read. So a file that appears at a target after the source has vanished is taken for the moved
file. `Operation.Inverse(OpId, BookId? createdBook)` takes the one created book, not the map.
`RenameCase` renames only the file name, through `<name>.lo-rename` in the same folder. A folder whose spelling differs
keeps it on the disk; the library path gets the planned spelling. A `WriteCover` whose book has no cover fails.
Prune walks up from each candidate, past folders that are already gone, and fails as a whole after trying every
candidate when the system refuses one.
The journal file is `runs\yyyyMMdd-HHmmssfff-<runId>.journal`, UTC. Its `<run>` line carries `schema="1"` and the
number of entry and operation lines. `<entry>` lines come before the `<op>` lines and hold the plan entries, so a
resumed run reports like a clean one and undo has captions and profile names. A journal whose plan part is not there in
full is not a run: `List` omits it and `Trim` deletes it. A line counts only once its line end is written. A mark that
cannot be read is skipped. `RunSummary` has `Cancelled`. `RunJournal` has `Entries` and `Marks` (`OpMark`: file,
applied, created book, failure) in place of results and created books.
`ILibraryGateway.AddCopy` must return the existing book when the library already has one at the path, so a repeated call
adds nothing. With an unknown source it adds the copy with no metadata.
`PlanUndo` gives one entry per entry of the run that had operations. `PlannedAsFailed` means the book moves back and a
file the run recycled stays in the Recycle Bin; `Skipped` means the disk shows nothing to undo. Its conflicts have no
library book (`TargetExists(null)`). When the run pruned, the undo ends with a prune
of the folders its moves leave, with the run's stop and keep lists. `RunReport.Tally` counts an entry as planned when
all its operations succeeded, failed when one failed, and skipped when a cancel left one not run.

**Review fixes.** An independent review of Core found five defects. Each has a test that failed before its fix.
- *Undo moved another book's file.* Run 1 moves book 1 to T. Book 1 is renamed away, and run 2 moves book 2 to T. Undo
  of run 1 found "source gone, target there" and moved book 2's file to book 1's old path. A file at a path is not
  proof that it is the file the run put there. The evidence considered: later journals, the library index, and a file
  size recorded at plan time. Later journals see only plugin runs, `Trim` deletes them, and an undo of a later run
  makes the sequence hard to read. A size needs a new journal attribute, so a schema bump. The library index sees every
  move the library knows of, and it is one lookup. So `Organizer.PlanUndo` takes `libraryFiles`, an `ILookup` from path to every library book
  at that path (`LibrarySnapshot.PathOwners`). ComicRack can list two books at one path when one of them lost its file,
  so a path-to-one-book index made undo depend on library order; a second review proved both a missed case and a false
  refusal with that index. When any book other than the operation's own is listed at a move's or case rename's
  target, the undo leaves that file, even if the other entry is stale: refusing is safe, moving the wrong file is not. The entry is `Skipped` with the reason. Copies and covers keep the existing checks: a copy
  compares sizes while its source can be read. Two gaps remain. A file at the target that no library book holds is
  still taken for the run's file. `Resume` still takes "source gone, target there" as done, because the executor has
  no index; there the worst case is a library path that names the wrong file, and no file moves.
- *Replace onto an existing file was skipped.* A plan that recycles T and then writes T (a Replace onto a cover, or a
  copy onto a file of the same size) found the old file at T. The write looked done. The overlay then took it back,
  so the recycle looked done too. Both were reported `AlreadyDone`, and the old file stayed. An undo of such a run that
  never got that far would recycle the old file. `DiskOverlay` now takes the run's operations and marks. An operation
  that fills a path that an earlier recycle of the run frees is never done while that recycle has no `file` mark. The
  executor runs in order and never overwrites, so the operation cannot have run. `Holds` lost its `fileMarked`
  parameter, because the overlay reads the marks itself. The executor and the undo planner both use the rule. If the
  recycle failed and the path became free some other way before the write ran, a resume reports the write as failed
  and an undo leaves its file. That fails safe.
- *A case rename cut short could not be undone.* A kill between the two steps of a `RenameCase` leaves the file at
  `<name>.lo-rename`. `Resume` already finished it, because the executor moves from the temporary name when it
  exists. Undo saw no file at either spelling and skipped it. `RenameCase.Temporary` now owns the name. Undo emits the
  inverse rename when the temporary file exists. The inverse's temporary name differs only in case, so it is the same
  file, and the executor's existing step moves it to the original spelling.
- *Import dropped whitespace-only values.* `ImportFile` loaded without `LoadOptions.PreserveWhitespace`, so a
  replacement or empty substitute of one space came back empty. `Load` and `ImportFile` now read through one method.

## Open questions and risks

- Should text rule operators be case-insensitive by default? This design says yes (a breaking change). The importer flags each 2.x rule whose result could differ.
- Should Replace move the old file into a per-run holding folder on the same volume instead of the Recycle Bin, so undo can restore it? That makes undo complete but leaves files the user must purge.
- Does ComicRack CE's process opt into long paths? If it does, should the length check become a warning instead of a conflict?
- Should persisted per-issue answers expire when the book's field value changes? This design invalidates an answer when its selected values are no longer all present on the book.
- How large is the slowest real library selection to snapshot on the UI thread? This is a guess (about 1 second for 100k books with the demand-driven field set), not measured.

## Sketch files and build check

The sketch is in `sketch/`. `Fields.cs`, `Books.cs`, `Templates.cs`, `Rules.cs`, `Profiles.cs`, `Planning.cs` and `Execution.cs` are the Core library. `Plugin.cs` is the plugin shell. `_build/Core.csproj` (net48, C# 14, nullable, warnings as errors, PolySharp) and `_build/plugin/Plugin.csproj` (adds the ComicRack CE `Release/net48` DLLs) compile the sketch. Both built clean during this session, so the signatures type-check on net48 against the real `DotNetPlugin` API. They are measured facts, not a claim about behavior: every body throws.

## Next implementation step

Write the field registry and `Template.Parse` with the test vectors from `07-rebuild-guide.md` as the first TUnit tests, because every other module reads both.
