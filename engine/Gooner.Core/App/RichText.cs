using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gooner.Core.App;

// The small piece of Qt rich text the app's texts are written in - <h3>, <p>, <b>, <i>, <code>,
// <ul>/<ol> with <li>, and entities - as the BBCode a Godot RichTextLabel draws. A heading, a
// paragraph and a list each start on a line of their own, which is how Qt laid out a changelog
// entry's lead, bullets and closing hook, and the Guide's sections. Any other tag is dropped, its
// text kept.
public static partial class RichText
{
    [GeneratedRegex("<(/?)([a-zA-Z0-9]+)[^>]*>")]
    private static partial Regex Tag();

    public static string ToBbcode(string html, int headingSize = 16)
    {
        ArgumentNullException.ThrowIfNull(html);
        var output = new StringBuilder();
        var itemsInList = 0;
        var at = 0;

        void StartBlock()
        {
            if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
        }

        foreach (Match match in Tag().Matches(html))
        {
            Text(output, html[at..match.Index]);
            at = match.Index + match.Length;
            var closing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();
            switch (tag)
            {
                case "b" or "i" or "code":
                    output.Append(closing ? "[/" : "[").Append(tag).Append(']');
                    break;
                case "h3" when closing:
                    output.Append("[/b][/font_size]\n");
                    break;
                case "h3":
                    StartBlock();
                    output.Append("[font_size=").Append(headingSize).Append("][b]");
                    break;
                case "p" when closing:
                    output.Append('\n');
                    break;
                case "p":
                    StartBlock();
                    break;
                case "ul" or "ol" when closing:
                    output.Append("[/").Append(tag).Append("]\n");
                    break;
                case "ul" or "ol":
                    StartBlock();
                    output.Append(tag == "ol" ? "[ol type=1]" : "[ul]");
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
