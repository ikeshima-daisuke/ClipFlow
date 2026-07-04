using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClipFlow.Services;

/// <summary>新バージョンが出ているか GitHub Releases に問い合わせる（呼ばれたときだけ通信する）。</summary>
public static class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/ikeshima-daisuke/ClipFlow/releases/latest";
    private const string FallbackUrl = "https://github.com/ikeshima-daisuke/ClipFlow/releases/latest";

    /// <summary>
    /// 現在のバージョンより新しい release があれば <see cref="UpdateInfo"/> を返す。
    /// 通信失敗・API変更・パース失敗などはすべて null を返して黙って諦める（通常起動を妨げない）。
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipFlow-UpdateChecker");

            using var res = await http.GetAsync(ReleasesApi);
            if (!res.IsSuccessStatusCode)
                return null;

            using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;

            var latest = ParseVersion(tag);
            var current = typeof(UpdateChecker).Assembly.GetName().Version;
            if (latest == null || current == null || latest <= current)
                return null;

            return new UpdateInfo(latest, url ?? FallbackUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"v1.2.3" や "1.2.3-beta.1" のようなタグ名から <see cref="Version"/> を取り出す。</summary>
    internal static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];

        var dash = s.IndexOf('-');
        if (dash >= 0)
            s = s[..dash];

        return Version.TryParse(s, out var v) ? v : null;
    }
}

public sealed record UpdateInfo(Version Version, string ReleaseUrl);
