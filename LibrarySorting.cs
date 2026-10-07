namespace EmpireModManager;

internal sealed class ModColumnComparer(int column, bool descending) : IComparer<Mod>
{
    int Direction(int result) => descending ? -Math.Sign(result) : Math.Sign(result);

    // Unknown or still-scanning values belong at the end in either direction.
    int Number<T>(T? left, T? right) where T : struct, IComparable<T> =>
        left is null ? right is null ? 0 : 1 : right is null ? -1 : Direction(left.Value.CompareTo(right.Value));

    int Text(string left, string right, bool natural = false) =>
        left.Length == 0 ? right.Length == 0 ? 0 : 1 : right.Length == 0 ? -1
        : Direction(natural ? NaturalCompare(left, right) : StringComparer.OrdinalIgnoreCase.Compare(left, right));

    static DateTimeOffset? Updated(Mod mod) => mod.Source == "Workshop" ? mod.WorkshopUpdatedUtc
        : mod.FileDetailsLoaded && mod.LastUpdatedUtc is { } date ? new DateTimeOffset(date.ToUniversalTime()) : null;
    static ulong? WorkshopId(Mod mod) => ulong.TryParse(mod.WorkshopId, out var id) ? id : null;

    public int Compare(Mod? left, Mod? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return 1;
        if (right is null) return -1;
        var result = column switch
        {
            0 => Text(left.Name, right.Name),
            1 => Text(left.Source, right.Source),
            2 => Text(left.Version, right.Version, natural: true),
            3 => Number(WorkshopId(left), WorkshopId(right)),
            4 => Number(Updated(left), Updated(right)),
            5 => Number(left.FileDetailsLoaded ? left.SizeBytes : null, right.FileDetailsLoaded ? right.SizeBytes : null),
            6 => Text(left.ModType.Length == 0 ? "Unspecified" : left.ModType, right.ModType.Length == 0 ? "Unspecified" : right.ModType),
            _ => 0
        };
        return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }

    // Compare numeric runs without integer conversion, including long version components.
    static int NaturalCompare(string left, string right)
    {
        var a = 0;
        var b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (char.IsAsciiDigit(left[a]) && char.IsAsciiDigit(right[b]))
            {
                var endA = a;
                var endB = b;
                while (endA < left.Length && char.IsAsciiDigit(left[endA])) endA++;
                while (endB < right.Length && char.IsAsciiDigit(right[endB])) endB++;
                while (a < endA && left[a] == '0') a++;
                while (b < endB && right[b] == '0') b++;
                var result = (endA - a).CompareTo(endB - b);
                if (result == 0) result = left.AsSpan(a, endA - a).SequenceCompareTo(right.AsSpan(b, endB - b));
                if (result != 0) return result;
                a = endA;
                b = endB;
            }
            else
            {
                var result = char.ToUpperInvariant(left[a]).CompareTo(char.ToUpperInvariant(right[b]));
                if (result != 0) return result;
                a++;
                b++;
            }
        }
        return (left.Length - a).CompareTo(right.Length - b);
    }
}

public sealed partial class MainForm
{
    int librarySortColumn = -1;
    bool librarySortDescending;
    bool libraryDragActive;
    bool pendingLibrarySort;

    void SortLibraryColumn(int column)
    {
        librarySortDescending = librarySortColumn == column && !librarySortDescending;
        librarySortColumn = column;
        ((BufferedModList)mods).SetSortIndicator(column, librarySortDescending ? SortOrder.Descending : SortOrder.Ascending);
        Filter();
    }

    void ClearLibrarySort(bool refresh = true)
    {
        librarySortColumn = -1;
        librarySortDescending = false;
        ((BufferedModList)mods).SetSortIndicator(-1, SortOrder.None);
        if (refresh) Filter();
    }

    void RefreshLibrarySort()
    {
        if (librarySortColumn < 0) return;
        if (libraryDragActive) { pendingLibrarySort = true; return; }
        Filter();
    }
}
