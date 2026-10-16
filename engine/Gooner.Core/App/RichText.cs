using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gooner.Core.App;

// The small piece of Qt rich text the app's texts are written in - <b>, <i>, <code>, <ul><li> and
// entities - as the BBCode a Godot RichTextLabel draws. A list sits on lines of its own between
// the text before and after it, which is how Qt laid out a changelog entry's lead, bullets and
// closing hook. Any other tag is dropped, its text kept.
public static partial class RichText
{
    [GeneratedRegex("<(/?)([a-zA-Z]+)[^>]*>")]
    private static partial Regex Tag();

    public static string ToBbcode(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var output = new StringBuilder();
        var itemsInList = 0;
        var at = 0;
        foreach (Match match in Tag().Matches(html))
        {
            Text(output, html[at..match.Index]);
            at = match.Index + match.Length;
            var closing = match.Groups[1].Value == "/";
            switch (match.Groups[2].Value.ToLowerInvariant())
            {
                case "b" or "i" or "code" when closing:
                    output.Append("[/").Append(match.Groups[2].Value.ToLowerInvariant()).Append(']');
                    break;
                case "b" or "i" or "code":
                    output.Append('[').Append(match.Groups[2].Value.ToLowerInvariant()).Append(']');
                    break;
                case "ul" when closing:
                    output.Append("[/ul]\n");
                    break;
                case "ul":
                    output.Append("\n[ul]");
                    itemsInList = 0;
                    break;
                case "li" when !closing:
                    if (itemsInList++ > 0) output.Append('\n');
                    break;
                case "br":
                    output.Append('\n');
                    break;
            }
        }
        Text(output, html[at..]);
        return output.ToString().Trim('\n');
    }

    // Entities decoded, and a square bracket kept from opening a tag of its own.
    private static void Text(StringBuilder output, string text)
    {
        foreach (var c in WebUtility.HtmlDecode(text))
        {
            switch (c)
            {
                case '[': output.Append("[lb]"); break;
                case ']': output.Append("[rb]"); break;
                default: output.Append(c); break;
            }
        }
    }
}
