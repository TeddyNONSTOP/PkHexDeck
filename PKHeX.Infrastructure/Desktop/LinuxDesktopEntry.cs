using System.Text;

namespace PKHeX.Infrastructure.Desktop;

/// <summary>Desktop-entry string escaping is applied after Exec argument quoting.</summary>
internal static class LinuxDesktopEntry
{
    internal static string EscapeValue(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    internal static string QuoteExec(string path)
    {
        if (path.Contains('='))
            throw new ArgumentException("Desktop entry executable paths cannot contain '='.", nameof(path));
        var argument = new StringBuilder("\"");
        foreach (var c in path)
        {
            if (c is '\\' or '"' or '$' or '`')
                argument.Append('\\');
            if (c == '%')
                argument.Append('%');
            argument.Append(c);
        }
        return EscapeValue(argument.Append('"').ToString());
    }
}
