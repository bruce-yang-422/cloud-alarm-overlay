using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Infrastructure;

/// <summary>CWA announcements distributed by NCDR's public, currently effective CAP feed.</summary>
public sealed class CwaAlertClient(HttpClient http)
{
    public const string FeedUrl = "https://alerts.ncdr.nat.gov.tw/RssAtomFeeds.ashx";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly CultureInfo Taiwan = CultureInfo.GetCultureInfo("zh-TW");

    public async Task<WeatherAlertSnapshot> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var xml = await http.GetStringAsync(FeedUrl, deadline.Token);
        var items = Parse(xml, now);
        using var concurrency = new SemaphoreSlim(4);
        var details = await Task.WhenAll(items.Select(async item =>
        {
            if (item.CapUri is null) return new[] { item };
            await concurrency.WaitAsync(deadline.Token);
            try
            {
                var cap = await http.GetStringAsync(item.CapUri, deadline.Token);
                return ParseCap(cap, item, now).ToArray();
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { throw; }
            catch { return new[] { item }; } // Unknown area stays visible as an unconfirmed-scope notice.
            finally { concurrency.Release(); }
        }));
        return new(now, details.SelectMany(a => a).ToArray());
    }

    public static IReadOnlyList<WeatherAlert> Parse(string xml, DateTimeOffset now)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        var feed = XDocument.Load(reader).Root;
        if (feed?.Name != Atom + "feed") throw new FormatException("Unexpected official alert feed.");
        var alerts = new List<WeatherAlert>();
        foreach (var entry in feed.Elements(Atom + "entry"))
        {
            var author = (string?)entry.Element(Atom + "author")?.Element(Atom + "name");
            if (author is not ("中央氣象署" or "中央氣象局")) continue;
            string Cap(string name) => entry.Elements().FirstOrDefault(e => e.Name.LocalName == name &&
                e.Name.NamespaceName.StartsWith("urn:oasis:names:tc:emergency:cap:", StringComparison.Ordinal))?.Value ?? "";
            if (Cap("status") != "Actual" || Cap("msgType") is not ("Alert" or "Update")) continue;
            var title = (string?)entry.Element(Atom + "title") ?? "";
            var id = (string?)entry.Element(Atom + "id") ?? "";
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) throw new FormatException("Incomplete official alert.");
            if (!TryTime((string?)entry.Element(Atom + "updated"), out var published) ||
                !TryTime(Cap("effective"), out var effective) || !TryTime(Cap("expires"), out var expires))
                throw new FormatException("Invalid official alert period.");
            if (published > now || effective > now || expires <= now || expires <= effective) continue;
            var summary = PlainText((string?)entry.Element(Atom + "summary") ?? "");
            var link = entry.Elements(Atom + "link").FirstOrDefault(e => (string?)e.Attribute("rel") == "alternate")?.Attribute("href")?.Value;
            alerts.Add(new(id, Classify(title), title, summary, published, effective, expires)
            {
                CapUri = Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                    uri.Host == "alerts.ncdr.nat.gov.tw" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
                    uri.AbsolutePath.StartsWith("/Capstorage/", StringComparison.OrdinalIgnoreCase) &&
                    uri.AbsolutePath.EndsWith(".cap", StringComparison.OrdinalIgnoreCase) ? uri : null
            });
        }
        return alerts.GroupBy(a => a.Id).Select(g => g.OrderByDescending(a => a.PublishedAt).First())
            .OrderBy(a => a.Kind).ThenByDescending(a => a.PublishedAt).ToArray();
    }

    public static IReadOnlyList<WeatherAlert> ParseCap(string xml, WeatherAlert source, DateTimeOffset now)
    {
        using var reader = XmlReader.Create(new StringReader(xml.TrimStart('\uFEFF')), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        var root = XDocument.Load(reader).Root ?? throw new FormatException("Missing CAP document.");
        var ns = root.Name.Namespace;
        if (root.Name.LocalName != "alert" || ns.NamespaceName is not ("urn:oasis:names:tc:emergency:cap:1.2" or "urn:oasis:names:tc:emergency:cap:1.1") ||
            (string?)root.Element(ns + "identifier") != source.Id) throw new FormatException("Unexpected CAP document.");
        if ((string?)root.Element(ns + "status") != "Actual" || (string?)root.Element(ns + "scope") != "Public" ||
            (string?)root.Element(ns + "msgType") is not ("Alert" or "Update")) return [];
        var infos = root.Elements(ns + "info").Where(info => ((string?)info.Element(ns + "language")) is null or "" or "zh-TW" or "zh-tw").ToArray();
        if (infos.Length == 0) throw new FormatException("Missing Chinese CAP information.");
        var alerts = new List<WeatherAlert>();
        for (var i = 0; i < infos.Length; i++)
        {
            var info = infos[i];
            var effective = source.EffectiveAt; var expires = source.ExpiresAt;
            if (info.Element(ns + "effective") is {} start && !TryTime(start.Value, out effective) ||
                info.Element(ns + "expires") is {} end && !TryTime(end.Value, out expires)) throw new FormatException("Invalid CAP period.");
            // Never extend the period beyond the currently-effective feed's own validity.
            effective = effective > source.EffectiveAt ? effective : source.EffectiveAt;
            expires = expires < source.ExpiresAt ? expires : source.ExpiresAt;
            if (effective > now || expires <= now || info.Elements(ns + "responseType").Any(e => e.Value == "AllClear")) continue;
            var areas = info.Elements(ns + "area").Select(area => new WeatherAlertArea(
                (string?)area.Element(ns + "areaDesc") ?? "",
                area.Elements(ns + "geocode").Select(g =>
                    ((string?)g.Element(ns + "valueName")) is "Taiwan_Geocode_103" or "Taiwan_Geocode_100"
                        ? ((string?)g.Element(ns + "value") ?? "").Trim() : "unsupported:" + g.Value).ToArray(),
                area.Elements(ns + "polygon").Select(p => p.Value).ToArray(),
                area.Elements(ns + "circle").Select(p => p.Value).ToArray())).ToArray();
            alerts.Add(source with
            {
                Id = source.Id + "/" + i,
                Title = (string?)info.Element(ns + "headline") ?? source.Title,
                Summary = PlainText((string?)info.Element(ns + "description") ?? source.Summary),
                EffectiveAt = effective, ExpiresAt = expires, Areas = areas,
                SeverityLabel = info.Elements(ns + "parameter").FirstOrDefault(p =>
                    (string?)p.Element(ns + "valueName") == "severity_level")?.Element(ns + "value")?.Value ?? ""
            });
        }
        return alerts;
    }

    private static bool TryTime(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Atom dates carry their own offset; CAP fields in this feed use Taiwan local time.
        if (Regex.IsMatch(value, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant))
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        if (!DateTime.TryParse(value, Taiwan, DateTimeStyles.AllowWhiteSpaces, out var local)) return false;
        result = new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
        return true;
    }

    private static string PlainText(string value) => Regex.Replace(WebUtility.HtmlDecode(
        Regex.Replace(value, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(1))), @"\s+", " ").Trim();

    public static WeatherAlertKind Classify(string title) => title switch
    {
        var t when t.Contains("地震") => WeatherAlertKind.Earthquake,
        var t when t.Contains("海嘯") => WeatherAlertKind.Tsunami,
        var t when t.Contains("颱風") => WeatherAlertKind.Typhoon,
        var t when t.Contains("雷") => WeatherAlertKind.Thunderstorm,
        var t when t.Contains("雨") => WeatherAlertKind.Rain,
        var t when t.Contains("風") => WeatherAlertKind.Wind,
        var t when t.Contains("高溫") => WeatherAlertKind.Heat,
        var t when t.Contains("低溫") => WeatherAlertKind.Cold,
        var t when t.Contains("霧") => WeatherAlertKind.Fog,
        _ => WeatherAlertKind.Other
    };
}
