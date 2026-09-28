namespace KeelMatrix.CorsSpec;

internal static class Validation
{
    public static string RequirePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Any(static character => character <= '\u001f' || character == '\u007f'))
        {
            throw new ArgumentException("The target path cannot contain raw control characters.", nameof(path));
        }

        if (IsRootRelativeWindowsDrivePath(path) ||
            path.Contains('\\') ||
            path.StartsWith("//", StringComparison.Ordinal) ||
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

    private static bool IsRootRelativeWindowsDrivePath(string path) =>
        path.Length >= 3 &&
        path[0] == '/' &&
        ((path[1] >= 'A' && path[1] <= 'Z') || (path[1] >= 'a' && path[1] <= 'z')) &&
        path[2] == ':';

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

        if (method.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
            method.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpMethod(method.Method.ToUpperInvariant());
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

    public static string RequireHeaderValue(string value, string parameterName, bool allowHorizontalTab = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(character => (character <= '\u001f' && (!allowHorizontalTab || character != '\u0009')) || character == '\u007f'))
        {
            throw new ArgumentException($"'{parameterName}' contains a value with an invalid control character.", parameterName);
        }

        return value;
    }

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

    public static IReadOnlyList<CorsRequestHeader> NormalizeRequestHeaders(IEnumerable<CorsRequestHeader>? headers, string parameterName)
    {
        if (headers is null)
        {
            return Array.Empty<CorsRequestHeader>();
        }

        var normalized = new SortedDictionary<string, CorsRequestHeader>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (header is null)
            {
                throw new ArgumentException($"'{parameterName}' contains a null header.", parameterName);
            }

            if (!normalized.TryAdd(header.Name, header))
            {
                throw new ArgumentException($"'{parameterName}' contains duplicate header name '{header.Name}'.", parameterName);
            }
        }

        return Array.AsReadOnly(normalized.Values.ToArray());
    }

    public static IReadOnlyList<string> MergeHeaderNames(IEnumerable<string> declared, IEnumerable<string> valued)
    {
        var merged = new SortedSet<string>(declared, StringComparer.OrdinalIgnoreCase);
        merged.UnionWith(valued);
        return Array.AsReadOnly(merged.ToArray());
    }

    public static bool IsCorsSafelistedRequestHeader(string name, string value)
    {
        if (value.Length > 128 || ContainsCorsUnsafeRequestHeaderByte(value, name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase))
        {
            return !value.Any(static character => character is '"' or '(' or ')' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '{' or '}');
        }

        if (name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase) || name.Equals("Content-Language", StringComparison.OrdinalIgnoreCase))
        {
            return !value.Any(static character => !IsLanguageCharacter(character));
        }

        if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            return IsCorsSafelistedContentType(value);
        }

        if (name.Equals("Range", StringComparison.OrdinalIgnoreCase))
        {
            return IsCorsSafelistedRange(value);
        }

        return false;
    }

    private static bool IsLanguageCharacter(char character) =>
        character is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or ' ' or '*' or ',' or '-' or '.' or ';' or '=';

    private static bool ContainsCorsUnsafeRequestHeaderByte(string value, bool allowHorizontalTab) =>
        value.Any(character =>
            (character < '\u0020' && (character != '\u0009' || !allowHorizontalTab)) ||
            character is '\u0022' or '\u0028' or '\u0029' or '\u003a' or '\u003c' or '\u003e' or '\u003f' or '\u0040' or '\u005b' or '\u005c' or '\u005d' or '\u007b' or '\u007d' or '\u007f');

    private static bool IsCorsSafelistedContentType(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsHttpWhitespace(value[start]))
        {
            start++;
        }

        while (end > start && IsHttpWhitespace(value[end - 1]))
        {
            end--;
        }

        var mediaType = value[start..end];
        var slash = mediaType.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        var type = mediaType[..slash];
        if (!IsToken(type))
        {
            return false;
        }

        var subtype = mediaType[(slash + 1)..];
        var parameterStart = subtype.IndexOf(';');
        if (parameterStart >= 0)
        {
            subtype = subtype[..parameterStart];
        }

        while (subtype.EndsWith(' ') || subtype.EndsWith('\t'))
        {
            subtype = subtype[..^1];
        }

        if (!IsToken(subtype))
        {
            return false;
        }

        var essence = string.Concat(type, "/", subtype);
        return essence.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
            essence.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) ||
            essence.Equals("text/plain", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCorsSafelistedRange(string value)
    {
        const string prefix = "bytes=";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var range = value[prefix.Length..];
        var dash = range.IndexOf('-');
        if (dash <= 0)
        {
            return false;
        }

        var start = range[..dash];
        var end = range[(dash + 1)..];
        if (!IsAsciiDigits(start) || (end.Length != 0 && !IsAsciiDigits(end)))
        {
            return false;
        }

        if (end.Contains('-'))
        {
            return false;
        }

        return end.Length == 0 || CompareDecimalStrings(start, end) <= 0;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareDecimalStrings(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        left = TrimLeadingZeros(left);
        right = TrimLeadingZeros(right);
        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.SequenceCompareTo(right);
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> value)
    {
        var index = 0;
        while (index < value.Length - 1 && value[index] == '0')
        {
            index++;
        }

        return value[index..];
    }

    private static bool IsHttpWhitespace(char character) => character is ' ' or '\t';

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
