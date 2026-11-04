using BroadcastSystem.Models;

namespace BroadcastSystem.Config;

public class PluginConfig
{
    public SettingsSection Settings { get; set; } = new();
    public WelcomeSection Welcome { get; set; } = new();
    public CommandSection Commands { get; set; } = new();
    public List<AdEntry> Ads { get; set; } = new();

    public class SettingsSection
    {
        public bool EnableAds { get; set; } = true;
        public float AdIntervalSeconds { get; set; } = 45.0f;

        /// <summary>"sequential" (sirali) veya "random" (agirlikli rastgele).</summary>
        public string RotationMode { get; set; } = "sequential";

        /// <summary>Zamanli duyurularin calismasi icin gereken minimum gercek oyuncu sayisi.</summary>
        public int MinPlayersForAds { get; set; } = 0;

        public bool SkipBots { get; set; } = true;

        /// <summary>true ise CenterHTML duyurulari yalnizca olu/izleyici oyunculara gosterilir (catisma ortasinda ekrani kapatmamak icin).</summary>
        public bool ShowHtmlWhenDeadOnly { get; set; } = false;

        public int DefaultHtmlDurationMs { get; set; } = 5000;

        public bool EnablePrefix { get; set; } = true;
        public string Prefix { get; set; } = "[gold][Duyuru][default]";

        /// <summary>Bos birakilirsa sunucunun 'hostname' convar degeri kullanilir.</summary>
        public string ServerNameOverride { get; set; } = "";
        public string ServerDomain { get; set; } = "oyun.sunucunuz.com";
        public string DiscordInvite { get; set; } = "discord.gg/sunucu";
        public string WebsiteUrl { get; set; } = "www.sunucunuz.com";
        public string GeoIpDatabasePath { get; set; } = "GeoLite2-Country.mmdb";

        public bool FreezeWhileMenuOpen { get; set; } = true;
        public bool LogToConsole { get; set; } = false;
        public bool Debug { get; set; } = false;
    }

    public class WelcomeSection
    {
        public bool EnableWelcome { get; set; } = true;
        public float WelcomeDelaySeconds { get; set; } = 3.0f;

        /// <summary>"both" (Chat + HTML), "chat", "html", "center", "alert" veya "none".</summary>
        public string PrivateChannel { get; set; } = "both";

        public List<string> PrivateChatLines { get; set; } = new()
        {
            "[gold]══════════════════════════════════════════════════[default]",
            "👋 Hoş geldin [lime]{NAME}[default] ([gold]{COUNTRY}[default])! Sunucu: [lightblue]{SERVERNAME}[default]",
            "🗺️ Harita: [gold]{MAP}[default] | 👥 Oyuncular: [lime]{PLAYERS}/{MAXPLAYERS}[default] | 💬 Discord: [lightblue]{DISCORD}[default]",
            "[gold]══════════════════════════════════════════════════[default]"
        };

        public string PrivateHtmlMessage { get; set; } =
            "<font class='fontSize-m' color='#38BDF8'><b>✦ HOŞ GELDİN {NAME} ✦</b></font><br>" +
            "<font class='fontSize-sm' color='#F8FAFC'>{SERVERNAME}</font><br>" +
            "<font class='fontSize-xs' color='#FACC15'>Harita: {MAP} • Oyuncu: {PLAYERS}/{MAXPLAYERS} • Ülke: {COUNTRY}</font><br>" +
            "<font class='fontSize-xs' color='#4ADE80'>Discord: {DISCORD} • İyi oyunlar dileriz!</font>";

        public int PrivateHtmlDurationMs { get; set; } = 6000;

        public bool EnablePublicJoin { get; set; } = true;
        public string PublicJoinMessage { get; set; } =
            "[lime]➕ {NAME}[default] ([gold]{COUNTRY}[default]) sunucuya katıldı! ([lightblue]{PLAYERS}/{MAXPLAYERS}[default])";

        public bool EnablePublicLeave { get; set; } = true;
        public string PublicLeaveMessage { get; set; } =
            "[lightred]➖ {NAME}[default] ([grey]{STEAMID2}[default]) sunucudan ayrıldı. (Oynama: [gold]{PLAYTIME}[default])";
    }

    public class CommandSection
    {
        public string MenuCommand { get; set; } = "duyurumenu";
        public string SayBroadcastCommand { get; set; } = "duyuru";
        public string CenterBroadcastCommand { get; set; } = "centerduyuru";
        public string HtmlBroadcastCommand { get; set; } = "htmlduyuru";
        public string AlertBroadcastCommand { get; set; } = "alertduyuru";
        public string CountdownCommand { get; set; } = "gerisayim";
        public string PollCommand { get; set; } = "anket";
        public string VoteCommand { get; set; } = "oy";
        public string NextAdCommand { get; set; } = "sonrakiduyuru";
        public string ReloadCommand { get; set; } = "duyurureload";
        public string AdminPermission { get; set; } = "broadcast.admin";
    }

    public static List<AdEntry> CreateDefaultAds() => new()
    {
        new AdEntry
        {
            Id = "hosgeldin_bilgi",
            Name = "Sunucu Durumu",
            Enabled = true,
            Chat = "[lime]Sunucu Durumu:[default] [gold]{PLAYERS}/{MAXPLAYERS}[default] Oyuncu | Harita: [lightblue]{MAP}[default] | Saat: [gold]{TIME}[default]",
            Html = "<font class='fontSize-m' color='#38BDF8'><b>✦ {SERVERNAME} ✦</b></font><br><font class='fontSize-sm' color='#F8FAFC'>Aktif Oyuncu: <font color='#4ADE80'><b>{PLAYERS}/{MAXPLAYERS}</b></font> • Harita: <font color='#FACC15'><b>{MAP}</b></font></font>",
            HtmlDurationMs = 4500,
            Weight = 1
        },
        new AdEntry
        {
            Id = "discord",
            Name = "Discord Topluluğu",
            Enabled = true,
            Chat = "Çekilişler, duyurular ve destek için Discord sunucumuza katılın: [lightblue]{DISCORD}[default]",
            Center = "💬 Discord: {DISCORD}",
            Weight = 1
        },
        new AdEntry
        {
            Id = "vip_tanitim",
            Name = "VIP Ayrıcalıkları",
            Enabled = true,
            Permission = "!vip",
            Chat = "[gold]★ VIP Üyelik ★[default] Özel etiketler ve ayrıcalıklar için: [lime]{WEBSITE}[default]",
            Html = "<font class='fontSize-m' color='#FACC15'><b>★ VIP AYRICALIKLARI ★</b></font><br><font class='fontSize-sm' color='#E2E8F0'>Sayın <b>{NAME}</b>, VIP avantajlarını incelemek için: <font color='#38BDF8'><b>{WEBSITE}</b></font></font>",
            HtmlDurationMs = 5000,
            Weight = 1
        },
        new AdEntry
        {
            Id = "kurallar",
            Name = "Sunucu Kuralları",
            Enabled = true,
            Chat = "[lightred]Lütfen sunucu kurallarına uyun![default] Hile, küfür ve toksik davranışlar kalıcı yasaklanma sebebidir.",
            Alert = "⚖️ Kurallara uyun ve adil oynayın! İyi eğlenceler!",
            Weight = 1
        }
    };
}

