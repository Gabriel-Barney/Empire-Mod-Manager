using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace EmpireModManager;

internal static class UpdateTests
{
    public static void Run(string root, List<string> results)
    {
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + name);
            results.Add("PASS: " + name);
        }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { results.Add("PASS: " + name); return; }
            throw new InvalidOperationException("FAILED: " + name);
        }
        var version = GithubUpdater.CurrentVersion;
        var newer = new Version(version.Major, version.Minor + 1, 0);
        var name = $"EmpireModManager-{newer}-win-x64.zip";
        string Feed(string tag, bool prerelease = false, bool draft = false, bool checksum = true, string repository = GithubUpdater.Repository, string? digest = null)
        {
            var assets = new List<object>();
            foreach (var file in checksum ? new[] { name, name + ".sha256" } : new[] { name })
                assets.Add(new { name = file, state = "uploaded", size = 50000, digest, browser_download_url = $"https://github.com/{repository}/releases/download/{tag}/{file}" });
            return JsonSerializer.Serialize(new { tag_name = tag, prerelease, draft, body = "Update notes", assets });
        }
        var available = GithubUpdater.ParseRelease(Feed("v" + newer), version);
        Check(available?.Version == newer && available.Notes == "Update notes", "Discover a newer stable GitHub release with matching Windows ZIP and checksum");
        Check(GithubUpdater.ParseRelease(Feed(version.ToString()), version) is null &&
            GithubUpdater.ParseRelease(Feed("1.0.0"), version) is null, "Skip current and older releases without requiring their assets");
        Check(GithubUpdater.ParseRelease(Feed("v9.0.0-beta", true), version) is null &&
            GithubUpdater.ParseRelease(Feed("v9.0.0", draft: true), version) is null, "Exclude draft and prerelease updates");
        Check(GithubUpdater.ParseVersion("v1.10.0") > GithubUpdater.ParseVersion("v1.9.9"), "Compare release versions numerically");
        Reject(() => GithubUpdater.ParseRelease(Feed("v" + newer, checksum: false), version), "Reject a newer release without a checksum file or GitHub digest");
        var nativeDigest = GithubUpdater.ParseRelease(Feed("v" + newer, checksum: false, digest: "sha256:" + new string('a', 64)), version);
        Check(nativeDigest?.ArchiveHash == new string('a', 64) && nativeDigest.Checksum is null,
            "Discover a release with only a GitHub SHA-256 digest, as published for version 1.2.1");
        foreach (var digest in new[] { "sha256:bad", "sha512:" + new string('a', 64), "" })
            Reject(() => GithubUpdater.ParseRelease(Feed("v" + newer, checksum: false, digest: digest), version), "Reject an unverifiable GitHub asset digest: " + digest);
        Reject(() => GithubUpdater.ParseRelease(Feed("v" + newer, checksum: false, repository: "someone/other-app", digest: "sha256:" + new string('a', 64)), version),
            "Digest-only updates still reject downloads from another repository");
        Reject(() => GithubUpdater.ParseRelease(Feed("v" + newer, repository: "someone/other-app"), version), "Reject update downloads from a different repository");
        Reject(() => GithubUpdater.ParseRelease(Feed("v" + newer).Replace("https://", "http://"), version), "Require HTTPS update assets");
        var sampleHash = new string('a', 64);
        Check(GithubUpdater.ParseChecksum($"{sampleHash}  {name}\r\n", name) == sampleHash, "Read the checksum emitted by the release packager");
        Reject(() => GithubUpdater.ParseChecksum(sampleHash + "  other.zip", name), "Reject checksums for a different archive");
        Reject(() => GithubUpdater.ParseChecksum("bad  " + name, name), "Reject malformed checksums");

        var stage = Path.Combine(root, "update-stage");
        Directory.CreateDirectory(stage);
        var files = new Dictionary<string, string>();
        foreach (var file in new[] { "EmpireModManager.exe", "EmpireModManager.dll", "EmpireModManager.runtimeconfig.json", "hostfxr.dll", "hostpolicy.dll", "new-file.txt", "third-party-notices/license.txt" })
        {
            var path = UpdatePackage.SafePath(stage, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (file == "EmpireModManager.exe") File.Copy(Path.Combine(AppContext.BaseDirectory, file), path);
            else File.WriteAllText(path, "new payload " + file);
            files.Add(file, Hash(path));
        }
        var manifest = new UpdateManifest(version.ToString(), files);
        void WriteManifest() => File.WriteAllText(Path.Combine(stage, UpdatePackage.ManifestName), JsonSerializer.Serialize(manifest));
        WriteManifest();
        Check(UpdatePackage.Validate(stage, version).Files.Count == files.Count, "Verify the package manifest and executable version before installation");
        var validZip = Path.Combine(root, "valid-update.zip");
        using (var zip = ZipFile.Open(validZip, ZipArchiveMode.Create))
            foreach (var file in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, $"EmpireModManager-{version}-win-x64/" + Path.GetRelativePath(stage, file).Replace('\\', '/'));
        var extracted = Path.Combine(root, "update-extracted");
        UpdatePackage.Extract(validZip, extracted, version, CancellationToken.None);
        Check(Hash(Path.Combine(extracted, "EmpireModManager.exe")) == files["EmpireModManager.exe"], "Extract and verify the release ZIP layout");
        var zipBytes = File.ReadAllBytes(validZip);
        var downloadName = $"EmpireModManager-{version}-win-x64.zip";
        var download = new UpdateRelease(version, "v" + version, "", new Uri(GithubUpdater.RepositoryUrl),
            new Uri(GithubUpdater.RepositoryUrl + "/" + downloadName), new Uri(GithubUpdater.RepositoryUrl + "/" + downloadName + ".sha256"), zipBytes.Length);
        HttpClient DownloadClient(string digest, byte[] bytes) => new(new FixtureHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                ? new StringContent(digest + "  " + downloadName) : new ByteArrayContent(bytes) }));
        var reporting = new SilentProgress();
        using (var client = DownloadClient(Hash(validZip), zipBytes))
        {
            var prepared = Task.Run(() => GithubUpdater.DownloadAsync(download, reporting, CancellationToken.None, client)).GetAwaiter().GetResult();
            Check(prepared.Version == version && File.Exists(Path.Combine(prepared.Stage, "EmpireModManager.exe")), "Download, verify, and stage a valid release through the HTTP update flow");
            GithubUpdater.DeleteWork(prepared.Work);
        }
        using (var client = DownloadClient(new string('0', 64), zipBytes))
            Reject(() => Task.Run(() => GithubUpdater.DownloadAsync(download, reporting, CancellationToken.None, client)).GetAwaiter().GetResult(), "Reject a downloaded ZIP with a mismatched SHA-256 checksum");
        using (var client = new HttpClient(new FixtureHandler(request =>
        {
            if (request.RequestUri != download.Archive) throw new InvalidOperationException("Digest-only updates must request only the archive.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
        })))
        {
            var digestDownload = download with { Checksum = null, ArchiveHash = Hash(validZip).ToLowerInvariant() };
            var prepared = Task.Run(() => GithubUpdater.DownloadAsync(digestDownload, reporting, CancellationToken.None, client)).GetAwaiter().GetResult();
            Check(prepared.Version == version && File.Exists(Path.Combine(prepared.Stage, "EmpireModManager.exe")), "Download, verify, and stage a ZIP using GitHub's SHA-256 digest without a checksum file");
            GithubUpdater.DeleteWork(prepared.Work);
            Reject(() => Task.Run(() => GithubUpdater.DownloadAsync(digestDownload with { ArchiveHash = new string('0', 64) }, reporting, CancellationToken.None, client)).GetAwaiter().GetResult(),
                "Reject a ZIP that does not match GitHub's SHA-256 digest");
        }
        using (var client = DownloadClient(Hash(validZip), zipBytes))
            Reject(() => Task.Run(() => GithubUpdater.DownloadAsync(download with { ArchiveHash = new string('0', 64) }, reporting, CancellationToken.None, client)).GetAwaiter().GetResult(),
                "Reject conflicting checksum-file and GitHub SHA-256 digests");
        using (var client = DownloadClient(Hash(validZip), zipBytes[..^1]))
            Reject(() => Task.Run(() => GithubUpdater.DownloadAsync(download, reporting, CancellationToken.None, client)).GetAwaiter().GetResult(), "Reject an incomplete update download before extraction");
        using (var client = new HttpClient(new FixtureHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))))
            Check(Task.Run(() => GithubUpdater.CheckAsync(CancellationToken.None, client)).GetAwaiter().GetResult() is null, "A repository with no published release does not block startup");
        using (var client = new HttpClient(new FixtureHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))))
            Reject(() => Task.Run(() => GithubUpdater.CheckAsync(CancellationToken.None, client)).GetAwaiter().GetResult(), "Report a rate-limited update check without starting an installation");
        Reject(() => UpdatePackage.Validate(stage, newer), "Reject an update manifest for the wrong version");
        File.AppendAllText(Path.Combine(stage, "hostfxr.dll"), "tampered");
        Reject(() => UpdatePackage.Validate(stage, version), "Reject an extracted application file whose hash changed");
        File.WriteAllText(Path.Combine(stage, "hostfxr.dll"), "new payload hostfxr.dll");
        File.WriteAllText(Path.Combine(stage, "unexpected.txt"), "unexpected");
        Reject(() => UpdatePackage.Validate(stage, version), "Reject files absent from the update manifest");
        File.Delete(Path.Combine(stage, "unexpected.txt"));
        foreach (var invalid in new[] { "../outside.txt", "/absolute.txt", "data/presets.json", "DATA/updates.json", "file.txt:stream", "folder/../file.txt", "CON.txt", "folder /file.txt", "..\\outside.txt" })
            Reject(() => UpdatePackage.SafePath(stage, invalid), "Reject unsafe update path: " + invalid);
        var unsafeZip = Path.Combine(root, "unsafe-update.zip");
        using (var zip = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry($"EmpireModManager-{version}-win-x64/../../outside.txt").Open());
            writer.Write("invalid");
        }
        Reject(() => UpdatePackage.Extract(unsafeZip, Path.Combine(root, "unsafe-stage"), version, CancellationToken.None), "Block ZIP traversal before any file escapes staging");
        Check(!File.Exists(Path.Combine(root, "outside.txt")), "A rejected ZIP leaves no file outside its staging folder");

        string Target(string label)
        {
            var target = Path.Combine(root, label);
            Directory.CreateDirectory(Path.Combine(target, "data"));
            File.WriteAllText(Path.Combine(target, "data", "presets.json"), "saved presets");
            File.WriteAllText(Path.Combine(target, "data", "updates.json"), "saved update preferences");
            File.WriteAllText(Path.Combine(target, "personal-notes.txt"), "keep me");
            foreach (var file in files.Keys.Where(f => f != "new-file.txt"))
            {
                var path = UpdatePackage.SafePath(target, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "old payload " + file);
            }
            return target;
        }
        var target = Target("update-target");
        UpdateInstaller.Install(stage, target, Path.Combine(root, "update-backup"), version);
        Check(files.All(pair => Hash(UpdatePackage.SafePath(target, pair.Key)) == pair.Value), "Install every verified application file");
        Check(File.ReadAllText(Path.Combine(target, "data", "presets.json")) == "saved presets" &&
            File.ReadAllText(Path.Combine(target, "data", "updates.json")) == "saved update preferences" &&
            File.ReadAllText(Path.Combine(target, "personal-notes.txt")) == "keep me", "Preserve presets, settings, and unrelated files during an update");
        Check(File.ReadAllText(Path.Combine(root, "update-backup", "EmpireModManager.exe")) == "old payload EmpireModManager.exe", "Keep a backup of replaced application files");
        var rollback = Target("rollback-target");
        Reject(() => UpdateInstaller.Install(stage, rollback, Path.Combine(root, "rollback-backup"), version, relative =>
        { if (relative == "new-file.txt") throw new IOException("Simulated interrupted install"); }), "Report an interrupted installation after restoring the previous application");
        Check(files.Keys.Where(f => f != "new-file.txt").All(f => File.ReadAllText(UpdatePackage.SafePath(rollback, f)) == "old payload " + f) &&
            !File.Exists(Path.Combine(rollback, "new-file.txt")) && !File.Exists(Path.Combine(rollback, UpdatePackage.ManifestName)), "Rollback restores old files and removes newly added files");
        Check(File.ReadAllText(Path.Combine(rollback, "data", "presets.json")) == "saved presets", "Rollback preserves user data");
        var locked = Target("locked-target");
        using (File.Open(Path.Combine(locked, "hostpolicy.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => UpdateInstaller.Install(stage, locked, Path.Combine(root, "locked-backup"), version), "Refuse to update while another process holds application files open");
        Check(File.ReadAllText(Path.Combine(locked, "EmpireModManager.exe")) == "old payload EmpireModManager.exe", "A failed preflight does not replace application files");
        Reject(() => UpdateInstaller.Install(stage, stage, Path.Combine(root, "overlap-backup"), version), "Reject an installation that overlaps the updater staging folder");
    }
    static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
    sealed class SilentProgress : IProgress<string> { public void Report(string value) { } }
}
