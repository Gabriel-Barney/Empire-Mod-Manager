using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmpireModManager;

public sealed class Mod
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Source { get; set; } = "Local";
    public string Game { get; set; } = "FoC";
    public string WorkshopId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ModType { get; set; } = "";
    public long? SizeBytes { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public DateTimeOffset? WorkshopUpdatedUtc { get; set; }
    public bool WorkshopDetailsLoaded { get; set; }
    public bool FileDetailsLoaded { get; set; }
    public string Summary { get; set; } = "";
    public bool Installed => Directory.Exists(System.IO.Path.Combine(Path, "Data"));
    public override string ToString() => $"{(Installed ? "" : "[Missing] ")}{Name}   ·   {Source}";
}

public sealed class Preset
{
    public string Name { get; set; } = "New preset";
    public string Game { get; set; } = "FoC";
    public List<Mod> Mods { get; set; } = [];
    public int AddModsToTop(IEnumerable<Mod> mods)
    {
        var known = Mods.Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = mods.Where(m => known.Add(m.Path)).ToList();
        Mods.InsertRange(0, additions);
        return additions.Count;
    }
    public override string ToString() => Name;
}

public sealed class Settings
{
    public string GameRoot { get; set; } = "";
    public List<string> WorkshopRoots { get; set; } = [];
    public static Settings Detect()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(steam))
        {
            libraries.Add(steam);
            var vdf = System.IO.Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        // Also find conventional libraries when the Steam registry entry is unavailable.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var candidate = System.IO.Path.Combine(drive.RootDirectory.FullName, "SteamLibrary");
            if (Directory.Exists(candidate)) libraries.Add(candidate);
        }
        var settings = new Settings();
        foreach (var library in libraries)
        {
            var game = System.IO.Path.Combine(library, "steamapps", "common", "Star Wars Empire at War");
            if (Directory.Exists(game) && settings.GameRoot == "") settings.GameRoot = game;
            var workshop = System.IO.Path.Combine(library, "steamapps", "workshop", "content", "32470");
            if (Directory.Exists(workshop)) settings.WorkshopRoots.Add(workshop);
        }
        return settings;
    }
}

public sealed class AppState
{
    public Settings Settings { get; set; } = Settings.Detect();
    public List<Preset> Presets { get; set; } = [new() { Name = "My Forces of Corruption preset" }];
    public int SelectedPreset { get; set; }
}

