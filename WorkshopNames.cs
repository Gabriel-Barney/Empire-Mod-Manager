using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmpireModManager;

public sealed record WorkshopName(string Title, DateTimeOffset CheckedAt, DateTimeOffset? ReleasedAt = null, bool ReleaseChecked = false);

public sealed class WorkshopNames
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "workshop-names.json");
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly string path;
    readonly SemaphoreSlim gate = new(1);
    readonly Dictionary<string, DateTimeOffset> attempted = [];
    Dictionary<string, WorkshopName> cache = [];
    public WorkshopNames(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path))
                cache = (JsonSerializer.Deserialize<Dictionary<string, WorkshopName>>(File.ReadAllText(path), Store.JsonOptions) ?? [])
                    .Where(p => ValidId(p.Key) && p.Value is not null && !string.IsNullOrWhiteSpace(p.Value.Title))
                    .ToDictionary(p => p.Key, p => p.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { /* Optional cache: a failed read must not prevent opening the manager. */ }
    }
    static bool ValidId(string id) => Regex.IsMatch(id, "^[0-9]+$");
    public void Apply(IEnumerable<Mod> mods)
    {
        foreach (var mod in mods)
            if (mod.Source == "Workshop" && cache.TryGetValue(mod.WorkshopId, out var entry))
            { mod.Name = entry.Title; mod.WorkshopUpdatedUtc = entry.ReleasedAt; mod.WorkshopDetailsLoaded = entry.ReleaseChecked; }
    }
    public static Dictionary<string, string> Parse(string json, IEnumerable<string> requested)
        => ParseDetails(json, requested).ToDictionary(p => p.Key, p => p.Value.Title);

    public static Dictionary<string, WorkshopName> ParseDetails(string json, IEnumerable<string> requested)
    {
        var allowed = requested.ToHashSet();
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, WorkshopName>();
        foreach (var item in document.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            if (!item.TryGetProperty("publishedfileid", out var id) || id.ValueKind != JsonValueKind.String || !allowed.Contains(id.GetString()!)) continue;
            if (!item.TryGetProperty("result", out var code) || !code.TryGetInt32(out var status) || status != 1) continue;
            if (!item.TryGetProperty("consumer_app_id", out var app) || !app.TryGetInt32(out var appId) || appId != 32470) continue;
            if (!item.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title.GetString())) continue;
            DateTimeOffset? released = null;
            if (item.TryGetProperty("time_updated", out var timestamp) && timestamp.ValueKind == JsonValueKind.Number &&
                timestamp.TryGetInt64(out var seconds) && seconds > 0 && seconds <= 253402300799)
                released = DateTimeOffset.FromUnixTimeSeconds(seconds);
            result[id.GetString()!] = new(title.GetString()!.Trim(), DateTimeOffset.UtcNow, released, true);
        }
        return result;
    }
    public async Task<string?> RefreshAsync(IEnumerable<string> ids, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var pending = ids.Where(ValidId).Distinct().Where(id =>
                (!cache.TryGetValue(id, out var value) || !value.ReleaseChecked || now - value.CheckedAt > TimeSpan.FromDays(1)) &&
                (!attempted.TryGetValue(id, out var last) || now - last > TimeSpan.FromMinutes(5))).ToArray();
            foreach (var batch in pending.Chunk(100))
            {
                foreach (var id in batch) attempted[id] = now;
                var fields = new Dictionary<string, string> { ["itemcount"] = batch.Length.ToString() };
                for (var i = 0; i < batch.Length; i++) fields[$"publishedfileids[{i}]"] = batch[i];
                using var body = new FormUrlEncodedContent(fields);
                using var response = await Client.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body, cancellation);
                response.EnsureSuccessStatusCode();
                var titles = ParseDetails(await response.Content.ReadAsStringAsync(cancellation), batch);
                foreach (var entry in titles) cache[entry.Key] = entry.Value with { CheckedAt = now };
                if (titles.Count > 0) Store.WriteJson(cache, path);
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return "Workshop lookup timed out. Cached and local names are still available."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return "Workshop names could not be refreshed: " + ex.Message; }
        finally { gate.Release(); }
    }
}
