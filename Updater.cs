using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmpireModManager;

internal sealed record UpdateRelease(Version Version, string Tag, string Notes, Uri Page, Uri Archive, Uri? Checksum, long Size, string? ArchiveHash = null)
{
    public string ArchiveName => $"EmpireModManager-{Version}-win-x64.zip";
}

internal sealed class UpdatePreferences
{
    public bool CheckOnLaunch { get; set; } = true;
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "data", "updates.json");
    public static UpdatePreferences Load()
    {
        try { return File.Exists(DefaultPath) ? JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(DefaultPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save() => Store.WriteJson(this, DefaultPath);
}

internal static class GithubUpdater
{
    public const string Repository = "Gabriel-Barney/Empire-Mod-Manager";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public static Version CurrentVersion => ParseVersion(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0]);
    const long MaxArchive = 300L * 1024 * 1024;
    static readonly HttpClient Client = CreateClient();
    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EmpireModManager/" + CurrentVersion);
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }
    internal static Version ParseVersion(string text)
    {
        var value = text.TrimStart('v', 'V');
        if (!Regex.IsMatch(value, @"^\d+\.\d+\.\d+$") || !Version.TryParse(value, out var version))
            throw new InvalidDataException("The release tag must use a stable version such as v1.2.0.");
        return version;
    }
    public static async Task<UpdateRelease?> CheckAsync(CancellationToken token, HttpClient? client = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await (client ?? Client).GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new IOException("GitHub is limiting update checks. Please try again later.");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), CurrentVersion);
    }
    internal static UpdateRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version <= current) return null;
        var name = $"EmpireModManager-{version}-win-x64.zip";
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement? Asset(string expected, bool required = true)
        {
            var matches = assets.Where(a => a.GetProperty("name").GetString() == expected &&
                a.GetProperty("state").GetString() == "uploaded").ToArray();
            if (matches.Length == 0 && !required) return null;
            if (matches.Length != 1) throw new InvalidDataException("The release is missing or has duplicate update files: " + expected + ". Please use the GitHub release page.");
            return matches[0];
        }
        Uri AssetUrl(JsonElement asset, string expected)
        {
            var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            var allowed = $"{RepositoryUrl}/releases/download/{Uri.EscapeDataString(tag)}/{expected}";
            if (url.AbsoluteUri != allowed) throw new InvalidDataException("Unexpected update download location.");
            return url;
        }
        var archive = Asset(name)!.Value;
        var checksum = Asset(name + ".sha256", required: false);
        string? hash = null;
        if (archive.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
            digest.GetString() is { } value && Regex.IsMatch(value, "^sha256:[a-fA-F0-9]{64}$"))
            hash = value[7..];
        if (checksum is null && hash is null)
            throw new InvalidDataException("The published release has no SHA-256 checksum file or GitHub SHA-256 digest. The release publisher must provide one before it can be installed safely.");
        var size = archive.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxArchive) throw new InvalidDataException("The update ZIP has an unsupported size.");
        return new(version, tag, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            new Uri($"{RepositoryUrl}/releases/tag/{Uri.EscapeDataString(tag)}"),
            AssetUrl(archive, name), checksum is { } checksumAsset ? AssetUrl(checksumAsset, name + ".sha256") : null, size, hash);
    }
    internal static string ParseChecksum(string text, string fileName)
    {
        var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[a-fA-F0-9]{64}$") || parts[1].TrimStart('*') != fileName)
            throw new InvalidDataException("The update checksum is invalid or belongs to another file.");
        return parts[0];
    }
    public static async Task<PreparedUpdate> DownloadAsync(UpdateRelease release, IProgress<string> progress, CancellationToken token, HttpClient? client = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var work = Path.Combine(Path.GetTempPath(), "EmpireModManager-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            progress.Report("Downloading and verifying the update…");
            var hash = release.ArchiveHash;
            if (release.Checksum is { } checksumUrl)
            {
                using var checksumResponse = await (client ?? Client).GetAsync(checksumUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                checksumResponse.EnsureSuccessStatusCode();
                using var checksumStream = await checksumResponse.Content.ReadAsStreamAsync(timeout.Token);
                using var checksumBuffer = new MemoryStream();
                await CopyLimited(checksumStream, checksumBuffer, 4096, timeout.Token);
                var fileHash = ParseChecksum(System.Text.Encoding.UTF8.GetString(checksumBuffer.ToArray()), release.ArchiveName);
                if (hash is not null && !hash.Equals(fileHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The release checksum file does not match GitHub's SHA-256 digest.");
                hash = fileHash;
            }
            if (!Regex.IsMatch(hash ?? "", "^[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("The release has no valid SHA-256 checksum to verify the update.");
            var zip = Path.Combine(work, release.ArchiveName);
            using (var response = await (client ?? Client).GetAsync(release.Archive, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var destination = File.Create(zip);
                long received = 0;
                await CopyLimited(source, destination, release.Size, timeout.Token, bytes =>
                {
                    received += bytes;
                    progress.Report($"Downloading update… {received * 100 / release.Size}%");
                });
                if (received != release.Size) throw new InvalidDataException("The update download is incomplete.");
            }
            await using (var file = File.OpenRead(zip))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update failed SHA-256 verification. Your current installation has not been changed.");
            progress.Report("Preparing the new version…");
            var stage = Path.Combine(work, "package");
            await Task.Run(() => UpdatePackage.Extract(zip, stage, release.Version, timeout.Token), timeout.Token);
            return new(work, stage, release.Version);
        }
        catch { DeleteWork(work); throw; }
    }
    static async Task CopyLimited(Stream source, Stream destination, long limit, CancellationToken token, Action<int>? received = null)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("The update download exceeds its expected size.");
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
            received?.Invoke(count);
        }
    }
    internal static void DeleteWork(string work)
    {
        var full = Path.GetFullPath(work);
        if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Regex.IsMatch(Path.GetFileName(full), "^EmpireModManager-update-[a-f0-9]{32}$")) return;
        try { if (Directory.Exists(full)) Directory.Delete(full, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed record PreparedUpdate(string Work, string Stage, Version Version)
{
    public void StartInstaller()
    {
        using var parent = Process.GetCurrentProcess();
        var job = new UpdateJob(Stage, AppContext.BaseDirectory, Work, Version.ToString(), parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        var path = Path.Combine(Work, "job.json");
        File.WriteAllText(path, JsonSerializer.Serialize(job));
        var start = new ProcessStartInfo(Path.Combine(Stage, "EmpireModManager.exe")) { UseShellExecute = false, WorkingDirectory = Stage, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(path);
        using var helper = Process.Start(start) ?? throw new IOException("The updater could not be started.");
    }
}

internal sealed record UpdateManifest(string Version, Dictionary<string, string> Files);
internal sealed record UpdateJob(string Stage, string Target, string Work, string Version, int ParentId, long ParentStartedUtc);

internal static class UpdatePackage
{
    public const string ManifestName = "update-manifest.json";
    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\')) throw new InvalidDataException("Unsafe update file path.");
        var parts = relative.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", RegexOptions.IgnoreCase)) ||
            parts[0].Equals("data", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update contains an unsafe or personal-data path.");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update contains a file outside the application folder.");
        RejectLinks(root, full);
        return full;
    }
    static void RejectLinks(string root, string file)
    {
        var current = file;
        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The update cannot use symbolic links or junctions.");
            if (current.Equals(stop, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current) ?? throw new InvalidDataException("Invalid update folder.");
        }
    }
    internal static void Extract(string archive, string stage, Version expected, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 4000 || zip.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024) throw new InvalidDataException("The update package is too large.");
        var prefix = $"EmpireModManager-{expected}-win-x64/";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(stage);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            // Compress-Archive writes backslashes on Windows; normalize ZIP names before validation.
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("The update ZIP has an unexpected top-level folder.");
            var relative = name[prefix.Length..];
            if (relative.Length == 0 && name.EndsWith('/')) continue;
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("The update ZIP contains a link.");
            if (name.EndsWith('/')) { Directory.CreateDirectory(SafePath(stage, relative.TrimEnd('/'))); continue; }
            if (!seen.Add(relative)) throw new InvalidDataException("The update ZIP contains duplicate file paths.");
            var path = SafePath(stage, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
        Validate(stage, expected);
    }
    internal static UpdateManifest Validate(string stage, Version expected)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(Path.Combine(stage, ManifestName)), Store.JsonOptions)
            ?? throw new InvalidDataException("The update manifest is missing.");
        if (manifest.Version != expected.ToString() || manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > 4000)
            throw new InvalidDataException("The update manifest has an unexpected version or file list.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, hash) in manifest.Files)
        {
            if (!names.Add(name) || name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(hash ?? "", "^[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("The update manifest contains invalid files or hashes.");
            using var file = File.OpenRead(SafePath(stage, name));
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An extracted update file failed verification: " + name);
        }
        foreach (var required in new[] { "EmpireModManager.exe", "EmpireModManager.dll", "EmpireModManager.runtimeconfig.json", "hostfxr.dll", "hostpolicy.dll" })
            if (!names.Contains(required)) throw new InvalidDataException("The update package is missing required application files.");
        var actual = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(stage, p).Replace('\\', '/')).Where(p => p != ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(names)) throw new InvalidDataException("The update package contains files not listed in its manifest.");
        var info = FileVersionInfo.GetVersionInfo(Path.Combine(stage, "EmpireModManager.exe"));
        if (info.FileMajorPart != expected.Major || info.FileMinorPart != expected.Minor || info.FileBuildPart != expected.Build)
            throw new InvalidDataException("The packaged executable version does not match the release.");
        return manifest;
    }
}

internal static class UpdateInstaller
{
    public static int Run(string jobPath)
    {
        UpdateJob? job = null;
        try
        {
            job = JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(jobPath)) ?? throw new InvalidDataException("The update job is missing.");
            if (!Path.GetFullPath(job.Stage).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetDirectoryName(Path.GetFullPath(job.Stage))!.Equals(Path.GetFullPath(job.Work), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(jobPath).Equals(Path.Combine(job.Work, "job.json"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The updater was started from an unexpected location.");
            try
            {
                using var parent = Process.GetProcessById(job.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == job.ParentStartedUtc && !parent.WaitForExit(90000))
                    throw new IOException("The program is still running. Close it before trying the update again.");
            }
            catch (ArgumentException) { /* The original process already exited. */ }
            Install(job.Stage, job.Target, Path.Combine(job.Work, "backup"), GithubUpdater.ParseVersion(job.Version));
            Restart(job.Target, "--updated", job.Work);
            return 0;
        }
        catch (Exception ex)
        {
            if (job is not null)
                try { File.WriteAllText(Path.Combine(job.Work, "update-error.txt"), ex.ToString()); } catch { }
            MessageBox.Show("The update could not be completed.\n\n" + ex.Message +
                (job is null ? "" : "\n\nUpdate files and any backup are in:\n" + job.Work), "Empire Mod Manager update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    internal static void Install(string stage, string target, string backup, Version expected, Action<string>? afterCopy = null)
    {
        var manifest = UpdatePackage.Validate(stage, expected);
        stage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (stage.Equals(target, StringComparison.OrdinalIgnoreCase) || target.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            stage.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(target, "EmpireModManager.exe")))
            throw new InvalidDataException("The update target is not an existing separate installation.");
        var paths = manifest.Files.Keys.Append(UpdatePackage.ManifestName).ToArray();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Back up and check access to every destination before replacing any files.
        foreach (var relative in paths)
        {
            var path = UpdatePackage.SafePath(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path))
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var saved = UpdatePackage.SafePath(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(path, saved, false);
                existing.Add(relative);
            }
            else
            {
                // Check folder writability without leaving a new app file behind.
                var probe = Path.Combine(Path.GetDirectoryName(path)!, ".update-probe-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe)) { }
                File.Delete(probe);
            }
        }
        var changed = new List<string>();
        try
        {
            foreach (var relative in paths)
            {
                Replace(UpdatePackage.SafePath(stage, relative), UpdatePackage.SafePath(target, relative));
                changed.Add(relative);
                afterCopy?.Invoke(relative);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var relative in changed.AsEnumerable().Reverse())
                try
                {
                    var path = UpdatePackage.SafePath(target, relative);
                    if (existing.Contains(relative)) Replace(UpdatePackage.SafePath(backup, relative), path);
                    else File.Delete(path);
                }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            if (rollbackErrors.Count > 0) throw new IOException("Some files could not be restored. Close all copies of the app and restore the backup from " + backup, new AggregateException(rollbackErrors.Prepend(failure)));
            throw new IOException("The previous application files were restored. " + failure.Message, failure);
        }
    }
    static void Replace(string source, string target)
    {
        var temporary = target + ".update-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temporary); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    static void Restart(string target, string argument, string work)
    {
        var start = new ProcessStartInfo(Path.Combine(target, "EmpireModManager.exe")) { UseShellExecute = false, WorkingDirectory = target };
        start.ArgumentList.Add(argument);
        start.ArgumentList.Add(work);
        using var process = Process.Start(start) ?? throw new IOException("The updated app could not restart. Open EmpireModManager.exe manually.");
    }
    public static void CleanupAfterRestart(string work)
    {
        _ = Task.Run(async () => { await Task.Delay(10000); GithubUpdater.DeleteWork(work); });
    }
}
