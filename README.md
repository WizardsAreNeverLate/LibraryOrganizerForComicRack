# Library Organizer for ComicRack CE

Library Organizer moves and renames your comic files into folders built from their metadata. You write a folder template
and a file name template, choose which books each profile handles with rules, and run one or more profiles over the
selected books. Before any file moves, Library Organizer shows you the full plan, and you can undo a run afterwards.

It is a .NET plugin for [ComicRack Community Edition](https://github.com/maforget/ComicRackCE), built on the `DotNetPlugin`
API. It is modeled on the Python plugin Library Organizer 2.1.13, keeps its template syntax, and imports its settings.

## Requirements

- ComicRack CE with the .NET plugin API (the `DotNetPlugin` class in `ComicRack.Plugins.dll`).
- Windows with .NET Framework 4.8.

## Install the plugin

1. Download `LibraryOrganizer.crplugin`, or build it from source (see below).
2. Open the file with ComicRack, or drag it onto the ComicRack window. ComicRack opens its Preferences on the
   **Scripts** page to install the package.
3. Restart ComicRack.

ComicRack installs the package into the `Library Organizer .NET` folder under its Scripts folder. The Python plugin uses
the `Library Organizer` folder, so both plugins can be installed at the same time.

The plugin keeps its settings and run history in `%APPDATA%\LibraryOrganizer.NET`. Reinstalling the plugin does not
touch that folder.

## Organize books

1. Select books in the library.
2. Right-click and choose **Automation** > **Library Organizer**.
3. If you have more than one profile, choose which profiles run and in what order. For each book, the last Move profile
   that accepts it decides where the book goes. Every Copy profile that accepts it makes its own copy.
4. Review the plan. Each row is one book under one profile, with its outcome and its old and new path.
   - If a field such as Writer has several values, answer the question at the top of the window. The answer lasts for
     the rest of the ComicRack session.
   - If a target path is taken, choose **Skip**, **Replace** (the old file goes to the Recycle Bin), **Keep both**
     (adds ` (1)` to the name), or type another path. **For all conflicts** sets one answer for every conflict.
5. Click **Run**. To change nothing, click **Close**. Closing the review is the old Simulate mode.

**Library Organizer (Quick)** skips the review when no book needs an answer, no target is taken, and no book failed.

The first time you open the plugin, it looks for Library Organizer 2.x settings in ComicRack's Scripts folder and
offers to import them.

## Change the settings

In ComicRack's Preferences, on the **Scripts** page, select a Library Organizer command and click **Configure...**.
The dialog has one tab per area:

- **General**: Move or Copy, the base folder, which parts of the path to build, the folder for books with empty required
  fields, and covers for fileless books.
- **Templates**: the folder and file name templates. A field picker builds tokens for you. Errors show under each
  template as you type, and a preview line shows the result for a book from your library.
- **Rules**: which books the profile handles, as a tree of Any and All groups, and folders to leave alone.
- **Options**: illegal-character replacements, month names, empty-value substitutes, required fields, and removal of
  folders a run leaves empty.

**Import** reads `.xml` exports and Library Organizer 2.x `.dat` and `.lop` files. **Export** writes the selected or all
profiles.

## Undo a run

1. Choose **Automation** > **Library Organizer: History and Undo** from the File menu.
2. Select a run and click **Undo...**.
3. Review the plan and click **Run**.

Undo works from what is on disk now. It moves each file back, removes copies the run made, and removes the folders the
run left empty. A file that a run replaced went to the Recycle Bin, and undo does not restore it.

If ComicRack closed in the middle of a run, the next Library Organizer run offers to finish it. **Finish** in the history window does
the same.

## Write templates

The template syntax is the 2.x syntax. A template is plain text plus groups in braces. A group holds one field in angle
brackets and optional text around it. The text appears only when the field has a value:

- `{<series>}` writes the series. `{ #<number>}` writes ` #7`, or nothing when the book has no number.
- `{<number3>}` pads to three digits (`007`). `{<number0>}` pads to the width of the last issue in the series.
- `{ ({<month>, }<year>)}` nests groups: ` (March, 1992)`, ` (1992)` with no month, and nothing with no year.
- `{ (Annual)<?format(Annual)>}` writes ` (Annual)` when the format is `Annual`. `{No Number<!number>}` writes
  `No Number` when the number is empty. A test argument that starts with `!` is a regular expression.
- `{<writer(, )(issue)>}` asks which writers to use for each issue. With `(series)` it asks once per series.
- A backslash (`\`) in the folder template starts a new folder level.

The field picker on the **Templates** tab lists every field and builds these tokens for you. The default templates are:

```
Folder: {<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}
File:   {<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}
```

For Marvel's X-Men volume 2 issue 7 of 12, from March 1992, in a series that started in 1991, these give
`Marvel\X-Men (1991)\X-Men Vol.2 #07 (of 12) (March, 1992).cbz`.

## Differences from Library Organizer 2.x

These changes are deliberate. Most fix defects listed in the 2.x documentation.

- Every run shows its plan first. There is no Simulate mode. The importer turns Simulate profiles into Move profiles.
- Conflicts are resolved in the review, before any file moves, instead of in dialogs after the other files moved.
- Undo works for any past run, not only the last, and finishes or reverses a run that a crash interrupted. It no longer
  depends on profile names.
- `?` and `!` tests in templates compare the raw value, so `<?format(Trade: Paperback)>` matches.
- A field value can never add a folder level or be read as template syntax. Only `\` in the folder template, and the
  "one folder per item" answer, create folder levels.
- Dates with no format are written as `yyyy-MM-dd`, not in the system's date format.
- The counter numbers only the books a profile renders, so skipped books do not use up numbers.
- Rules compare numbers as numbers, ignore letter case in text, treat an empty value as empty (so `Year less than 2000`
  no longer matches books with no year), and offer every field, including creators and start year.
- Excluded folders match whole folders, ignoring case. `D:\Manga` no longer excludes `D:\Manga Extra`.
- Required fields are checked on the book, not only when a template uses them.
- The first and last issue of a series use natural issue order (`9` before `10`, `12.1` after `12`), and are worked out
  fresh for each run.
- Settings use a new file format. 2.x and 1.7 settings are imported, not shared.

## Build from source

You need the .NET SDK 10 or later and a ComicRack CE build that has the `DotNetPlugin` API.

```powershell
./Build.ps1 -ComicRackDir <ComicRack build folder>
```

`Build.ps1` builds the solution, runs the test suites, and writes
`src\LibraryOrganizer\bin\Debug\net48\LibraryOrganizer.crplugin`. `tools\smoke\run-smoke.ps1` runs the plugin inside a
real ComicRack with a separate configuration. `AGENTS.md` describes the project layout and conventions, and
`docs\design\architecture.md` describes the design.
