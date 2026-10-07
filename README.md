# Empire Mod Manager

**Organize your mods. Save your setups. Launch your next campaign.**

Empire Mod Manager is a portable Windows application for **Star Wars: Empire at War Gold Pack**, supporting both **Empire at War** and **Forces of Corruption**. It brings installed local and Steam Workshop mods into one library, lets you save ordered mod combinations as named presets, and launches the game with your selected setup.

## Why this project exists

Switching between a main mod, its submods, and compatibility patches can mean keeping track of Workshop IDs and editing launch arguments by hand. Empire Mod Manager gives those setups a name and a place to save them, so you can return to a favorite campaign setup without rebuilding its launch command each time.

For example, you can keep one preset for a main mod and its supported submods, another for a different total conversion, and an empty preset for the unmodded game. Each preset remembers its game edition and mod order.

## Project goals

- **Make switching mods easier.** Reduce repeated setup with reusable presets and clear launch controls.
- **Bring installed mods together.** Show local and Workshop content in one searchable, organized library.
- **Make launch order visible.** Let players inspect and arrange their selected mods according to the mod authors' instructions.
- **Keep players in control.** Allow custom names, categories, and listing details while keeping installed mod files untouched.
- **Stay portable.** Keep settings beside the application so they are easy to back up and carry between app installations.
- **Make problems understandable.** Report missing mods and scan issues, and show the command used to launch a preset.

These goals guide the project. The features below describe what the current version implements.

## Features

- **Mod discovery:** Find installed local and Workshop mods, with configurable game and Workshop folders.
- **Saved presets:** Create, duplicate, rename, and save ordered mod combinations for either game edition.
- **Launch controls:** Preview and copy launch arguments, launch a selected preset, or start the unmodded game with an empty preset.
- **Library organization:** Create categories, search listings, and use drag-and-drop to organize mods and preset order.
- **Custom listings:** Edit display names, versions, and mod types without changing installed mod files.
- **Workshop details:** Retrieve titles and update dates from Steam, with cached information available offline.
- **Flexible interface:** Reorder or hide columns, resize panels, and retain window preferences between sessions.
- **Modern desktop design:** A navy and gold theme with an orbital header, live library counters, source badges, preset cards, and numbered load-order cards. Controls and typography scale with the display.
- **Portable storage:** Keep settings and presets in a `data` folder beside the executable, with backups of previous saves for supported settings files.

## Current scope

The manager works with **already installed mods**. Steam handles Workshop subscriptions and updates. Mod compatibility and launch order still depend on each mod author's instructions; saving mods in a preset does not make them compatible.

Automatic dependency resolution, merging mod files, and separate savegame or game-settings profiles are outside the current version's scope.

## Requirements

- A Windows x64 PC for the packaged release.
- An installed copy of Star Wars: Empire at War and the relevant expansion for the presets you want to use.
- Installed mods for modded presets; the game and mods are not included.
- Steam running when launching the Steam edition.
- Internet access for Workshop metadata refreshes. Local discovery and cached metadata work offline.

The release ZIP includes the .NET runtime, so no separate .NET installation is required. Keep the app in a writable folder so it can save settings.

## Quick start

For the release package, extract the entire `EmpireModManager-1.2.4-win-x64.zip` into a writable folder and open `EmpireModManager.exe`. The Windows x64 release includes the .NET runtime. Keep all extracted files together. See [release notes](RELEASE-NOTES.md) for setup and update instructions.

### Program updates