public static class Store
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string StatePath => System.IO.Path.Combine(AppContext.BaseDirectory, "data", "presets.json");
    public static AppState Load(string path)
    {
        if (!File.Exists(path)) return new();
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The preset file is empty.");
        if (state.Settings is null || state.Settings.WorkshopRoots is null || state.Presets is null ||
            state.Presets.Any(p => p is null || p.Mods is null || p.Mods.Any(m => m is null)))
            throw new InvalidDataException("The preset file contains invalid data. Restore data/presets.json.bak.");
        return state;
    }
    public static void Save(AppState state, string path)
        => WriteJson(state, path);

    public static void WriteJson<T>(T value, string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed record ListingDetails(string Name, string Version, string ModType = "");

public sealed class ListingOverrides
{
    readonly string file;
    Dictionary<string, ListingDetails> entries;
    public static string DefaultPath => System.IO.Path.Combine(AppContext.BaseDirectory, "data", "listings.json");

    public ListingOverrides(string file)
    {
        this.file = file;
        var saved = File.Exists(file)
            ? JsonSerializer.Deserialize<Dictionary<string, ListingDetails>>(File.ReadAllText(file), Store.JsonOptions)
                ?? throw new InvalidDataException("The custom listings file is empty.")
            : new Dictionary<string, ListingDetails>();
        if (saved.Any(pair => pair.Value is null || string.IsNullOrWhiteSpace(pair.Value.Name) || pair.Value.Version is null))
            throw new InvalidDataException("The custom listings file contains invalid data. Restore data/listings.json.bak.");
        entries = new(saved, StringComparer.OrdinalIgnoreCase);
    }

    public void Set(Mod mod, ListingDetails? details)
    {
        if (details is not null && string.IsNullOrWhiteSpace(details.Name))
            throw new InvalidOperationException("Enter a display name.");
        var updated = new Dictionary<string, ListingDetails>(entries, StringComparer.OrdinalIgnoreCase);
        var key = System.IO.Path.GetFullPath(mod.Path);
        if (details is null) updated.Remove(key);
        else updated[key] = new(details.Name.Trim(), details.Version.Trim(), (details.ModType ?? "").Trim());
        // Persist before changing the live catalog; failed saves leave existing labels intact.
        Store.WriteJson(updated, file);
        entries = updated;
    }

    public void Apply(IEnumerable<Mod> mods)
    {
        foreach (var mod in mods)
            if (entries.TryGetValue(System.IO.Path.GetFullPath(mod.Path), out var custom))
            { mod.Name = custom.Name; mod.Version = custom.Version; mod.ModType = custom.ModType ?? ""; }
    }

    public void Refresh(List<Mod> library, IEnumerable<Preset> presets)
    {
        Apply(library);
        var installed = library.ToDictionary(m => System.IO.Path.GetFullPath(m.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            foreach (var mod in preset.Mods)
                if (installed.TryGetValue(System.IO.Path.GetFullPath(mod.Path), out var current))
                { mod.Name = current.Name; mod.Version = current.Version; mod.ModType = current.ModType; }
            Apply(preset.Mods);
        }
        library.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
    }
}

public sealed record ScanResult(List<Mod> Mods, List<string> Warnings);

public static class Library
{
    public static ScanResult Scan(Settings settings)
    {
        var mods = new List<Mod>();
        var warnings = new List<string>();
        var roots = new List<(string Path, string Source, string Game)>();
        if (!string.IsNullOrWhiteSpace(settings.GameRoot))
        {
            roots.Add((System.IO.Path.Combine(settings.GameRoot, "corruption", "Mods"), "Local", "FoC"));
            roots.Add((System.IO.Path.Combine(settings.GameRoot, "GameData", "Mods"), "Local", "EaW"));
        }
        roots.AddRange(settings.WorkshopRoots.Select(p => (p, "Workshop", "FoC")));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path))
            {
                if (root.Source == "Workshop") warnings.Add($"Folder unavailable: {root.Path}");
                continue;
            }
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(root.Path))
                {
                    var path = System.IO.Path.GetFullPath(folder);
                    if (!seen.Add(path)) continue;
                    var id = System.IO.Path.GetFileName(path);
                    if (root.Source == "Workshop" && !Regex.IsMatch(id, "^[0-9]+$")) continue;
                    if (!Directory.Exists(System.IO.Path.Combine(path, "Data")))
                    {
                        warnings.Add($"Skipped {id}: no Data folder (unpacked mods must have Data directly inside their folder).");
                        continue;
                    }
                    var mod = new Mod { Name = root.Source == "Workshop" ? $"Workshop {id}" : id,
                        Path = path, Source = root.Source, Game = root.Game, WorkshopId = root.Source == "Workshop" ? id : "" };
                    var metadata = System.IO.Path.Combine(path, "modinfo.json");
                    if (File.Exists(metadata))
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(File.ReadAllText(metadata), new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                            var json = document.RootElement;
                            mod.Name = Read(json, "name") ?? mod.Name;
                            mod.Version = Read(json, "version") ?? "";
                            mod.Summary = Read(json, "summary") ?? "";
                            if (json.TryGetProperty("steamdata", out var data) && data.ValueKind == JsonValueKind.Object)
                            {
                                if (mod.Name == $"Workshop {id}") mod.Name = Read(data, "title") ?? mod.Name;
                            }
                        }
                        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
                        { warnings.Add($"Could not read metadata for {id}: {ex.Message}"); }
                    }
                    mods.Add(mod);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add($"Could not scan {root.Path}: {ex.Message}"); }
        }
        return new(mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(), warnings);
    }
    static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
}

public sealed record LaunchPlan(string Executable, string WorkingDirectory, List<string> Arguments)
{
    public string Preview => $"\"{Executable}\" " + string.Join(" ", Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
    public ProcessStartInfo StartInfo()
    {
        var info = new ProcessStartInfo(Executable) { WorkingDirectory = WorkingDirectory, UseShellExecute = false };
        foreach (var arg in Arguments) info.ArgumentList.Add(arg);
        return info;
    }
}

public static class Launcher
{
    public static LaunchPlan Build(Settings settings, Preset preset)
    {
        if (string.IsNullOrWhiteSpace(settings.GameRoot)) throw new InvalidOperationException("Choose your game installation in Folders first.");
        if (preset.Game is not ("FoC" or "EaW")) throw new InvalidOperationException("Unknown game edition.");
        var working = System.IO.Path.GetFullPath(System.IO.Path.Combine(settings.GameRoot, preset.Game == "FoC" ? "corruption" : "GameData"));
        var executable = System.IO.Path.Combine(working, "StarWarsG.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException($"Game executable not found: {executable}");
        var args = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in preset.Mods)
        {
            if (!mod.Installed) throw new InvalidOperationException($"Missing mod: {mod.Name}\n{mod.Path}\nRestore the mod or remove it from this preset.");
            if (mod.Game != preset.Game) throw new InvalidOperationException($"{mod.Name} belongs to {mod.Game}, but this preset targets {preset.Game}.");
            if (!seen.Add(System.IO.Path.GetFullPath(mod.Path))) throw new InvalidOperationException($"Duplicate mod: {mod.Name}");
            if (mod.Source == "Workshop")
            {
                if (!Regex.IsMatch(mod.WorkshopId, "^[0-9]+$")) throw new InvalidOperationException("Invalid Workshop ID.");
                args.Add("STEAMMOD=" + mod.WorkshopId);
            }
            else args.Add("MODPATH=" + System.IO.Path.GetRelativePath(working, mod.Path));
        }
        return new(executable, working, args);
    }
}
