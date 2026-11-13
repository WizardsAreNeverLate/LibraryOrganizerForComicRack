# Framing

Goal: a ComicRack CE plugin built on the .NET DotNetPlugin API that moves and renames comics into folders built from
templates and rules, modeled on Library Organizer 2.1.13 (Python). Compatibility with the Python plugin is optional.

## Definition of done (falsifiable)

1. `./Build.ps1` (COMICRACK_DIR = ComicRackCE integration Release build) builds with warnings-as-errors and emits
   `LibraryOrganizer.crplugin` containing the plugin DLL(s) and Package.ini.
2. The test suite passes, and it includes:
   - every template test vector from docs/07-rebuild-guide.md of the Python docs,
   - rules (all operators, nested groups, Only / Do not, typed numbers and unset values),
   - planner (profile order, last move wins, copy profiles, failed/required fields, already-in-place, case-only rename, two books one target),
   - executor + journal + undo on a real temp folder (move, copy, conflict resolutions, empty-folder removal stops at library root, undo after a partial run),
   - settings round-trip, and import of a 2.x `losettingsx.dat` sample.
3. ComicRack's own `DotNetPluginLoader`/initializer discovers the built DLL and registers its hooks (test against the real ComicRack.Plugins.dll).
4. In-app: ComicRack started with the plugin installed shows the commands, and a Simulate run over sample books shows a plan. (Best effort; record VERIFIED / INCONCLUSIVE.)

## Scope

About 12 units. Core engine roughly 3k lines, UI roughly 2k, tests roughly 1.5k. Multi-hour run.
Blockers found in grounding: none. ComicRack Release build present (2026-11-13). `dotnet build` cannot compile binary .resx
for net48, so UI is code-only WinForms (no .resx) and the build uses VS MSBuild like ComicApiPlugin.

## Rigor: high

One-way doors: the settings file format, the template grammar, the journal format. These get the architect pass and
round-trip tests. The file executor touches user files: it gets real-filesystem tests, a journal, and Simulate as the
default mode for a new profile. UI is reversible and gets lighter checks.

## Deliberate deviations from 2.x (approved by the user as breaking changes)

- New settings file (typed XML with schema version); 2.x `losettingsx.dat` / `.lop` are imported, not shared.
- Template syntax kept, parsed into a tree. Tests compare raw values; cleaning happens once per value.
- Plan -> review -> execute. Simulate is the plan preview. Conflicts are resolved in the preview, before any file moves.
- Journal-based undo, idempotent replay.
- Unset is a value, not -1. Numeric comparisons parse decimals. Excluded folders compare normalized folder prefixes.
