using System.Text;

namespace OspreyRelay.Core.Routing;

/// <summary>
/// Applies %variable% substitution to folder paths and filename templates.
///
/// Path resolution:
///   Each segment produced by a variable is sanitized individually.
///   %subject[*]% expands inline into multiple path segments.
///
/// Filename resolution:
///   Template produces the base name only — the original extension is always preserved.
///   %subject[*]% in a filename flattens with the subject delimiter instead of creating path separators.
///   An optional space-replacement character (e.g. '_' or '-') is applied post-substitution.
/// </summary>
public static class PathVariableResolver
{
    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves a folder path template. Returns a normalised absolute path (leading slash, no trailing slash).
    /// %subject[*]% expands into multiple path segments inline.
    /// </summary>
    public static string ResolvePath(string template, PathVariableContext ctx,
        string subjectDelimiter = " ")
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        var expanded = ExpandSubjectStar(template, ctx.Subject, subjectDelimiter, forFilename: false);
        expanded     = ApplyScalars(expanded, ctx, originalBase: null, subjectDelimiter: subjectDelimiter);

        var segments = expanded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var clean    = segments.Select(SanitizeSegment).Where(s => s.Length > 0);
        return "/" + string.Join("/", clean);
    }

    /// <summary>
    /// Resolves a filename template. Returns a sanitised filename with the original extension appended.
    /// Spaces in the result can be replaced by <paramref name="spaceReplacement"/> (null = keep spaces).
    /// </summary>
    public static string ResolveFilename(string template, PathVariableContext ctx,
        string originalFilename, string subjectDelimiter = " ", char? spaceReplacement = null)
    {
        if (string.IsNullOrWhiteSpace(template)) return originalFilename;

        var baseName = Path.GetFileNameWithoutExtension(originalFilename);
        var ext      = Path.GetExtension(originalFilename); // includes dot

        // In a filename, %subject[*]% flattens (no path separators)
        var expanded = ExpandSubjectStar(template, ctx.Subject, subjectDelimiter, forFilename: true);
        expanded     = ApplyScalars(expanded, ctx, baseName, subjectDelimiter);

        var sanitized = SanitizeSegment(expanded);

        if (spaceReplacement.HasValue)
            sanitized = sanitized.Replace(' ', spaceReplacement.Value);

        return (string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized) + ext;
    }

    // ── Subject[*] expansion ──────────────────────────────────────────────────

    /// <summary>
    /// Replaces %subject[*]% with either multiple path segments or a flattened string.
    /// Must run before ApplyScalars so the segments can be split cleanly.
    /// </summary>
    private static string ExpandSubjectStar(string template, string subject,
        string delimiter, bool forFilename)
    {
        const string token = "%subject[*]%";
        if (!template.Contains(token, StringComparison.OrdinalIgnoreCase))
            return template;

        var parts  = SplitSubject(subject, delimiter);
        string replacement;

        if (forFilename)
        {
            // Flatten: "Invoices Jane 2020" → "Invoices Jane 2020" (keep delimiter)
            replacement = string.Join(delimiter, parts.Select(SanitizeValue));
        }
        else
        {
            // Expand: "Invoices Jane 2020" → "Invoices/Jane/2020" (path segments)
            replacement = string.Join("/", parts.Select(SanitizeValue).Where(s => s.Length > 0));
        }

        return ReplaceInsensitive(template, token, replacement);
    }

    // ── Scalar variable substitution ──────────────────────────────────────────

    private static string ApplyScalars(string t, PathVariableContext ctx,
        string? originalBase, string subjectDelimiter)
    {
        t = ReplaceInsensitive(t, "%from%",          SanitizeValue(ctx.From));
        t = ReplaceInsensitive(t, "%fromupn%",       SanitizeValue(ctx.FromUpn));
        t = ReplaceInsensitive(t, "%fromdomain%",    SanitizeValue(ctx.FromDomain));
        t = ReplaceInsensitive(t, "%to%",            SanitizeValue(ctx.To));
        t = ReplaceInsensitive(t, "%toupn%",         SanitizeValue(ctx.ToUpn));
        t = ReplaceInsensitive(t, "%todomain%",      SanitizeValue(ctx.ToDomain));
        t = ReplaceInsensitive(t, "%tobasedomain%",  SanitizeValue(ctx.ToBaseDomain));
        t = ReplaceInsensitive(t, "%suffix%",        SanitizeValue(ctx.Suffix));
        t = ReplaceInsensitive(t, "%subject%",       SanitizeValue(ctx.Subject));
        t = ReplaceInsensitive(t, "%date%",          ctx.Date);
        t = ReplaceInsensitive(t, "%datetime%",      ctx.DateTime);
        t = ReplaceInsensitive(t, "%username%",      SanitizeValue(ctx.Username));
        t = ReplaceInsensitive(t, "%ftppath%",       SanitizeValue(ctx.FtpPath));

        // Indexed subject access %subject[n]%
        t = ResolveIndexedSubject(t, ctx.Subject, subjectDelimiter);

        // Filename-only
        if (originalBase is not null)
            t = ReplaceInsensitive(t, "%originalbasefilename%", SanitizeValue(originalBase));

        // Regex capture groups — named: %groupname%, numbered: %match1%, %match2%, etc.
        foreach (var (key, value) in ctx.RegexCaptures)
            t = ReplaceInsensitive(t, $"%{key}%", SanitizeValue(value));

        return t;
    }

    private static string ResolveIndexedSubject(string template, string subject, string delimiter)
    {
        if (!template.Contains("%subject[", StringComparison.OrdinalIgnoreCase))
            return template;

        var parts = SplitSubject(subject, delimiter);
        var sb    = new StringBuilder(template.Length);
        var i     = 0;

        while (i < template.Length)
        {
            var start = template.IndexOf("%subject[", i, StringComparison.OrdinalIgnoreCase);
            if (start < 0) { sb.Append(template[i..]); break; }

            sb.Append(template[i..start]);

            var end = template.IndexOf("]%", start + 9, StringComparison.Ordinal);
            if (end < 0) { sb.Append(template[start..]); break; }

            var indexStr = template[(start + 9)..end];
            if (int.TryParse(indexStr, out var idx) && idx >= 0 && idx < parts.Length)
                sb.Append(SanitizeValue(parts[idx]));
            // out-of-bounds → empty (segment silently dropped)

            i = end + 2; // skip "]%"
        }

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] SplitSubject(string subject, string delimiter)
    {
        if (string.IsNullOrWhiteSpace(subject)) return Array.Empty<string>();
        var parts = string.IsNullOrEmpty(delimiter)
            ? new[] { subject }
            : subject.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
    }

    /// <summary>Sanitises a value for embedding inside a path segment or filename base.</summary>
    private static string SanitizeValue(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Characters illegal in SharePoint/OneDrive names (Graph rejects these)
        ReadOnlySpan<char> illegal = stackalloc char[] { '"', '*', ':', '<', '>', '?', '\\', '|', '\0' };
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (illegal.IndexOf(c) < 0) sb.Append(c);
        return sb.ToString().Trim();
    }

    /// <summary>Sanitises a complete path segment or filename base (removes / too).</summary>
    private static string SanitizeSegment(string s)
    {
        var invalid = Path.GetInvalidFileNameChars(); // includes / and \
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!invalid.Contains(c)) sb.Append(c);
        var result = sb.ToString().Trim();
        if (result.Length > 200) result = result[..200];
        return result;
    }

    private static string ReplaceInsensitive(string source, string token, string replacement) =>
        source.Replace(token, replacement, StringComparison.OrdinalIgnoreCase);
}
