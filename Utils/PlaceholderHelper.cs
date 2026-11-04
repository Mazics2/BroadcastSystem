using System.Text.RegularExpressions;
using BroadcastSystem.Config;
using BroadcastSystem.Models;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace BroadcastSystem.Utils;

/// <summary>
/// Tek gecisli (single-pass) {PLACEHOLDER} degistirici.
/// Hem sunucu genel degiskenlerini ({PLAYERS}, {MAXPLAYERS}, {TIME}, {DATE}, {MAP}, {SERVERNAME}, {DISCORD}, {WEBSITE}...)
/// hem de oyuncu ozel degiskenlerini ({NAME}, {STEAMID2}, {STEAMID64}, {COUNTRY}, {COUNTRY_CODE}, {PING}, {PLAYTIME}...)
/// guvenle doldurur.
/// </summary>
public static class PlaceholderHelper
{
    private static readonly Regex TokenRegex = new(@"\{(?<k>[A-Z0-9_]+)\}", RegexOptions.Compiled);

    public static Dictionary<string, string> BuildServerValues(ISwiftlyCore core, PluginConfig config)
    {
        var all = core.PlayerManager.GetAllPlayers().Where(PlayerHelper.IsUsable).ToList();
        int humanCount = all.Count(p => !p!.IsFakeClient);
        int botCount = all.Count(p => p!.IsFakeClient);
        int tCount = all.Count(p => !p!.IsFakeClient && PlayerHelper.GetTeam(p) == "T");
        int ctCount = all.Count(p => !p!.IsFakeClient && PlayerHelper.GetTeam(p) == "CT");
        int specCount = all.Count(p => !p!.IsFakeClient && PlayerHelper.GetTeam(p) == "SPEC");

        string mapName = "unknown";
        int maxPlayers = 32;
        string serverIp = config.Settings.ServerDomain;

        try
        {
            var rawMap = core.Engine.GlobalVars.MapName.ToString();
            if (!string.IsNullOrWhiteSpace(rawMap)) mapName = rawMap;
            var mc = core.Engine.GlobalVars.MaxClients;
            if (mc > 0) maxPlayers = mc;
            if (!string.IsNullOrWhiteSpace(core.Engine.ServerIP))
                serverIp = core.Engine.ServerIP!;
        }
        catch { /* fallback */ }

        string serverName = config.Settings.ServerNameOverride;
        if (string.IsNullOrWhiteSpace(serverName))
        {
            try
            {
                serverName = core.ConVar.FindAsString("hostname")?.ValueAsString ?? "Counter-Strike 2";
            }
            catch
            {
                serverName = "Counter-Strike 2";
            }
        }

        var now = DateTime.Now;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TIME"] = now.ToString("HH:mm:ss"),
            ["SHORTTIME"] = now.ToString("HH:mm"),
            ["DATE"] = now.ToString("dd.MM.yyyy"),
            ["DATETIME"] = now.ToString("dd.MM.yyyy HH:mm"),
            ["MAP"] = mapName,
            ["SERVERNAME"] = ColorHelper.Sanitize(serverName),
            ["SERVERIP"] = serverIp,
            ["DOMAIN"] = config.Settings.ServerDomain,
            ["DISCORD"] = config.Settings.DiscordInvite,
            ["WEBSITE"] = config.Settings.WebsiteUrl,
            ["PLAYERS"] = humanCount.ToString(),
            ["BOTS"] = botCount.ToString(),
            ["TOTALPLAYERS"] = (humanCount + botCount).ToString(),
            ["MAXPLAYERS"] = maxPlayers.ToString(),
            ["T_COUNT"] = tCount.ToString(),
            ["CT_COUNT"] = ctCount.ToString(),
            ["SPEC_COUNT"] = specCount.ToString(),
            ["PREFIX"] = config.Settings.Prefix,
        };
    }

    public static Dictionary<string, string> WithPlayerValues(
        Dictionary<string, string> baseValues,
        IPlayer? player,
        PlayerSessionInfo? session = null)
    {
        var dict = new Dictionary<string, string>(baseValues, StringComparer.Ordinal);

        ulong sid = session?.SteamId64 is > 0 ? session.SteamId64 : PlayerHelper.GetSteamId64(player);
        string rawName = PlayerHelper.IsUsable(player) ? PlayerHelper.GetName(player) : (session?.Name ?? "Oyuncu");
        string ip = session?.IpAddress is { Length: > 0 } ? session.IpAddress : PlayerHelper.GetIpAddress(player);

        dict["NAME"] = ColorHelper.Sanitize(rawName);
        dict["STEAMID64"] = sid == 0 ? "-" : sid.ToString();
        dict["STEAMID2"] = sid == 0 ? "-" : SteamIdHelper.ToSteam2(sid);
        dict["STEAMID3"] = sid == 0 ? "-" : SteamIdHelper.ToSteam3(sid);
        dict["IP"] = string.IsNullOrWhiteSpace(ip) ? "-" : ip;
        dict["COUNTRY"] = session?.CountryName ?? "Türkiye";
        dict["COUNTRY_CODE"] = session?.CountryCode ?? "TR";
        dict["PLAYTIME"] = session?.FormattedPlayTime() ?? "0 sn";

        if (PlayerHelper.IsUsable(player))
        {
            dict["SLOT"] = player!.Slot.ToString();
            dict["PLAYERID"] = player.PlayerID.ToString();
            dict["USERID"] = player.UserID.ToString();
            dict["TEAM"] = PlayerHelper.GetTeam(player);
            dict["PING"] = PlayerHelper.GetPing(player).ToString();
            dict["SCORE"] = PlayerHelper.GetScore(player).ToString();
            dict["KILLS"] = PlayerHelper.GetKills(player).ToString();
            dict["DEATHS"] = PlayerHelper.GetDeaths(player).ToString();
            dict["MVPS"] = PlayerHelper.GetMvps(player).ToString();
        }
        else
        {
            dict["SLOT"] = (session?.Slot ?? -1).ToString();
            dict["PLAYERID"] = (session?.Slot ?? -1).ToString();
            dict["USERID"] = "-1";
            dict["TEAM"] = "NONE";
            dict["PING"] = "0";
            dict["SCORE"] = "0";
            dict["KILLS"] = "0";
            dict["DEATHS"] = "0";
            dict["MVPS"] = "0";
        }

        return dict;
    }

    public static string Apply(string? input, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return TokenRegex.Replace(input, m => values.TryGetValue(m.Groups["k"].Value, out var v) ? v : m.Value);
    }
}

