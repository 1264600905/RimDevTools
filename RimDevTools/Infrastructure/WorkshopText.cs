using System.Net;
using System.Text.RegularExpressions;

namespace RimDevTools;

internal static class WorkshopText
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    internal static string? Plain(string? text)
    {
        if (text is null) return null;
        text = WebUtility.HtmlDecode(text).Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"\[(img|previewyoutube|youtube)(?:\s*=[^\]]*)?\].*?(?:\[/\1\]|$)", " ", Options | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[url\].*?(?:\[/url\]|$)", " ", Options | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[\*\]", "\n- ", Options);
        text = Regex.Replace(text, @"\[/?(?:td|th)\]", " ", Options);
        text = Regex.Replace(text, @"\[/?(?:h[1-6]|list|olist|p|br|hr|quote|table|tr|code)(?:\s*=[^\]]*)?\]", "\n", Options);
        text = Regex.Replace(text, @"\[/?(?:b|i|u|s|strike|spoiler|noparse|url|color|size)(?:\s*=[^\]]*)?\]", "", Options);
        // Official summaries can end in the middle of a tag or link attribute.
        text = Regex.Replace(text, @"\[/?(?:url|img|h[1-6]|b|i|u|s|strike|spoiler|list|olist|quote|code|previewyoutube|youtube)(?:\s*=|\s|$)[^\]]*$", "", Options);
        text = Regex.Replace(text, "https?://[^\\s<>\\[\\]\\\"'\\u3000-\\u303f\\uff00-\\uff65]+", match =>
        {
            var value = match.Value;
            var clean = value.TrimEnd('.', ',', '!', '?', ';', ':', ')', '}');
            return value[clean.Length..];
        }, Options);
        text = Regex.Replace(text, @"https?:/{0,2}$", "", Options);
        text = Regex.Replace(text, @"[\t\p{Zs}]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }
}
