using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gooner.Core.Sessions;

// How the Python side sees a parsed JSON value - truthiness, "is a number", str() - so that code
// ported from it decides the same things and words its messages the same way. Lives next to the
// session format because that is where messages are compared word for word.
//
// One deliberate difference: in Python a bool is an int, so `true` passes as the number 1. Here
// it does not - a session that says "duration_sec": true is wrong, and saying so is better than
// quietly playing a one-second session.
internal static class PyJson
{
    public static bool IsNumber(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number;

    // A value parsed from text converts to any numeric type; one built in code only to the exact
    // type it was built from - so ask for each.
    public static double Number(JsonNode? node)
    {
        var value = (JsonValue)node!;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<JsonElement>(out var e)) return e.GetDouble();
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<float>(out var f)) return f;
        if (value.TryGetValue<decimal>(out var m)) return (double)m;
        throw new InvalidOperationException("not a number");
    }

    // isinstance(x, int): a JSON number written without a fraction or exponent.
    public static bool IsInteger(JsonNode? node)
    {
        if (!IsNumber(node)) return false;
        var value = (JsonValue)node!;
        if (value.TryGetValue<JsonElement>(out var element))
        {
            var raw = element.GetRawText();
            return raw.IndexOfAny(['.', 'e', 'E']) < 0;
        }
        return value.TryGetValue<int>(out _) || value.TryGetValue<long>(out _);
    }

    // Python truthiness, for the `x or default` idiom.
    public static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonObject obj => obj.Count > 0,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.False => false,
            JsonValueKind.Number => Number(value) != 0,
            JsonValueKind.String => value.GetValue<string>().Length > 0,
            _ => true,
        },
        _ => true,
    };

    // f"{x:.0f}" - Python rounds half to even.
    public static string F0(double x) =>
        Math.Round(x, MidpointRounding.ToEven).ToString("F0", CultureInfo.InvariantCulture);

    // str(x) of the parsed value, as an f-string would print it.
    public static string Str(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String
        ? value.GetValue<string>()
        : Repr(node);

    private static string Repr(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return "None";
            case JsonArray array:
                return "[" + string.Join(", ", array.Select(Repr)) + "]";
            case JsonObject obj:
                return "{" + string.Join(", ", obj.Select(kv => Quote(kv.Key) + ": " + Repr(kv.Value))) + "}";
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: return Quote(value.GetValue<string>());
                    case JsonValueKind.True: return "True";
                    case JsonValueKind.False: return "False";
                    case JsonValueKind.Number:
                        if (IsInteger(value))
                            return value.TryGetValue<JsonElement>(out var e) ? e.GetRawText() : value.ToJsonString();
                        return FloatRepr(Number(value));
                }
                break;
        }
        return node.ToJsonString();
    }

    // repr(float): shortest round-trip digits, always with a fraction or an exponent.
    private static string FloatRepr(double x)
    {
        var text = x.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');
        if (text.Contains('e', StringComparison.Ordinal))
        {
            // .NET writes 1e+20; Python too, but pads the exponent to two digits: 1e-05.
            var parts = text.Split('e');
            var sign = parts[1][0] is '+' or '-' ? parts[1][0].ToString() : "+";
            var digits = parts[1].TrimStart('+', '-');
            return parts[0] + "e" + sign + digits.PadLeft(2, '0');
        }
        return text.Contains('.', StringComparison.Ordinal) || text is "NaN" or "Infinity" or "-Infinity" ? text : text + ".0";
    }

    public static string Quote(string s)
    {
        // Python prefers single quotes, and switches to double ones when the text holds a single
        // quote but no double quote.
        var quote = s.Contains('\'', StringComparison.Ordinal) && !s.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var builder = new StringBuilder().Append(quote);
        foreach (var c in s)
        {
            if (c == quote || c == '\\') builder.Append('\\');
            builder.Append(c);
        }
        return builder.Append(quote).ToString();
    }
}
