using System.Text.Json;

namespace EmpireModManager;

public sealed class ModCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Paths { get; set; } = [];
}

public sealed class Organization
{
    public const string Uncategorized = "uncategorized";
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "organization.json");
    readonly string file;
    public List<ModCategory> Categories { get; private set; }
    public Organization(string file)
    {
        this.file = file;
        Categories = File.Exists(file)
            ? JsonSerializer.Deserialize<List<ModCategory>>(File.ReadAllText(file), Store.JsonOptions)
                ?? throw new InvalidDataException("Invalid category file.")
            : [new() { Name = "Main Mods" }, new() { Name = "Submods" }, new() { Name = "Utilities" }, new() { Id = Uncategorized, Name = "Uncategorized" }];
        if (Categories.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || c.Paths is null)
            || Categories.Select(c => c.Id).Distinct().Count() != Categories.Count)
            throw new InvalidDataException("Invalid categories. Restore data/organization.json.bak.");
        if (!Categories.Any(c => c.Id == Uncategorized)) Categories.Add(new() { Id = Uncategorized, Name = "Uncategorized" });
    }
    void Change(Action<List<ModCategory>> action)
    {
        var copy = Categories.Select(c => new ModCategory { Id = c.Id, Name = c.Name, Paths = [.. c.Paths] }).ToList();
        action(copy);
        Store.WriteJson(copy, file);
        Categories = copy;
    }
    public void Rename(string? id, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Enter a category name.");
        if (Categories.Any(c => c.Id != id && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That category name already exists.");
        Change(groups => { if (id is null) groups.Insert(groups.FindIndex(c => c.Id == Uncategorized), new() { Name = name }); else groups.Single(c => c.Id == id).Name = name; });
    }
    public string CategoryOf(string path) => Categories.FirstOrDefault(c => c.Paths.Contains(path, StringComparer.OrdinalIgnoreCase))?.Id ?? Uncategorized;
    public List<Mod> OrderedMods(string id, IEnumerable<Mod> mods)
    {
        var paths = Categories.Single(c => c.Id == id).Paths;
        return mods.Where(m => CategoryOf(m.Path) == id).OrderBy(m =>
        { var index = paths.FindIndex(p => p.Equals(m.Path, StringComparison.OrdinalIgnoreCase)); return index < 0 ? int.MaxValue : index; })
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public void Move(IEnumerable<string> paths, string destination, string? anchor = null, bool after = false)
    {
        var moving = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (anchor is not null && moving.Contains(anchor, StringComparer.OrdinalIgnoreCase)) return;
        Change(groups =>
        {
            var target = groups.Single(c => c.Id == destination);
            foreach (var group in groups) group.Paths.RemoveAll(p => moving.Contains(p, StringComparer.OrdinalIgnoreCase));
            var index = anchor is null ? -1 : target.Paths.FindIndex(p => p.Equals(anchor, StringComparison.OrdinalIgnoreCase));
            target.Paths.InsertRange(index < 0 ? target.Paths.Count : index + (after ? 1 : 0), moving);
        });
    }
    // Persist currently visible discovery order before moving relative to previously unassigned mods.
    public void Remember(IEnumerable<Mod> mods)
    {
        foreach (var mod in mods)
            if (!Categories.Any(c => c.Paths.Contains(mod.Path, StringComparer.OrdinalIgnoreCase)))
                Categories.Single(c => c.Id == Uncategorized).Paths.Add(mod.Path);
    }
    public void Delete(string id)
    {
        if (id == Uncategorized) throw new InvalidOperationException("Uncategorized is the fallback category and cannot be deleted.");
        Change(groups => { var group = groups.Single(c => c.Id == id); groups.Single(c => c.Id == Uncategorized).Paths.AddRange(group.Paths); groups.Remove(group); });
    }
    public void MoveCategory(string source, string target)
    {
        if (source == target) return;
        Change(groups => { var group = groups.Single(c => c.Id == source); groups.Remove(group); groups.Insert(groups.FindIndex(c => c.Id == target), group); });
    }
    public static int ReorderPreset(Preset preset, Mod mod, int slot)
    {
        var source = preset.Mods.IndexOf(mod);
        if (source < 0) return -1;
        slot = Math.Clamp(slot, 0, preset.Mods.Count);
        preset.Mods.RemoveAt(source);
        if (source < slot) slot--;
        preset.Mods.Insert(slot, mod);
        return slot;
    }
}
