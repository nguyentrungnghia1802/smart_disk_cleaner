namespace SmartDiskCleaner.Domain;

public static class PathNormalizer
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(normalized);
        if (!string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
        }

        return normalized;
    }

    public static bool EqualsPath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinOrEqual(string candidate, string parent)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedParent = Normalize(parent);
        if (string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string Parent(string path) => Path.GetDirectoryName(Normalize(path)) ?? Normalize(path);

    public static IReadOnlyList<string> Segments(string path)
    {
        var root = Path.GetPathRoot(Normalize(path)) ?? string.Empty;
        return Normalize(path)[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
    }
}

