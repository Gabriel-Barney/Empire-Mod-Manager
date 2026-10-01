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

For the release package, extract the entire `EmpireModManager-1.0.0-win-x64.zip` into a writable folder and open `EmpireModManager.exe`. The Windows x64 release includes the .NET runtime. Keep all extracted files together. See [release notes](RELEASE-NOTES.md) for setup and update instructions.

Developer builds in `dist` require the .NET 9 Windows Desktop Runtime.

1. Use **Folders** to check the detected game installation and Workshop locations.
2. Create a preset and choose **Forces of Corruption** or **Empire at War**.
3. Search the library and double-click mods (or select several and use **Add to preset**).
4. Arrange the list with **Up / Down**. Follow the author's launch instructions, typically submods first and the main mod last.
5. **Save preset**, or **Save & launch preset**. Keep Steam running for the Steam edition.

An empty preset launches the unmodded game. You can duplicate, rename, or delete presets. Deleting a preset does not delete mods. Changes are kept in memory until you save or launch; closing with changes offers to save.

## Detailed user guide

<details>
<summary>Library layout, categories, custom listings, discovery, storage, and launch behavior</summary>

### Library layout and preferences

Drag the vertical divider between **Mod Library** and **Preset Configuration** to adjust their widths. An amber guide previews the new position; the panels resize when you release the mouse to avoid repaint trails during dragging.

The library also shows **Workshop ID**, **Last updated**, **Size**, and **Mod type**. Workshop dates use Steam's latest published update timestamp, cached for up to a day and available offline after a successful lookup. Missing Workshop dates show Unavailable; local mods use the newest installed file modification date. Dates display in local time. Size totals installed file bytes, excluding symbolic links/junctions; inaccessible folders show Unavailable. These details scan in the background on opening or rescanning. Use the horizontal scrollbar when the panel is narrow. Right-click **Edit listing** to choose Main mod, Submod, Compatibility patch, Utility, or enter a custom type. Types are saved with custom names and versions; Reset to detected clears the custom type.

Use **Columns** beside Mod Library to toggle individual fields, show all columns, or hide all optional columns. Drag column headings left or right to arrange them in any order. Mod names remain visible for identification and dragging. Column order and visibility are saved in data/columns.json independently of presets; hiding a column preserves its position for when you show it again.

The manager remembers its window size, position, and maximized state when closed, in data/window.json. Restored windows are kept within the available monitor. Visible columns return to readable, DPI-scaled widths when the window or library panel is resized; wider manually chosen widths are retained during the session. Narrow windows use horizontal scrolling instead of squeezing every field. Hidden columns stay hidden.

### Drag-and-drop and categories

Drag a mod in the **preset configuration** list above or below another entry to change launch order. An amber line marks the insertion position; dragging near the list edges scrolls. Use **Save preset** or launch to save the new order. The Up/Down buttons remain available.

In the **mod library**, drag mods onto a category heading to move them there, or above/below another mod to position them within that category. You can select multiple mod rows before dragging. Drag a category heading onto another heading to place it before that category. Double-click headings to collapse/expand them. Search expands matching categories temporarily; hidden mods retain their positions.

Use **+ Category** to create categories. Right-click a heading to rename or delete it, or right-click a mod and choose **Move to category**. Deleting a category moves its mods to **Uncategorized** without deleting files. Uncategorized is the permanent fallback category and can be renamed. Initial categories are Main Mods, Submods, Utilities, and Uncategorized; new mods begin in Uncategorized.

Library organization saves automatically in `data/organization.json` with a previous-save `.bak`. It survives scans, Workshop name updates, and restarts. Categories are organizational only: moving library entries never changes a preset's launch order.

### Editing listings

Right-click a mod row and choose **Edit listing…**, or use the **Edit listing** button. Right-clicking selects the clicked row; empty space does not open the menu. The menu is also available with the keyboard menu key or Shift+F10 when one row is selected.

Select a single mod in the library and click **Edit listing** to change its display name and version. Version labels can contain text or be left blank. **Save listing** saves immediately, updates the library and existing presets, and keeps your labels through rescans and restarts. Search also matches custom versions. **Reset to detected** restores the name and version from the installed mod's metadata (or its folder/Workshop ID fallback).

Custom listings are stored separately in `data/listings.json`, with a previous-save `.bak` file. They are matched to the mod's installation path. Renaming or moving a mod folder requires editing its listing again. Editing a listing does not change mod files, Workshop IDs, load order, or launch arguments, and does not save other pending preset changes.

### Automatic Workshop names

At startup and on **Rescan**, the manager looks up Workshop IDs through [Steam's public GetPublishedFileDetails API](https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage#GetPublishedFileDetails). No API key is required. Lookup runs in the background; the window remains usable. Names are cached in `data/workshop-names.json` for offline use and refreshed when older than one day. Failed lookups are retried on a later scan after five minutes. Private, deleted, or unavailable entries keep their cached or local fallback names. Hover over a row to see its full title and ID.

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
