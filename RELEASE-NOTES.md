# Empire Mod Manager 1.2.4

Release build — October 6, 2026.

Portable Windows x64 package with the .NET runtime included. Extract the entire ZIP into a writable folder and open `EmpireModManager.exe`. Keep all extracted files together. No installer or separate .NET installation is needed.

## Changes in 1.2.4

- Keeps a complete load-order row readable when the expanded launch command is shown on a small desktop, including the 1024-pixel Windows runner display.
- Checks compact layouts with both configured and missing game folders in the UI smoke test, including visible launch errors.
- Continues game-folder discovery when Steam's registry entry cannot be read.
- Prints the underlying application exception when release checks fail and retains diagnostics in the tag-release workflow.
- Updates GitHub artifact uploads to the Node.js 24 action runtime.
- Includes a verified source ZIP and checksum alongside application release assets.

## Changes in 1.2.3

- Removes the distracting column separators from the blank area below the mod library.
- Adds ascending and descending sorting by clicking any library column heading, with an arrow showing the active direction.
- Sorts sizes, dates, Workshop IDs, and version numbers by value, keeping unavailable values last and mods within their categories.
- Keeps sorting active during searches and rescans, preserves selected mods and collapsed categories, and refreshes sorting as file details finish loading.
- Adds **Columns → Use saved category order** to restore manual order; dragging a mod before or after another mod also restores manual ordering. Preset load order is unaffected.

## Changes in 1.2.2

- Fixes update checks for releases that provide GitHub's SHA-256 asset digest without a separate checksum file, including the published 1.2.1 release.
- Verifies downloads against the GitHub digest and rejects conflicts when a separate checksum file is also supplied.
- Reports missing release verification files as a publishing problem instead of suggesting a retry will fix it.

## Changes in 1.2.1

- Adds slim, rounded separators between library column headings to make resize targets easier to find.
- Highlights the active separator in gold with a subtle glow while hovering or dragging.
- Keeps native column resizing, double-click autosizing, reordering, and hidden-column behavior.

## Changes in 1.2.0

- Checks this project's GitHub releases in the background on launch and offers Update and restart for newer stable builds.
- Adds an Updates dialog with manual checking, release notes, download progress, and an option to disable launch checks.
- Verifies the ZIP checksum, individual file hashes, and executable version before replacing application files. Presets and other files in data are preserved, and pending preset edits are saved before restarting.
- Backs up replaced files and restores them when installation fails. Offline checks and incomplete downloads leave the current installation usable.
- Adds a GitHub release workflow that publishes the verified Windows ZIP and checksum for version tags.

## Changes in 1.1.1

- Removed white hover popups from mod rows and the mod detail card.
- Suppressed Windows' automatic hover tips for truncated library labels as well as custom mod tooltip text. Selected mod details remain in the detail card and library columns.

## Included features

- Redesigned navy and gold interface with the application icon in the header, library counters, source badges, and preset and load-order cards.
- Larger category arrows with wide single-click targets. Category hover popups are removed; category names still support dragging and double-clicking.
- Category arrows work while searching, after reordering columns, and while horizontally scrolling. Space toggles the selected category.
- Expandable launch command with validation errors kept visible, plus a selected mod detail card.
- New preset additions appear above existing mods. Adding several together preserves their order within the group and skips duplicates.
- Removed redundant library counts from the bottom status area.

- Discover installed local and Steam Workshop mods for Empire at War and Forces of Corruption.
- Create, save, duplicate, and launch ordered mod presets.
- Organize the library with categories and drag-and-drop.
- Customize listing names, versions, types, and visible columns.
- Retrieve and cache Workshop titles and update dates.
- Remember window placement and library preferences.

## Getting started

1. Extract to a folder you can write to, such as a folder under Documents. Do not run from inside the ZIP.
2. Open `EmpireModManager.exe` and check **Folders** for your game and Workshop locations.
3. Create a preset, choose the game edition, and add installed mods. New additions go at the top; arrange the final order as specified by the mod authors.
4. Choose **Save & launch preset**. Keep Steam running for the Steam edition.

The game and mods are not included. Settings and presets are created in `data` beside the executable. Back up that folder before moving or replacing an existing installation. From version 1.2.0 onward, use **Updates** to install newer published builds automatically. To update an older version manually, extract into a new folder and copy your old `data` folder into it while the app is closed. To uninstall, remove the extracted app folder; back up `data` first if you want to keep your presets.

## Known limitations

The app manages installed mods; Steam handles subscriptions and updates. It does not merge mods, resolve dependencies, or isolate game saves. Mod compatibility and load order depend on the mod authors' instructions. Workshop information needs an internet connection; cached information and local discovery work offline.

This release is unsigned. Automated checks exercise application behavior and window layout, but do not establish that every mod combination works in game. See `README.md` for details. Bundled runtime notices are in `third-party-notices`.

## Previous release

Version 1.0.0 was the initial release on September 29, 2026.
Version 1.1.0 introduced the redesigned interface and preset additions at the top on October 3, 2026.
