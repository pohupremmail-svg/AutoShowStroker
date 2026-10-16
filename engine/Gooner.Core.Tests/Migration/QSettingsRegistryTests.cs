using System.Text;
using System.Text.Json.Nodes;
using Gooner.Core.Migration;

namespace Gooner.Core.Tests.Migration;

// What QSettings makes of a registry value when the Python app reads it back (qsettings_win.cpp,
// readKey and stringToVariant) - which is what the import has to carry over. The parity run holds
// the same conversion against values a real PyQt6 QSettings wrote (parity/generate.py, registry).
public class QSettingsRegistryTests
{
    private static JsonNode? Read(RegistryKind kind, object? data)
    {
        Assert.True(QSettingsRegistry.TryRead(kind, data, out var value), $"{kind} could not be read");
        return value;
    }

    private static void Same(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"expected {expected}, actual {actual?.ToJsonString() ?? "null"}");

    // A multi-string's lines as .NET reads them.
    private static string[] Lines(params string[] lines) => lines;

    // A QString as QSettings puts it into REG_BINARY: the UTF-16 of the text, no terminator.
    private static byte[] Binary(string text) => Encoding.Unicode.GetBytes(text);

    [Fact]
    public void text_comes_back_as_text()
    {
        Same("\"en\"", Read(RegistryKind.Sz, "en"));
        // Floats and bools are text in the registry; the typed reads turn them back.
        Same("\"0.25\"", Read(RegistryKind.Sz, "0.25"));
        Same("\"true\"", Read(RegistryKind.Sz, "true"));
        Same("\"\"", Read(RegistryKind.Sz, ""));
    }

    [Fact]
    public void text_ends_at_the_first_null_as_qt_reads_it() =>
        Same("\"before\"", Read(RegistryKind.Sz, "before\0after"));

    [Fact]
    public void an_expandable_string_is_read_as_it_is_written() =>
        Same("\"%TEMP%\"", Read(RegistryKind.ExpandSz, "%TEMP%"));

    [Fact]
    public void numbers_come_back_as_numbers()
    {
        Same("7", Read(RegistryKind.DWord, 7));
        Same("-3", Read(RegistryKind.DWord, -3));
        Same("1099511627776", Read(RegistryKind.QWord, 1L << 40));
    }

    [Fact]
    public void a_string_list_comes_back_as_a_list() =>
        Same("""["flirty", "teasing"]""", Read(RegistryKind.MultiSz, Lines("flirty", "teasing")));

    [Fact]
    public void an_empty_string_list_is_an_empty_list()
    {
        Same("[]", Read(RegistryKind.MultiSz, Array.Empty<string>()));
        // What an empty list written by QSettings may read as: a lone terminator.
        Same("[]", Read(RegistryKind.MultiSz, Lines("")));
    }

    [Fact]
    public void a_string_list_ends_at_the_first_empty_line() =>
        Same("""["a"]""", Read(RegistryKind.MultiSz, Lines("a", "", "b")));

    [Fact]
    public void an_escaped_at_sign_loses_its_escape()
    {
        // QSettings writes text starting with @ as @@, so it cannot be mistaken for a wrapped type.
        Same("\"@home\"", Read(RegistryKind.Sz, "@@home"));
        Same("""["@a", "b"]""", Read(RegistryKind.MultiSz, Lines("@@a", "b")));
    }

    [Fact]
    public void an_unwrapped_at_sign_stays_as_it_is() =>
        Same("\"@home\"", Read(RegistryKind.Sz, "@home"));

    [Fact]
    public void the_text_wrappers_are_unwrapped()
    {
        Same("\"abc\"", Read(RegistryKind.Sz, "@String(abc)"));
        Same("\"abc\"", Read(RegistryKind.Sz, "@ByteArray(abc)"));
    }

    [Fact]
    public void invalid_is_no_value() => Assert.Null(Read(RegistryKind.Sz, "@Invalid()"));

    [Fact]
    public void text_with_a_null_comes_back_whole_from_binary() =>
        // QSettings stores text containing a null as REG_BINARY instead of REG_SZ.
        Same("\"a\\u0000b\"", Read(RegistryKind.Binary, Binary("a\0b")));

    // A QDataStream blob holds nulls, so QSettings writes it as REG_BINARY.
    [Theory]
    [InlineData("@Variant(\0\0\0\u0009\0\0\0\0\u0002)")]
    [InlineData("@DateTime(\0\0\0\u0010)")]
    public void a_serialised_qt_type_cannot_be_read(string text) =>
        Assert.False(QSettingsRegistry.TryRead(RegistryKind.Binary, Binary(text), out _));

    [Theory]
    [InlineData("@Rect(1 2 3 4)")]
    [InlineData("@Size(1 2)")]
    [InlineData("@Point(1 2)")]
    public void a_geometry_cannot_be_read(string text)
    {
        Assert.False(QSettingsRegistry.TryRead(RegistryKind.Sz, text, out _));
        Assert.False(QSettingsRegistry.TryRead(RegistryKind.Binary, Binary(text), out _));
    }

    [Theory]
    [InlineData(RegistryKind.Unknown, "x")]
    [InlineData(RegistryKind.Sz, 5)]
    [InlineData(RegistryKind.DWord, "5")]
    [InlineData(RegistryKind.MultiSz, "a")]
    [InlineData(RegistryKind.Sz, null)]
    public void what_qsettings_never_writes_cannot_be_read(RegistryKind kind, object? data) =>
        Assert.False(QSettingsRegistry.TryRead(kind, data, out _));
}
