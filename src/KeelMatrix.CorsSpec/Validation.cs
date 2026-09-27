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

        if (path.Contains('\\') ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(path, "^[A-Za-z][A-Za-z0-9+.-]*:"))
        {
            throw new ArgumentException("The target must be a relative application path; use the caller's HttpClient to select the destination.", nameof(path));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (request.RequestUri?.IsAbsoluteUri == true)
            {
                throw new ArgumentException("The target must be a relative application path; use the caller's HttpClient to select the destination.", nameof(path));
            }
        }
        catch (UriFormatException)
        {
            throw new ArgumentException("The target must be a relative application path; use the caller's HttpClient to select the destination.", nameof(path));
        }

        return path;
    }

    public static HttpMethod RequireMethod(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (!IsToken(method.Method))
        {
            throw new ArgumentException("The HTTP method must be a valid token.", nameof(method));
        }

        if (method.Method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("TRACE", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("TRACK", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The HTTP method is forbidden for browser-style CORS requests.", nameof(method));
        }

        return method;
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
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
        {
            throw new ArgumentException("Origin must be a canonical HTTP(S) browser origin without a path, query, fragment, credentials, or explicit default port.", nameof(origin));
        }

        return origin;
    }

    public static string RequireHeaderName(string name, string parameterName)
        => RequireHeaderName(name, parameterName, rejectBrowserManaged: true);

    private static string RequireHeaderName(string name, string parameterName, bool rejectBrowserManaged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Any(char.IsWhiteSpace) || name.Contains('\r') || name.Contains('\n') || !IsToken(name))
        {
            throw new ArgumentException($"'{parameterName}' contains an invalid HTTP header name.", parameterName);
        }

        if (rejectBrowserManaged && IsBrowserManagedHeader(name))
        {
            throw new ArgumentException($"'{parameterName}' contains a browser-managed or CORS protocol header that script cannot request.", parameterName);
        }

        return name;
    }

    private static bool IsToken(string value)
    {
        foreach (var character in value)
        {
            if (character <= 0x7f && (character is >= '0' and <= '9' || character is >= 'A' and <= 'Z' || character is >= 'a' and <= 'z' || "!#$%&'*+-.^_`|~".Contains(character)))
            {
                continue;
            }

            return false;
        }

        return value.Length != 0;
    }

    public static IReadOnlyList<string> NormalizeHeaderNames(IEnumerable<string>? names, string parameterName, bool rejectBrowserManaged = true)
    {
        if (names is null)
        {
            return Array.Empty<string>();
        }

        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            normalized.Add(RequireHeaderName(name, parameterName, rejectBrowserManaged));
        }

        return Array.AsReadOnly(normalized.ToArray());
    }

    public static IReadOnlyList<string> NormalizeExpectedExposedHeaders(IEnumerable<string>? names, string parameterName)
    {
        var normalized = NormalizeHeaderNames(names, parameterName, rejectBrowserManaged: false);
        if (normalized.Any(IsForbiddenResponseHeader))
        {
            throw new ArgumentException($"'{parameterName}' contains a response header that browsers forbid exposing.", parameterName);
        }

        return normalized;
    }

    private static bool IsForbiddenResponseHeader(string name) =>
        name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase);

    private static bool IsBrowserManagedHeader(string name) =>
        name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Accept-Charset", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie2", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Date", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("DNT", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Expect", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Via", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-HTTP-Method", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-HTTP-Method-Override", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Method-Override", StringComparison.OrdinalIgnoreCase);

    public static void ValidateMaxAge(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero || value.TotalSeconds > int.MaxValue || value.TotalSeconds != Math.Truncate(value.TotalSeconds))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Max-age must be a whole number of seconds between zero and Int32.MaxValue.");
        }
    }
}
