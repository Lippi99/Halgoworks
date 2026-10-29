using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Halgoworks.Services;

/// <summary>Renders the narration markup used in traces: **bold**, ==focus== and `code`.</summary>
public static partial class Rich
{
    public static MarkupString Render(string text)
    {
        var html = WebUtility.HtmlEncode(text);
        html = CodePattern().Replace(html, "<code>$1</code>");
        html = FocusPattern().Replace(html, "<mark>$1</mark>");
        html = BoldPattern().Replace(html, "<strong>$1</strong>");
        return new MarkupString(html);
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldPattern();

    [GeneratedRegex("==(.+?)==")]
    private static partial Regex FocusPattern();

    [GeneratedRegex("`(.+?)`")]
    private static partial Regex CodePattern();
}
