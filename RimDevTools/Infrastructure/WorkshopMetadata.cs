using System.Text;

namespace RimDevTools;

internal sealed record WorkshopPreview(uint Index, string Url, string OriginalFileName, int Type);
internal sealed record WorkshopPreviewReplacement(uint Index, string Path);

internal static class WorkshopMetadata
{
    // Steamworks API codes, not Web API language codes or in-game translation directories.
    private static readonly HashSet<string> Languages = new(("arabic bulgarian schinese tchinese czech danish dutch english finnish french german greek hungarian " +
        "indonesian italian japanese koreana malay norwegian polish portuguese brazilian romanian russian spanish latam swedish thai turkish ukrainian vietnamese").Split(' '), StringComparer.Ordinal);

    internal static string Language(string[] args)
    {
        var value = CliArgs.Value(args, "--text-language") ?? "english";
        if (!Languages.Contains(value)) throw new ArgumentException("--text-language 必须使用官方 Steamworks API 语言代码，例如 english/schinese/tchinese/japanese/koreana");
        return value;
    }

    internal static string? ChangeNote(string[] args)
    {
        if (CliArgs.Has(args, "--change-note") && CliArgs.Has(args, "--change-note-file"))
            throw new ArgumentException("--change-note 与 --change-note-file 互斥");
        var value = CliArgs.Value(args, "--change-note-file") is { } file
            ? ReadText(file) : CliArgs.Value(args, "--change-note");
        if (value?.Contains('\0') == true) throw new ArgumentException("改动说明不能包含 NUL");
        // Missing or empty input deliberately sends NULL: no change note.
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static string ReadText(string file) => File.ReadAllText(Path.GetFullPath(file), new UTF8Encoding(false, true));

    internal static string Title(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0') || Encoding.UTF8.GetByteCount(value) >= 129)
            throw new ArgumentException("标题不能为空、不能包含 NUL 且必须少于 129 UTF-8 字节");
        return value;
    }

    internal static WorkshopPreviewReplacement[] Replacements(string[] args)
        => CliArgs.Values(args, "--update-preview").Select(value =>
        {
            var split = value.IndexOf('=');
            if (split <= 0 || split == value.Length - 1) throw new ArgumentException("--update-preview 需要 <从零开始的索引>=<图片路径>");
            return new WorkshopPreviewReplacement(Index(value[..split]), WorkshopPublishingService.ValidatePreview(Path.GetFullPath(value[(split + 1)..])));
        }).ToArray();

    internal static uint Index(string value) => uint.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
        ? index : throw new ArgumentException("附图索引必须为从零开始的非负整数");

    internal static WorkshopPreview[] ExpectedPreviews(WorkshopPreview[] before, string[] additions, WorkshopPreviewReplacement[] replacements, uint[] removals)
    {
        var indexes = replacements.Select(x => x.Index).Concat(removals).ToArray();
        if (indexes.Distinct().Count() != indexes.Length) throw new ArgumentException("同一附图索引不能重复替换、删除或同时替换和删除");
        if (indexes.Any(index => index >= before.Length)) throw new ArgumentException("附图索引超出当前范围，请先运行 status 查看附图索引");
        if (replacements.Any(r => before[r.Index].Type != 0)) throw new ArgumentException("--update-preview 仅替换图片类型的附图");
        return before.Where(x => !removals.Contains(x.Index)).Select(x => replacements.FirstOrDefault(r => r.Index == x.Index) is { } r
                ? x with { OriginalFileName = Path.GetFileName(r.Path), Url = "" } : x)
            .Concat(additions.Select(path => new WorkshopPreview(0, "", Path.GetFileName(path), 0)))
            .Select((x, i) => x with { Index = (uint)i }).ToArray();
    }
}
