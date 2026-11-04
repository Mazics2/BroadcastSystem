using System.Net;
using SwiftlyS2.Shared.Players;

namespace BroadcastSystem.Utils;

/// <summary>
/// Harici DLL bagimliligi olmadan oyuncunun IP adresinden (varsa CSV veritabanindan veya yerel ag kontrolunden)
/// ve istemci oyun dilinden (IPlayer.PlayerLanguage) ulke adi ve ulke kodunu (orn: "Türkiye", "TR") cozumler.
/// </summary>
public sealed class GeoIpHelper : IDisposable
{
    private readonly List<(uint Start, uint End, string Code)> _ipRanges = new();

    private static readonly Dictionary<string, string> CountryNamesTr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TR"] = "Türkiye",
        ["AZ"] = "Azerbaycan",
        ["DE"] = "Almanya",
        ["NL"] = "Hollanda",
        ["FR"] = "Fransa",
        ["GB"] = "Birleşik Krallık",
        ["US"] = "ABD",
        ["RU"] = "Rusya",
        ["UA"] = "Ukrayna",
        ["PL"] = "Polonya",
        ["BG"] = "Bulgaristan",
        ["RO"] = "Romanya",
        ["GR"] = "Yunanistan",
        ["AT"] = "Avusturya",
        ["CH"] = "İsviçre",
        ["BE"] = "Belçika",
        ["SE"] = "İsveç",
        ["NO"] = "Norveç",
        ["DK"] = "Danimarka",
        ["FI"] = "Finlandiya",
        ["IT"] = "İtalya",
        ["ES"] = "İspanya",
        ["PT"] = "Portekiz",
        ["CZ"] = "Çekya",
        ["HU"] = "Macaristan",
        ["RS"] = "Sırbistan",
        ["KZ"] = "Kazakistan",
        ["GE"] = "Gürcistan",
        ["CY"] = "Kıbrıs",
    };

    private static readonly (string Key, string Name, string Code)[] LanguageMappings =
    {
        ("turkish", "Türkiye", "TR"),
        ("tr", "Türkiye", "TR"),
        ("german", "Almanya", "DE"),
        ("de", "Almanya", "DE"),
        ("russian", "Rusya", "RU"),
        ("ru", "Rusya", "RU"),
        ("ukrainian", "Ukrayna", "UA"),
        ("uk", "Ukrayna", "UA"),
        ("french", "Fransa", "FR"),
        ("fr", "Fransa", "FR"),
        ("polish", "Polonya", "PL"),
        ("pl", "Polonya", "PL"),
        ("dutch", "Hollanda", "NL"),
        ("nl", "Hollanda", "NL"),
        ("romanian", "Romanya", "RO"),
        ("ro", "Romanya", "RO"),
        ("bulgarian", "Bulgaristan", "BG"),
        ("bg", "Bulgaristan", "BG"),
        ("greek", "Yunanistan", "GR"),
        ("el", "Yunanistan", "GR"),
        ("italian", "İtalya", "IT"),
        ("it", "İtalya", "IT"),
        ("spanish", "İspanya", "ES"),
        ("es", "İspanya", "ES"),
        ("portuguese", "Portekiz", "PT"),
        ("pt", "Portekiz", "PT"),
        ("swedish", "İsveç", "SE"),
        ("sv", "İsveç", "SE"),
        ("norwegian", "Norveç", "NO"),
        ("no", "Norveç", "NO"),
        ("danish", "Danimarka", "DK"),
        ("da", "Danimarka", "DK"),
        ("finnish", "Finlandiya", "FI"),
        ("fi", "Finlandiya", "FI"),
        ("czech", "Çekya", "CZ"),
        ("cs", "Çekya", "CZ"),
        ("hungarian", "Macaristan", "HU"),
        ("hu", "Macaristan", "HU"),
        ("english", "Uluslararası", "EN"),
        ("en", "Uluslararası", "EN"),
    };

    public bool IsDatabaseLoaded => _ipRanges.Count > 0;

    /// <summary>
    /// Config klasorunde istege bagli IP-Country CSV dosyasi (start_ip,end_ip,country_code) varsa yukler.
    /// Yoksa istemci dilinden ve yerel ag kontrolunden otomatik cozumleme yapar.
    /// </summary>
    public void TryLoadDatabase(string configDir, string pluginDir, string? configuredPath)
    {
        _ipRanges.Clear();

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (Path.IsPathRooted(configuredPath))
                candidates.Add(configuredPath);
            else
            {
                candidates.Add(Path.Combine(configDir, configuredPath));
                candidates.Add(Path.Combine(pluginDir, configuredPath));
            }
        }
        candidates.Add(Path.Combine(configDir, "ip-country.csv"));
        candidates.Add(Path.Combine(pluginDir, "ip-country.csv"));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path) || !path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var line in File.ReadLines(path))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    var s0 = parts[0].Trim('"', ' ');
                    var s1 = parts[1].Trim('"', ' ');
                    var code = parts[2].Trim('"', ' ').ToUpperInvariant();
                    if (TryIpToUint(s0, out uint start) && TryIpToUint(s1, out uint end) && code.Length == 2)
                        _ipRanges.Add((start, end, code));
                }

                _ipRanges.Sort((a, b) => a.Start.CompareTo(b.Start));
                if (_ipRanges.Count > 0) return;
            }
            catch
            {
                // Okunamayan dosyayi atla
            }
        }
    }

    public (string CountryName, string CountryCode) Resolve(IPlayer? player, string? ipAddress = null)
    {
        var ip = ipAddress ?? PlayerHelper.GetIpAddress(player);

        if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip, out var parsedIp))
        {
            if (IsPrivateOrLoopback(parsedIp))
                return ("Yerel Ağ", "LAN");

            if (_ipRanges.Count > 0 && TryIpToUint(ip, out uint ipNum))
            {
                var code = FindCountryCode(ipNum);
                if (code != null)
                {
                    var name = CountryNamesTr.TryGetValue(code, out var trName) ? trName : code;
                    return (name, code);
                }
            }
        }

        // Oyuncunun CS2 istemci dilinden (PlayerLanguage) ulke cozumle
        try
        {
            if (PlayerHelper.IsUsable(player))
            {
                var lang = player!.PlayerLanguage.ToString();
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    foreach (var m in LanguageMappings)
                    {
                        if (lang.Equals(m.Key, StringComparison.OrdinalIgnoreCase) ||
                            lang.Contains(m.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            return (m.Name, m.Code);
                        }
                    }
                }
            }
        }
        catch { /* yoksay */ }

        return ("Türkiye", "TR");
    }

    private string? FindCountryCode(uint ip)
    {
        int lo = 0, hi = _ipRanges.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            var r = _ipRanges[mid];
            if (ip < r.Start) hi = mid - 1;
            else if (ip > r.End) lo = mid + 1;
            else return r.Code;
        }
        return null;
    }

    private static bool TryIpToUint(string ipStr, out uint result)
    {
        result = 0;
        if (uint.TryParse(ipStr, out result)) return true;
        if (!IPAddress.TryParse(ipStr, out var ip)) return false;
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        result = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        return true;
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] == 10 || bytes[0] == 127) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        }
        return false;
    }

    public void Dispose() => _ipRanges.Clear();
}
