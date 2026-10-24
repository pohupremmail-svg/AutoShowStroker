using System.Text;
using System.Text.Json.Nodes;

namespace Gooner.Core.Migration;

// What QSettings makes of a registry value when the Python app reads it back - readKey and
// stringToVariant in qsettings_win.cpp / qsettings.cpp - as the JSON settings.json keeps.
//
// Python writes floats and bools as text ("0.25", "true"), an int as a DWORD and a list of strings
// as a multi-string; the text stays text here, and SettingsReads turns it back the way the Python
// read sides did (float(), type=bool). Checked against values a real PyQt6 QSettings wrote while
// both apps were compared side by side.
public static class QSettingsRegistry
{
    // The value, or false when it cannot be carried over: a Qt type serialised with QDataStream
    // (@Variant, @DateTime) or a geometry (@Rect, @Size, @Point), none of which the Python app
    // writes, and kinds QSettings never writes. A value of null is QSettings' @Invalid() - no value.
    public static bool TryRead(RegistryKind kind, object? data, out JsonNode? value)
    {
        value = null;
        switch (kind, data)
        {
            case (RegistryKind.Sz or RegistryKind.ExpandSz, string text):
                // QString::fromWCharArray: the text ends at the first null.
                return TryFromText(text.IndexOf('\0', StringComparison.Ordinal) is var end and >= 0 ? text[..end] : text, out value);
            case (RegistryKind.MultiSz, string[] lines):
                // QSettings stops at the first empty line; it writes a list holding one as binary.
                var list = new JsonArray();
                foreach (var line in lines.TakeWhile(l => l.Length > 0))
                {
                    if (!TryFromText(line, out var item)) return false;
                    list.Add(item);
                }
                value = list;
                return true;
            case (RegistryKind.DWord, int number):
                value = number;
                return true;
            case (RegistryKind.QWord, long number):
                value = number;
                return true;
            case (RegistryKind.Binary or RegistryKind.None, byte[] bytes):
                // Text holding a null: every UTF-16 unit of it, nulls included.
                return TryFromText(Encoding.Unicode.GetString(bytes, 0, bytes.Length & ~1), out value);
            default:
                return false;
        }
    }

    private static readonly string[] Unreadable = ["@Variant(", "@DateTime(", "@Rect(", "@Size(", "@Point("];

    // stringToVariant: an @ marks a wrapped type, and text that really starts with one is written @@.
    private static bool TryFromText(string text, out JsonNode? value)
    {
        value = null;
        if (text.StartsWith('@'))
        {
            if (text.EndsWith(')'))
            {
                if (text.StartsWith("@ByteArray(", StringComparison.Ordinal))
                {
                    value = text[11..^1];
                    return true;
                }
                if (text.StartsWith("@String(", StringComparison.Ordinal))
                {
                    value = text[8..^1];
                    return true;
                }
                if (text == "@Invalid()") return true;
                if (Unreadable.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal))) return false;
            }
            if (text.StartsWith("@@", StringComparison.Ordinal))
            {
                value = text[1..];
                return true;
            }
        }
        value = text;
        return true;
    }
}
