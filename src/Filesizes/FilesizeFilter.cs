using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cove.Plugins;

namespace Cove.Filesizes;

public sealed record FilesizeFilter(string Modifier, long Minimum, long Maximum)
{
    public const string Key = "filesizesTotalCriterion";
    public static readonly string[] Kinds = ["video", "performer", "studio", "image", "gallery", "audio", "text"];
    private static readonly Regex Size = new(@"^\s*(\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static List<UIListFilterContribution> Declarations(string owner) => Kinds.Select(kind => new UIListFilterContribution(
        Id: $"{owner}:filesize:{kind}", EntityType: kind == "gallery" ? "galleries" : kind + "s",
        Label: "Filesize", CriterionType: "string", ExtensionId: owner,
        FilterKey: Key, Modifiers: ["EQUALS"], Order: 60)).ToList();

    public static long ParseBytes(string value)
    {
        var match = Size.Match(value);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentException("Enter a size such as 500 MB, 10 GB, or 1.2 TB. A value without a unit means bytes.");
        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1000m, "MB" => 1000000m, "GB" => 1000000000m, "TB" => 1000000000000m, _ => 1m };
        decimal bytes;
        try { bytes = checked(amount * multiplier); } catch (OverflowException) { throw new ArgumentException("Filesize exceeds the supported byte range."); }
        if (bytes > long.MaxValue || decimal.Truncate(bytes) != bytes) throw new ArgumentException("Filesize must resolve to a whole number of bytes within the supported range.");
        return (long)bytes;
    }
    public static FilesizeFilter Parse(JsonElement criterion)
    {
        if (criterion.ValueKind != JsonValueKind.Object || !criterion.TryGetProperty("value", out var value)) throw new ArgumentException("A filesize value is required.");
        var modifier = criterion.TryGetProperty("modifier", out var mod) && mod.ValueKind == JsonValueKind.String
            ? mod.GetString()!.Replace("_", "").Replace("-", "").ToLowerInvariant() : "equals";
        if (!new[] { "equals", "notequals", "greaterthan", "lessthan", "between", "notbetween" }.Contains(modifier)) throw new ArgumentException("Unsupported filesize comparison.");
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : throw new ArgumentException("Enter a filesize value.");
        if (text.Contains("..") && modifier is "equals" or "notequals")
            modifier = modifier == "equals" ? "between" : "notbetween";
        if (modifier is "between" or "notbetween")
        {
            var range = text.Split("..", StringSplitOptions.TrimEntries);
            if (range.Length != 2) throw new ArgumentException("Enter an inclusive range such as 500 MB..10 GB.");
            var min = range[0] == "" ? 0 : ParseBytes(range[0]);
            var max = range[1] == "" ? long.MaxValue : ParseBytes(range[1]);
            if (min > max) throw new ArgumentException("The lower filesize must not exceed the upper filesize.");
            return new(modifier, min, max);
        }
        var bytes = ParseBytes(text);
        return new(modifier, bytes, bytes);
    }
    public IQueryable<SizeEntry> Apply(IQueryable<SizeEntry> totals) => Modifier switch {
        "equals" => totals.Where(row => row.Bytes == Minimum), "notequals" => totals.Where(row => row.Bytes != Minimum),
        "greaterthan" => totals.Where(row => row.Bytes > Minimum), "lessthan" => totals.Where(row => row.Bytes < Minimum),
        "between" => totals.Where(row => row.Bytes >= Minimum && row.Bytes <= Maximum),
        "notbetween" => totals.Where(row => row.Bytes < Minimum || row.Bytes > Maximum),
        _ => throw new ArgumentException("Unsupported filesize comparison.")
    };
}
