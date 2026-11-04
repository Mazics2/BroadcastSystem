using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace BroadcastSystem.Utils;

/// <summary>IPlayer uzerinden guvenli (exception firlatmayan) bilgi okuma yardimcilari.</summary>
public static class PlayerHelper
{
    public static bool IsUsable(IPlayer? player) => player is { IsValid: true };

    public static CCSPlayerController? GetController(IPlayer? player)
    {
        try
        {
            if (!IsUsable(player)) return null;
            var c = player!.Controller;
            return c is { IsValid: true } ? c : null;
        }
        catch { return null; }
    }

    /// <summary>Yetkilendirilmis SteamID64 (henuz auth olmadiysa UnauthorizedSteamID'ye duser).</summary>
    public static ulong GetSteamId64(IPlayer? player)
    {
        try
        {
            if (!IsUsable(player)) return 0;
            return player!.SteamID != 0 ? player.SteamID : player.UnauthorizedSteamID;
        }
        catch { return 0; }
    }

    public static string GetName(IPlayer? player)
    {
        try
        {
            if (!IsUsable(player)) return "Unknown";
            var name = player!.Controller?.PlayerName;
            if (string.IsNullOrEmpty(name)) name = player.Name;
            return string.IsNullOrEmpty(name) ? "Unknown" : name;
        }
        catch { return "Unknown"; }
    }

    public static string GetIpAddress(IPlayer? player)
    {
        try
        {
            if (!IsUsable(player)) return string.Empty;
            var raw = player!.IPAddress ?? string.Empty;
            int colon = raw.IndexOf(':');
            return colon > 0 ? raw[..colon] : raw;
        }
        catch { return string.Empty; }
    }

    /// <summary>"T", "CT", "SPEC" veya "NONE".</summary>
    public static string GetTeam(IPlayer? player)
    {
        var c = GetController(player);
        if (c == null) return "NONE";
        try
        {
            return c.TeamNum switch
            {
                1 => "SPEC",
                2 => "T",
                3 => "CT",
                _ => "NONE"
            };
        }
        catch { return "NONE"; }
    }

    public static bool IsAlive(IPlayer? player)
    {
        try { return IsUsable(player) && player!.IsAlive; } catch { return false; }
    }

    public static uint GetPing(IPlayer? player)
    {
        var c = GetController(player);
        try { return c?.Ping ?? 0; } catch { return 0; }
    }

    public static int GetScore(IPlayer? player)
    {
        var c = GetController(player);
        try { return c?.Score ?? 0; } catch { return 0; }
    }

    public static int GetMvps(IPlayer? player)
    {
        var c = GetController(player);
        try { return c?.MVPs ?? 0; } catch { return 0; }
    }

    public static int GetKills(IPlayer? player)
    {
        var c = GetController(player);
        try { return c?.ActionTrackingServices?.MatchStats.Kills ?? 0; } catch { return 0; }
    }

    public static int GetDeaths(IPlayer? player)
    {
        var c = GetController(player);
        try { return c?.ActionTrackingServices?.MatchStats.Deaths ?? 0; } catch { return 0; }
    }
}

