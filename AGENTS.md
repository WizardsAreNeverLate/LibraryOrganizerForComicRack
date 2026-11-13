# AGENTS.md

How to build, test and change this repository.

## What this is

A .NET Framework 4.8 plugin for ComicRack CE, built on the `DotNetPlugin` API. It moves and renames comic files into
folders built from templates and rules. It is modeled on the Python Library Organizer 2.1.13, whose design notes are at
`d:\Data\_Code\comicrack-library-organizer\docs\`. Compatibility with the Python plugin is not required.

The design is `docs/design/architecture.md`. Its **Synthesis decision** section overrides the sketch in
`docs/design/sketch/` wherever the two disagree.

## Build and test

```powershell
./Build.ps1 -ComicRackDir D:\Data\_Code\ComicRackCE\ComicRack\bin\Release\net48
./Build.ps1 -ComicRackDir ... -SkipTests
```

- `src/LibraryOrganizer.Core` has no ComicRack or WinForms reference. `dotnet build src/LibraryOrganizer.Core` works
  without `ComicRackDir`, and so do `tests/LibraryOrganizer.Tests` (they reference Core only).
- Run one suite: `tests/LibraryOrganizer.Tests/bin/Debug/net48/LibraryOrganizer.Tests.exe --progress off`
  (add `--treenode-filter "/*/*/ClassName/*"` to narrow it).
- `tests/LibraryOrganizer.LoadTests` unzips the built `LibraryOrganizer.crplugin` and loads it through ComicRack's own
  `PluginEngine`. It must not reference the plugin assembly: ComicRack skips a DLL whose identity is already loaded.
- Never run a Python interpreter.

## Conventions

- Tabs, file-scoped namespaces, `Nullable` on, `ImplicitUsings` off, warnings are errors.
- Core is pure: no UI, no ComicRack types. Disk access goes through `IFileProbe`/`IFileSystem`, library writes through
  `ILibraryGateway`. Business logic has no static mutable state.
- Comments only for a non-obvious *why*. No phase-narrating comments in tests.
- Tests use TUnit and assert behavior a caller can see: rendered paths, plan entries, files on disk, journal contents.
  Derive expected values from the requirement (the Python docs or the design), not from a run of the code.
- Code-only WinForms. No `.resx`.

## Formats that must not change once shipped

`settings.xml` (root `schema` attribute), the run journal line format, and `Field.Key` values. Change one only with a
schema bump and a migration.
