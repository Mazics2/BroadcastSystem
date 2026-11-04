using System.Text.RegularExpressions;

namespace BroadcastSystem.Utils;

/// <summary>SteamID64 / SteamID2 (STEAM_X:Y:Z) / SteamID3 ([U:1:N]) donusumleri.</summary>
public static class SteamIdHelper
{
    private const ulong Base = 76561197960265728UL;

    private static readonly Regex Steam2Regex = new(@"^STEAM_[0-5]:(?<y>[01]):(?<z>\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Steam3Regex = new(@"^\[?U:1:(?<id>\d+)\]?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string ToSteam2(ulong steamId64)
    {
        if (steamId64 <= Base) return string.Empty;
        ulong account = steamId64 - Base;
        return $"STEAM_0:{account & 1}:{account >> 1}";
    }

    public static string ToSteam3(ulong steamId64)
    {
        if (steamId64 <= Base) return string.Empty;
        return $"[U:1:{steamId64 - Base}]";
    }

    /// <summary>Herhangi bir formattaki SteamID'yi SteamID64'e cevirir. Basarisizsa 0 doner.</summary>
    public static ulong Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        var s = input.Trim();

        if (ulong.TryParse(s, out var id64) && id64 > Base) return id64;

        var m2 = Steam2Regex.Match(s);
        if (m2.Success)
        {
            ulong y = ulong.Parse(m2.Groups["y"].Value);
            ulong z = ulong.Parse(m2.Groups["z"].Value);
            return Base + z * 2 + y;
        }

        var m3 = Steam3Regex.Match(s);
        if (m3.Success) return Base + ulong.Parse(m3.Groups["id"].Value);

        return 0;
    }
}

