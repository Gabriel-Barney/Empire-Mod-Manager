namespace EmpireModManager;

public static class SelfTests
{
    public static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "EmpireModManager-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var results = new List<string>();
        try
        {
            void Check(bool condition, string name)
            { if (!condition) throw new Exception("FAILED: " + name); results.Add("PASS: " + name); }
            void Reject(Action action, string name)
            {
                try { action(); }
                catch (InvalidOperationException) { results.Add("PASS: " + name); return; }
                throw new Exception("FAILED: " + name);
            }
            var game = Path.Combine(folder, "Game with spaces");
            var local = Path.Combine(game, "corruption", "Mods", "Submod with spaces");
            var workshop = Path.Combine(folder, "workshop", "content", "32470");
            var main = Path.Combine(workshop, "123456");
            Directory.CreateDirectory(Path.Combine(local, "Data"));
            Directory.CreateDirectory(Path.Combine(main, "Data"));
            Directory.CreateDirectory(Path.Combine(game, "GameData", "Mods", "Classic mod", "Data"));
            Directory.CreateDirectory(Path.Combine(workshop, "999999", "Data"));
            File.WriteAllText(Path.Combine(workshop, "999999", "modinfo.json"), "broken json");
            Directory.CreateDirectory(Path.Combine(workshop, "888888"));
            File.WriteAllText(Path.Combine(game, "corruption", "StarWarsG.exe"), "test fixture");
            File.WriteAllText(Path.Combine(game, "GameData", "StarWarsG.exe"), "test fixture");
            File.WriteAllText(Path.Combine(main, "modinfo.json"), "{\"name\":\"Main mod\",\"version\":\"2.0\"}");
            var settings = new Settings { GameRoot = game, WorkshopRoots = [workshop, workshop] };
            var scan = Library.Scan(settings);
            Check(scan.Mods.Count == 4, "Scan both games and Workshop; deduplicate roots and skip incomplete mods");
            Check(scan.Warnings.Any(w => w.Contains("metadata")), "Malformed metadata is reported without breaking scan");
            var mainMod = scan.Mods.Single(m => m.WorkshopId == "123456");
            var subMod = scan.Mods.Single(m => m.Path == local);
            var detailFile = Path.Combine(local, "Data", "sample.bin");
            File.WriteAllBytes(detailFile, new byte[2048]);
            var detailDate = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(detailFile, detailDate);
            var details = ModFileDetails.Read(local);
            Check(details.SizeBytes == 2048 && details.UpdatedUtc == detailDate, "Installed size and date include nested mod files");
            Check(ModFileDetails.SizeText(details.SizeBytes) == "2 KiB" && ModFileDetails.SizeText(0) == "0 B", "Format installed file sizes including empty mods");
            Check(ModFileDetails.Read(Path.Combine(folder, "missing")).SizeBytes is null, "Missing file details are unavailable rather than zero");
            File.Delete(detailFile);
            Check(mainMod.Name == "Main mod" && mainMod.Version == "2.0", "Read mod metadata");
            var preset = new Preset { Name = "Test fleet", Mods = [subMod, mainMod] };
            var plan = Launcher.Build(settings, preset);
            Check(plan.Arguments.SequenceEqual([@"MODPATH=Mods\Submod with spaces", "STEAMMOD=123456"]), "Preserve local submod / Workshop base order");
            Check(plan.StartInfo().ArgumentList.Count == 2 && plan.StartInfo().ArgumentList[0].Contains("with spaces"), "Paths with spaces remain single process arguments");
            Check(plan.WorkingDirectory == Path.Combine(game, "corruption"), "Set FoC working directory");
            Check(Launcher.Build(settings, new()).Arguments.Count == 0, "Empty preset launches vanilla");
            preset.Game = "EaW";
            Reject(() => Launcher.Build(settings, preset), "Reject cross-edition preset");
            preset.Game = "FoC";
            preset.Mods.Add(subMod);
            Reject(() => Launcher.Build(settings, preset), "Reject duplicate mods");
            preset.Mods.RemoveAt(2);
            var state = new AppState { Settings = settings, Presets = [preset] };
            var path = Path.Combine(folder, "state", "presets.json");
            Store.Save(state, path);
            preset.Name = "Renamed";
            Store.Save(state, path);
            var loaded = Store.Load(path);
            Check(loaded.Presets[0].Name == "Renamed" && loaded.Presets[0].Mods[0].Path == local, "Persist preset name, paths, and order");
            Check(Store.Load(path + ".bak").Presets[0].Name == "Test fleet", "Retain previous save as backup");
            var steamResponse = """
                {"response":{"publishedfiledetails":[
                  {"publishedfileid":"123456","result":1,"consumer_app_id":32470,"title":"Steam fleet title"},
                  {"publishedfileid":"999999","result":9},
                  {"publishedfileid":"888888","result":1,"consumer_app_id":999,"title":"Wrong game"},
                  {"publishedfileid":"777777","result":1,"consumer_app_id":32470,"title":"Unrequested"}
                ]}}
                """;
            var fetched = WorkshopNames.Parse(steamResponse, ["123456", "999999", "888888"]);
            var datedResponse = steamResponse.Replace("\"title\":\"Steam fleet title\"", "\"title\":\"Steam fleet title\",\"time_updated\":1767355200");
            var release = WorkshopNames.ParseDetails(datedResponse, ["123456"])["123456"];
            Check(release.ReleasedAt == DateTimeOffset.FromUnixTimeSeconds(1767355200) && release.ReleaseChecked, "Read Workshop release timestamp from Steam response");
            Check(WorkshopNames.ParseDetails(steamResponse, ["123456"])["123456"].ReleasedAt is null, "Missing release timestamp stays unknown");
            Check(WorkshopNames.ParseDetails(datedResponse.Replace("1767355200", "-1"), ["123456"])["123456"].ReleasedAt is null, "Invalid release timestamp stays unknown");
            var releasePath = Path.Combine(folder, "state", "release-cache.json");
            Store.WriteJson(new Dictionary<string, WorkshopName> { ["123456"] = release }, releasePath);
            var releaseMod = new Mod { Source = "Workshop", WorkshopId = "123456", LastUpdatedUtc = DateTime.UtcNow, FileDetailsLoaded = true };
            new WorkshopNames(releasePath).Apply([releaseMod]);
            Check(releaseMod.WorkshopUpdatedUtc == release.ReleasedAt && MainForm.UpdatedText(releaseMod) == release.ReleasedAt!.Value.ToLocalTime().ToString("yyyy-MM-dd"), "Cached Workshop release date overrides installed file timestamp");
            releaseMod.WorkshopUpdatedUtc = null;
            Check(MainForm.UpdatedText(releaseMod) == "Unavailable", "Unknown Workshop date does not fall back to misleading local date");
            var columnPath = Path.Combine(folder, "state", "columns.json");
            var windowPath = Path.Combine(folder, "state", "window.json");
            var placement = new WindowPreferences { X = 5000, Y = 3000, Width = 1200, Height = 800, Dpi = 96, Maximized = true };
            Store.WriteJson(placement, windowPath);
            var restoredWindow = WindowPreferences.Load(windowPath)!;
            Check(restoredWindow.Width == 1200 && restoredWindow.Height == 800 && restoredWindow.Maximized, "Persist window size and maximized state");
            var workArea = new Rectangle(0, 0, 1920, 1080);
            Check(workArea.Contains(restoredWindow.Fit(workArea, new Size(1000, 700), 144)), "Restore off-screen windows inside the available monitor at changed DPI");
            placement.X = 30; placement.Y = 40;
            Check(placement.Fit(workArea, new Size(1000, 700), 96) == new Rectangle(30, 40, 1200, 800), "Restore exact normal window bounds at unchanged DPI");
            Store.WriteJson(new ColumnVisibility { Hidden = ["Size", "Version"] }, columnPath);
            Check(ColumnVisibility.Load(columnPath).Hidden.SetEquals(["Size", "Version"]), "Column visibility survives restart");
            var columnPreferences = ColumnVisibility.Load(columnPath);
            Check(columnPreferences.ResolveOrder(["Mod", "Version", "Size"]).SequenceEqual(["Mod", "Version", "Size"]), "Existing column settings keep default order");
            columnPreferences.Order = ["Size", "Mod", "Version"];
            Store.WriteJson(columnPreferences, columnPath);
            var restoredColumns = ColumnVisibility.Load(columnPath);
            Check(restoredColumns.ResolveOrder(["Mod", "Version", "Size"]).SequenceEqual(["Size", "Mod", "Version"]) && restoredColumns.Hidden.Contains("Size"), "Column order survives restart alongside visibility settings");
            restoredColumns.Order = ["Size", "Removed", "Size"];
            Check(restoredColumns.ResolveOrder(["Mod", "Version", "Size"]).SequenceEqual(["Size", "Mod", "Version"]), "Column order ignores obsolete duplicates and includes new columns");
            Check(fetched.Count == 1 && fetched["123456"] == "Steam fleet title", "Workshop response excludes unavailable, wrong-game, and unrequested entries");
            var cachePath = Path.Combine(folder, "state", "workshop-names.json");
            Store.WriteJson(new Dictionary<string, WorkshopName> { ["123456"] = new(fetched["123456"], DateTimeOffset.UtcNow) }, cachePath);
            var cachedNames = new WorkshopNames(cachePath);
            var cachedMods = Library.Scan(settings).Mods;
            cachedNames.Apply(cachedMods);
            Check(cachedMods.Single(m => m.WorkshopId == "123456").Name == "Steam fleet title"
                && cachedMods.Single(m => m.WorkshopId == "123456").Version == "2.0", "Cached Workshop titles load offline without changing installed version");
            File.WriteAllText(cachePath, "broken cache");
            var recoveredNames = new WorkshopNames(cachePath);
            var fallbackMods = Library.Scan(settings).Mods;
            recoveredNames.Apply(fallbackMods);
            Check(fallbackMods.Single(m => m.WorkshopId == "123456").Name == "Main mod", "Corrupt optional cache falls back to local metadata");
            var listingPath = Path.Combine(folder, "state", "listings.json");
            var labels = new ListingOverrides(listingPath);
            labels.Set(mainMod, new("My custom fleet", "3.5 beta", "Main mod"));
            labels.Set(subMod, new("Local custom ships", ""));
            var reopenedLabels = new ListingOverrides(listingPath);
            var rescanned = Library.Scan(settings).Mods;
            reopenedLabels.Refresh(rescanned, loaded.Presets);
            Check(rescanned.Single(m => m.WorkshopId == "123456").ModType == "Main mod" && loaded.Presets[0].Mods[1].ModType == "Main mod", "Mod type survives restart/rescan and updates presets");
            var legacyPath = Path.Combine(folder, "state", "legacy-listings.json");
            Store.WriteJson(new Dictionary<string, object> { [mainMod.Path] = new { Name = "Legacy name", Version = "1" } }, legacyPath);
            var legacyMod = new Mod { Path = mainMod.Path };
            new ListingOverrides(legacyPath).Apply([legacyMod]);
            Check(legacyMod.Name == "Legacy name" && legacyMod.ModType == "", "Existing listings without mod type remain compatible");
            Check(rescanned.Single(m => m.WorkshopId == "123456").Name == "My custom fleet"
                && loaded.Presets[0].Mods[1].Version == "3.5 beta", "Custom names and versions survive restart/rescan and update existing presets");
            Check(rescanned.Single(m => m.Path == local).Version == "", "Custom version may be blank");
            cachedNames.Apply(rescanned);
            reopenedLabels.Refresh(rescanned, loaded.Presets);
            Check(rescanned.Single(m => m.WorkshopId == "123456").Name == "My custom fleet"
                && loaded.Presets[0].Mods[1].Name == "My custom fleet", "Custom names take priority over Steam titles in the library and presets");
            Check(Launcher.Build(settings, loaded.Presets[0]).Arguments.SequenceEqual(plan.Arguments), "Editing labels preserves launch arguments and order");
            Check(File.ReadAllText(Path.Combine(main, "modinfo.json")).Contains("Main mod"), "Custom labels leave installed metadata unchanged");
            Reject(() => labels.Set(mainMod, new("  ", "1")), "Reject empty custom names");
            reopenedLabels.Set(mainMod, null);
            reopenedLabels.Refresh(Library.Scan(settings).Mods, loaded.Presets);
            Check(loaded.Presets[0].Mods[1].Name == "Main mod" && loaded.Presets[0].Mods[1].Version == "2.0", "Reset restores detected metadata in existing presets");
            var organizationPath = Path.Combine(folder, "state", "organization.json");
            var prependPreset = new Preset { Mods = [mainMod] };
            Check(prependPreset.AddModsToTop([subMod]) == 1
                && Launcher.Build(settings, prependPreset).Arguments.SequenceEqual(plan.Arguments),
                "New preset additions go above existing mods in the launch command");
            Check(prependPreset.AddModsToTop([mainMod, subMod, new Mod { Path = subMod.Path.ToUpperInvariant() }]) == 0
                && prependPreset.Mods.SequenceEqual([subMod, mainMod]),
                "Adding existing mods preserves their order and rejects duplicate paths regardless of case");
            var additionalMod = scan.Mods.Single(m => m.WorkshopId == "999999");
            var batchPreset = new Preset { Mods = [mainMod] };
            Check(batchPreset.AddModsToTop([subMod, additionalMod, subMod, mainMod]) == 2
                && batchPreset.Mods.SequenceEqual([subMod, additionalMod, mainMod]),
                "A batch of new mods keeps its selected order above existing mods without duplicates");
            var prependPath = Path.Combine(folder, "state", "prepend-preset.json");
            Store.Save(new AppState { Settings = settings, Presets = [batchPreset] }, prependPath);
            Check(Store.Load(prependPath).Presets[0].Mods.Select(m => m.Path).SequenceEqual(batchPreset.Mods.Select(m => m.Path)),
                "Prepended mod order survives saving and reopening the preset");
            var organization = new Organization(organizationPath);
            var allMods = Library.Scan(settings).Mods;
            organization.Remember(allMods);
            organization.Rename(null, "My collection");
            var collection = organization.Categories.Single(c => c.Name == "My collection").Id;
            organization.Move([main, local], collection);
            organization.Move([local], collection, main, false);
            Check(organization.OrderedMods(collection, allMods).Select(m => m.Path).SequenceEqual([local, main]), "Drag into categories and reorder before an anchor");
            organization.Move([local], collection, main, true);
            Check(organization.OrderedMods(collection, allMods).Select(m => m.Path).SequenceEqual([main, local]), "Drag downward places mod after anchor");
            organization.Move([main, local], collection, main);
            Check(organization.OrderedMods(collection, allMods).Count == 2, "Dropping selected mods onto themselves preserves membership");
            organization.Rename(collection, "Renamed collection");
            organization.MoveCategory(collection, organization.Categories[0].Id);
            var reopenedOrganization = new Organization(organizationPath);
            Check(reopenedOrganization.Categories[0].Name == "Renamed collection"
                && reopenedOrganization.OrderedMods(collection, allMods).Select(m => m.Path).SequenceEqual([main, local]), "Category names, category order, and mod order survive restart");
            Reject(() => organization.Rename(null, "Renamed collection"), "Reject duplicate category names");
            reopenedOrganization.Delete(collection);
            Check(reopenedOrganization.CategoryOf(main) == Organization.Uncategorized
                && Directory.Exists(main), "Deleting category returns mods to Uncategorized without deleting files");
            var draggedPreset = new Preset { Mods = [subMod, mainMod] };
            Check(Organization.ReorderPreset(draggedPreset, subMod, 2) == 1
                && Launcher.Build(settings, draggedPreset).Arguments[0] == "STEAMMOD=123456", "Dragging preset to end updates launch argument order");
            Check(Organization.ReorderPreset(draggedPreset, subMod, 0) == 0
                && Launcher.Build(settings, draggedPreset).Arguments.SequenceEqual(plan.Arguments), "Dragging preset to beginning restores launch order");
            Check(Organization.ReorderPreset(draggedPreset, subMod, 1) == 0 && draggedPreset.Mods.Count == 2, "Dropping beside original slot preserves preset without duplication");
            Directory.Delete(Path.Combine(local, "Data"));
            reopenedLabels.Refresh(Library.Scan(settings).Mods, loaded.Presets);
            Check(loaded.Presets[0].Mods[0].Name == "Local custom ships", "Missing mods retain custom labels");
            Reject(() => Launcher.Build(settings, loaded.Presets[0]), "Block launching a missing mod saved in a preset");
            Check(loaded.Presets[0].Mods.Count == 2, "Missing mod remains in saved preset");
            File.Delete(Path.Combine(game, "corruption", "StarWarsG.exe"));
            Reject(() => Launcher.Build(settings, new()), "Block missing executable");
            Check(Launcher.Build(settings, new() { Game = "EaW" }).WorkingDirectory.EndsWith("GameData"), "Select base game executable");
            using (var list = new BufferedModList { View = View.Details, Size = new Size(400, 300) })
            {
                list.Columns.Add("Mod", 350);
                for (var i = 0; i < 6; i++) list.Items.Add("Mod " + i);
                _ = list.Handle;
                var invalidations = new List<Rectangle>();
                list.Invalidated += (_, e) => invalidations.Add(e.InvalidRect);
                DragFeedback.Library(list, new(1, false));
                var initial = invalidations.Count;
                for (var i = 0; i < 1000; i++) DragFeedback.Library(list, new(1, false));
                Check(initial > 0 && invalidations.Count == initial, "Repeated drag events over the same library slot cause no additional repaint");
                invalidations.Clear();
                DragFeedback.Library(list, new(2, true));
                Check(invalidations.Count == 2 && invalidations.All(r => r.Height < list.ClientSize.Height / 2), "Changing library drop target repaints only old and new rows");
                invalidations.Clear();
                DragFeedback.Library(list, null);
                DragFeedback.Library(list, null);
                Check(invalidations.Count == 1 && list.Tag is null, "Clearing drag feedback repaints once and removes marker");
            }
            using (var list = new ListBox { Size = new Size(400, 300) })
            {
                list.Items.AddRange(["First", "Second", "Third"]);
                _ = list.Handle;
                var count = 0;
                list.Invalidated += (_, _) => count++;
                DragFeedback.Preset(list, 1);
                var initial = count;
                for (var i = 0; i < 1000; i++) DragFeedback.Preset(list, 1);
                Check(initial > 0 && count == initial, "Repeated preset drag events do not repaint unchanged insertion marker");
            }
            UpdateTests.Run(folder, results);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results);
            return 0;
        }
        finally
        {
            // Only remove this test's unique temporary directory.
            var full = Path.GetFullPath(folder);
            if (Path.GetDirectoryName(full) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) && Path.GetFileName(full).StartsWith("EmpireModManager-tests-"))
                Directory.Delete(full, true);
        }
    }
}
