namespace BroadcastSystem.Models;

/// <summary>
/// Sunucudaki bir oyuncunun oturum bilgilerini (isim, SteamID, ulke, giris zamani, hos geldin durumu)
/// saklar. Oyuncu cikis yaptiginda (OnClientDisconnected) IPlayer nesnesi gecersizlesse bile
/// ayrilma mesajinda {NAME}, {STEAMID2}, {COUNTRY} ve {PLAYTIME} dogru sekilde doldurulur.
/// </summary>
public sealed class PlayerSessionInfo
{
    public int Slot { get; init; }
    public ulong SteamId64 { get; set; }
    public string Name { get; set; } = "Unknown";
    public string IpAddress { get; set; } = string.Empty;
    public string CountryName { get; set; } = "Bilinmiyor";
    public string CountryCode { get; set; } = "--";
    public DateTime ConnectedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime WelcomeDueUtc { get; set; }
    public bool Welcomed { get; set; }
    public bool PublicJoinAnnounced { get; set; }

    public string FormattedPlayTime()
    {
        var elapsed = DateTime.UtcNow - ConnectedAtUtc;
        if (elapsed.TotalSeconds < 60)
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)} sn";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes} dk {elapsed.Seconds} sn";
        return $"{(int)elapsed.TotalHours} sa {elapsed.Minutes} dk";
    }
}

