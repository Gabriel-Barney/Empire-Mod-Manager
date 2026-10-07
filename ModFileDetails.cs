namespace EmpireModManager;

internal sealed record ModFileDetails(long? SizeBytes, DateTime? UpdatedUtc)
{
    public static ModFileDetails Read(string path, CancellationToken cancellation = default)
    {
        try
        {
            long size = 0;
            DateTime? updated = null;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                cancellation.ThrowIfCancellationRequested();
                size = checked(size + file.Length);
                if (updated is null || file.LastWriteTimeUtc > updated) updated = file.LastWriteTimeUtc;
            }
            return new(size, updated ?? Directory.GetLastWriteTimeUtc(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, null); }
    }

    public static string SizeText(long? bytes)
    {
        if (bytes is null) return "Unavailable";
        double value = bytes.Value;
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