The manager checks [this project's GitHub releases](https://github.com/Gabriel-Barney/Empire-Mod-Manager/releases) in the background when it starts. A newer stable release offers **Update and restart**. You can also open **Updates** in the header, check manually, or turn off launch checks. Offline or rate-limited checks do not prevent using the manager. Drafts, prereleases, older versions, and ordinary commits are not installed.

Updates download the Windows ZIP and verify its SHA-256 checksum using the matching checksum file or, from version 1.2.2 onward, GitHub's asset digest. When both are supplied, they must agree. Updates verify all packaged files, save pending preset edits, close the app, replace application files, and restart. The `data` folder, including presets, library organization, cached names, and preferences, is preserved. Installation failures restore replaced files; backups remain in the temporary update folder if an error needs recovery. Keep the program in a writable folder and close other running copies before updating. Version 1.2.0 is the first build with this updater; older versions need one manual update to enable it.

### Publishing updates

Run `./package-release.ps1` after increasing the project version. Publish a GitHub release with a matching tag such as `v1.2.4` or `1.2.4` and attach both `EmpireModManager-1.2.4-win-x64.zip` and `EmpireModManager-1.2.4-win-x64.zip.sha256`. Keep publishing the checksum file so existing 1.2.0 and 1.2.1 installations can update. The ZIP must come from the packager, which includes `update-manifest.json` with hashes of application files. The updater accepts assets only from this repository and rejects unverifiable or invalid packages.

The `Publish Windows release` workflow builds, tests, and uploads the Windows and source packages with their checksums when a matching version tag is pushed. The existing CI workflow still builds test artifacts for ordinary pushes. Enable GitHub Actions in the repository to use the release workflow. Releasing a new version requires increasing `Version`, `AssemblyVersion`, and `FileVersion` in the project and updating the release notes before tagging.

To verify the updater locally, run `./test-updater.ps1 -Archive releases/EmpireModManager-1.2.4-win-x64.zip`. It uses an isolated test installation to check waiting for app exit, replacing files, restarting, keeping a backup, and preserving saved data. Both GitHub workflows run this check after packaging.

Developer builds in `dist` require the .NET 9 Windows Desktop Runtime.

1. Use **Folders** to check the detected game installation and Workshop locations.
2. Create a preset and choose **Forces of Corruption** or **Empire at War**.
3. Search the library and double-click mods (or select several and use **Add to preset**). New additions go above the mods already in the preset; adding several together preserves their order within that group.
4. Arrange the list with **Up / Down**. Follow the author's launch instructions, typically submods first and the main mod last.
5. **Save preset**, or **Save & launch preset**. Keep Steam running for the Steam edition.

An empty preset launches the unmodded game. You can duplicate, rename, or delete presets. Deleting a preset does not delete mods. Changes are kept in memory until you save or launch; closing with changes offers to save.

## Detailed user guide

<details>
<summary>Library layout, categories, custom listings, discovery, storage, and launch behavior</summary>

### Library layout and preferences

The header shows installed mod, saved preset, and Workshop counts. Select a library entry to inspect its details in the card and library columns. Library rows and the detail card have no hover popups. Use **Command** beside **Copy command** to expand or collapse the launch preview. A launch error automatically opens the preview and remains visible until resolved. **Command ready** means the launch command passed the manager's validation; mod compatibility still depends on the authors' instructions.

Drag the vertical divider between **Mod Library** and **Preset Configuration** to adjust their widths. An amber guide previews the new position; the panels resize when you release the mouse to avoid repaint trails during dragging.

The library also shows **Workshop ID**, **Last updated**, **Size**, and **Mod type**. Workshop dates use Steam's latest published update timestamp, cached for up to a day and available offline after a successful lookup. Missing Workshop dates show Unavailable; local mods use the newest installed file modification date. Dates display in local time. Size totals installed file bytes, excluding symbolic links/junctions; inaccessible folders show Unavailable. These details scan in the background on opening or rescanning. Use the horizontal scrollbar when the panel is narrow. Right-click **Edit listing** to choose Main mod, Submod, Compatibility patch, Utility, or enter a custom type. Types are saved with custom names and versions; Reset to detected clears the custom type.

Use **Columns** beside Mod Library to toggle individual fields, show all columns, or hide all optional columns. Drag the vertical grip at the right edge of a heading to resize its column; the grip highlights in gold when hovered or dragged. Double-click a divider to fit the column automatically. Drag column headings left or right to arrange them in any order. Mod names remain visible for identification and dragging. Column order and visibility are saved in data/columns.json independently of presets; hiding a column preserves its position for when you show it again.

Click a column name to sort the mods within each category in ascending order; click it again for descending order. An arrow marks the active column and direction. Sizes, dates, Workshop IDs, and version numbers sort by value, with unavailable values last. Sorting stays active during searches and rescans for the current session. Choose **Columns → Use saved category order** to restore your manual order. Dragging a mod before or after another mod also returns to manual order. Sorting the library does not change preset load order.

The manager remembers its window size, position, and maximized state when closed, in data/window.json. Restored windows are kept within the available monitor. Visible columns return to readable, DPI-scaled widths when the window or library panel is resized; wider manually chosen widths are retained during the session. Narrow windows use horizontal scrolling instead of squeezing every field. Hidden columns stay hidden.

### Drag-and-drop and categories

Drag a mod in the **preset configuration** list above or below another entry to change launch order. An amber line marks the insertion position; dragging near the list edges scrolls. Use **Save preset** or launch to save the new order. The Up/Down buttons remain available.

In the **mod library**, drag mods onto a category heading to move them there, or above/below another mod to position them within that category. You can select multiple mod rows before dragging. Drag a category's name onto another heading to place it before that category. Click the large arrow once to collapse or expand a category; double-clicking its name or pressing Space on a selected heading also works. Category headings have no hover popups. Search initially expands matching categories, and their arrows still work while searching; hidden mods retain their positions.

Use **+ Category** to create categories. Right-click a heading to rename or delete it, or right-click a mod and choose **Move to category**. Deleting a category moves its mods to **Uncategorized** without deleting files. Uncategorized is the permanent fallback category and can be renamed. Initial categories are Main Mods, Submods, Utilities, and Uncategorized; new mods begin in Uncategorized.

Library organization saves automatically in `data/organization.json` with a previous-save `.bak`. It survives scans, Workshop name updates, and restarts. Categories are organizational only: moving library entries never changes a preset's launch order.

### Editing listings

Right-click a mod row and choose **Edit listing…**, or use the **Edit listing** button. Right-clicking selects the clicked row; empty space does not open the menu. The menu is also available with the keyboard menu key or Shift+F10 when one row is selected.

Select a single mod in the library and click **Edit listing** to change its display name and version. Version labels can contain text or be left blank. **Save listing** saves immediately, updates the library and existing presets, and keeps your labels through rescans and restarts. Search also matches custom versions. **Reset to detected** restores the name and version from the installed mod's metadata (or its folder/Workshop ID fallback).

Custom listings are stored separately in `data/listings.json`, with a previous-save `.bak` file. They are matched to the mod's installation path. Renaming or moving a mod folder requires editing its listing again. Editing a listing does not change mod files, Workshop IDs, load order, or launch arguments, and does not save other pending preset changes.

### Automatic Workshop names

At startup and on **Rescan**, the manager looks up Workshop IDs through [Steam's public GetPublishedFileDetails API](https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage#GetPublishedFileDetails). No API key is required. Lookup runs in the background; the window remains usable. Names are cached in `data/workshop-names.json` for offline use and refreshed when older than one day. Failed lookups are retried on a later scan after five minutes. Private, deleted, or unavailable entries keep their cached or local fallback names. Select a mod to see its title in the detail card, or widen the Mod column for long titles. IDs appear in the Workshop ID column.

Custom names always take priority over Steam titles. Resetting a custom listing restores the cached Steam title when available, otherwise local metadata. Version numbers still come from installed metadata or your custom version field; a Workshop page title is not proof of the installed version. Only Workshop IDs are sent to Steam. Presets and local paths are not uploaded.

### Discovery and storage

- Reads Steam's registry location and `libraryfolders.vdf`, plus conventional drive-root SteamLibrary folders.
- Local mods: `<game>/corruption/Mods` for FoC and `<game>/GameData/Mods` for EaW.
- Workshop mods: each configured `steamapps/workshop/content/32470` folder. Workshop entries default to FoC.
- Each installed mod must have a `Data` folder directly inside its directory. Archives and incomplete/nested installations are skipped and reported in **Scan details**.
- Reads titles and versions from `modinfo.json`, then applies cached/online Workshop names and your custom listings. Local folder names and Workshop IDs serve as fallbacks; **Workshop page** opens the corresponding page. Folder scanning works offline.
- Presets and folder settings are saved beside the executable in `data/presets.json`. Each save keeps the preceding version in `presets.json.bak`. Keep the app in a writable folder. A corrupt preset file is reported and never silently replaced.
- Missing mods remain in presets and block launch until restored or removed.

### Launch behavior and limits

The manager starts `corruption/StarWarsG.exe` or `GameData/StarWarsG.exe`, with the game folder as its working directory. Local entries produce a `MODPATH=` argument; Workshop entries produce `STEAMMOD=<id>`. Arguments are passed individually to the process, preserving paths containing spaces and the preset order. The launch preview can be copied for inspection. Global Steam launch options are not edited.

The mod author's instructions determine compatibility and load order. A preset does not merge mod files, automatically resolve dependencies, or isolate savegames/game settings. Mixing unrelated total conversions is generally unsupported. This version manages already installed mods; subscribing and updating remain Steam's responsibility. Workshop launch resolution is handled by the game/Steam.

Launch syntax reference: [The Art of War mod author's Workshop instructions](https://steamcommunity.com/workshop/filedetails/?id=3643661236) demonstrate an ordered submod/base pair. [Local mod installation instructions](https://www.moddb.com/tutorials/installing-and-launching-mods-on-eaw-moddb-and-steam) document `StarWarsG MODPATH=...`.

</details>

## Development

Built with **C#**, **.NET 9**, and **Windows Forms**. Building from source requires Windows and the .NET 9 SDK.

For a clean, shareable source ZIP, run `./package-source.ps1`. It packages source code, assets, documentation, and GitHub workflows, verifies the extracted files, and writes the versioned ZIP and SHA-256 checksum to `releases`. See [the source publishing guide](PUBLISHING.md) for building an extracted copy and publishing source or application releases.

### Build and checks

To build the self-contained release with the .NET 9 SDK, run `./package-release.ps1` in PowerShell. It downloads the official runtime packages when needed, publishes to a fresh staging folder, includes runtime license notices, creates a ZIP, verifies its extracted contents, and runs self-tests and UI smoke checks on the extracted copy. The ZIP and SHA-256 checksum are written to `releases`. Personal settings and test output are excluded. UI smoke checks require an interactive Windows desktop. Intermediate files and check results remain under `releases/build-*` for inspection; distribute only the final ZIP and checksum.

```powershell
dotnet build -c Release
dotnet publish -c Release --no-restore -o dist
& .\dist\EmpireModManager.exe --self-test
& .\dist\EmpireModManager.exe --scan
& .\dist\EmpireModManager.exe --smoke-test
```

The self-test exercises scanning, malformed metadata, preset persistence/backup, argument ordering/spacing, and missing/wrong-edition mod validation using temporary fixtures. It never starts the game. `--scan` writes `scan-report.json` beside the executable using auto-detected folders. `--smoke-test` renders the main window to `ui-preview.png` and exits. In PowerShell, use `Start-Process -Wait -PassThru` when an exit code is needed for this GUI executable.

The game itself must be launched to verify a particular mod combination in play; a valid command alone does not prove that the game loaded every mod.

## Feedback and contributions

Bug reports, usability feedback, and feature suggestions are welcome through this repository's GitHub Issues. For a bug report, include the app version, game edition, steps to reproduce the issue, and any relevant error message or scan details. For launch problems, include the mod names and their preset order.

Ideas that support easier setup, clearer mod organization, and reliable preset handling fit the project's goals.
