namespace KeelMatrix.CorsSpec;

internal static class Validation
{
    public static string RequirePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Contains('\r') || path.Contains('\n'))
        {
            throw new ArgumentException("The target path cannot contain line breaks.", nameof(path));
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new ArgumentException("The target must be a relative application path; use the caller's HttpClient to select the destination.", nameof(path));
        }

        return path;
    }

    public static string RequireOrigin(string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        if (string.Equals(origin, "null", StringComparison.Ordinal))
        {
            return origin;
        }

        if (origin.Contains('\r') || origin.Contains('\n') ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Origin must be an HTTP(S) origin without a path, query, fragment, or credentials.", nameof(origin));
        }

        return origin;
    }

    public static string RequireHeaderName(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Any(char.IsWhiteSpace) || name.Contains('\r') || name.Contains('\n') || !IsToken(name))
        {
            throw new ArgumentException($"'{parameterName}' contains an invalid HTTP header name.", parameterName);
        }

        return name;
    }

    private static bool IsToken(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character))
            {
                continue;
            }

            return false;
        }

        return value.Length != 0;
    }

    public static IReadOnlyList<string> NormalizeHeaderNames(IEnumerable<string>? names, string parameterName)
    {
        if (names is null)
        {
            return Array.Empty<string>();
        }

        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            normalized.Add(RequireHeaderName(name, parameterName));
        }

        return normalized.ToArray();
    }

    public static void ValidateMaxAge(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero || value.TotalSeconds > int.MaxValue || value.TotalSeconds != Math.Truncate(value.TotalSeconds))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Max-age must be a whole number of seconds between zero and Int32.MaxValue.");
        }
    }
}
