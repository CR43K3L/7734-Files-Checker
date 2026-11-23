using System.Text.Json;

namespace Files7734;

/// <summary>Dernière version publiée sur GitHub (réponse de l'API « releases/latest »).</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, long Size, string? Sha256, string Notes, string PageUrl)
{
    public const string AssetName = "7734FilesChecker.exe";

    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
            if (r.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var v)) return null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                string? sha = a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String && dg.GetString()!.StartsWith("sha256:")
                    ? dg.GetString()![7..] : null;
                return new UpdateInfo(v, tag, a.GetProperty("browser_download_url").GetString() ?? "", a.GetProperty("size").GetInt64(), sha,
                    r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "");
            }
            return null;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>« v1.2 », « 1.2.3 » → version (les 3 premiers nombres).</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        string t = tag.Trim().TrimStart('v', 'V');
        int dash = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash >= 0) t = t[..dash];
        var parts = t.Split('.');
        version = new Version(0, 0, 0);
        if (parts.Length == 0 || parts.Length > 4) return false;
        var nums = new int[3];
        for (int i = 0; i < Math.Min(3, parts.Length); i++) if (!int.TryParse(parts[i], out nums[i])) return false;
        version = new Version(nums[0], nums[1], nums[2]);
        return true;
    }

    public bool IsNewerThan(Version current) => Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
}
