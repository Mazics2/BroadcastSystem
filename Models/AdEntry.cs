namespace BroadcastSystem.Models;

/// <summary>
/// Zamanli otomatik duyuru listesindeki ([[ads]]) tek bir duyuru/reklam ogesini temsil eder.
/// </summary>
public class AdEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Bu duyuruda global [settings].prefix kullanilsin mi?
    /// null ise [settings].enable_prefix degeri gecerlidir.
    /// </summary>
    public bool? UsePrefix { get; set; }

    /// <summary>Sohbet (Chat) mesaji. '\n' ile birden fazla satira bolunebilir.</summary>
    public string? Chat { get; set; }

    /// <summary>Ekranin ortasinda (klasik Center) gosterilecek mesaj.</summary>
    public string? Center { get; set; }

    /// <summary>Zengin HTML olarak ekranin ortasinda (SendCenterHTML) gosterilecek mesaj.</summary>
    public string? Html { get; set; }

    /// <summary>CS2 Alert panelinde (SendAlert) gosterilecek mesaj.</summary>
    public string? Alert { get; set; }

    /// <summary>HTML mesajin ekranda kalma suresi (ms). null ise [settings].default_html_duration_ms kullanilir.</summary>
    public int? HtmlDurationMs { get; set; }

    /// <summary>Takim filtresi ("T", "CT", "SPEC"). Bos ise tum takimlara gosterilir.</summary>
    public List<string> Teams { get; set; } = new();

    /// <summary>Harita filtresi ("de_dust2", "de_mirage" vb.). Bos ise tum haritalarda gosterilir.</summary>
    public List<string> Maps { get; set; } = new();

    /// <summary>
    /// Yetki filtresi:
    /// - Bos ise herkese gosterilir.
    /// - "vip" gibi yazilirsa sadece o yetkiye sahip oyunculara gosterilir.
    /// - "!vip" gibi basina '!' konursa o yetkiye SAHIP OLMAYAN oyunculara gosterilir (orn: VIP satis reklami).
    /// </summary>
    public string? Permission { get; set; }

    /// <summary>Rastgele veya agirlikli dongude gosterim agirligi (en az 1).</summary>
    public int Weight { get; set; } = 1;

    public string Label => !string.IsNullOrWhiteSpace(Name) ? Name : Id;

    public bool HasAnyContent() =>
        !string.IsNullOrWhiteSpace(Chat) ||
        !string.IsNullOrWhiteSpace(Center) ||
        !string.IsNullOrWhiteSpace(Html) ||
        !string.IsNullOrWhiteSpace(Alert);

    public bool MatchesTeam(string playerTeam)
    {
        if (Teams.Count == 0) return true;
        return Teams.Any(t => t.Equals(playerTeam, StringComparison.OrdinalIgnoreCase));
    }

    public bool MatchesMap(string currentMap)
    {
        if (Maps.Count == 0 || string.IsNullOrWhiteSpace(currentMap)) return true;
        return Maps.Any(m => currentMap.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                             m.Equals(currentMap, StringComparison.OrdinalIgnoreCase));
    }

    public string ChannelsSummary()
    {
        var list = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(Chat)) list.Add("Chat");
        if (!string.IsNullOrWhiteSpace(Center)) list.Add("Center");
        if (!string.IsNullOrWhiteSpace(Html)) list.Add("HTML");
        if (!string.IsNullOrWhiteSpace(Alert)) list.Add("Alert");
        return list.Count > 0 ? string.Join("+", list) : "Boş";
    }
}

