using System.Net;
using System.Text;
using BroadcastSystem.Config;
using BroadcastSystem.Models;
using BroadcastSystem.Utils;
using Microsoft.Extensions.Logging;
using PanoramaHud.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace BroadcastSystem.Core;

/// <summary>
/// PanoramaHud tabanlı görsel BroadcastSystem yönetim menüsü (!duyurumenu).
/// Sıfır teknik bilgi ve sıfır komut bilgisine sahip yöneticilerin bile
/// hazır şablonlar, tek tıkla seçimler ve görsel butonlarla tüm duyuru ve
/// karşılama yapılandırmasını rahatça yönetebilmesini sağlar.
/// </summary>
public sealed class BroadcastPanoramaMenu : IDisposable
{
    // ============================== SELECTION PRESETS ==============================

    private static readonly float[] AdIntervalValues = [10.0f, 15.0f, 20.0f, 30.0f, 45.0f, 60.0f, 90.0f, 120.0f, 180.0f, 300.0f];
    private static readonly string[] AdIntervalLabels =
    [
        "10 saniye", "15 saniye", "20 saniye", "30 saniye", "45 saniye",
        "1 dakika", "1.5 dakika", "2 dakika", "3 dakika", "5 dakika"
    ];

    private static readonly float[] WelcomeDelayValues = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 8.0f, 10.0f];
    private static readonly string[] WelcomeDelayLabels =
    [
        "1 saniye", "2 saniye", "3 saniye", "4 saniye", "5 saniye", "8 saniye", "10 saniye"
    ];

    private static readonly int[] HtmlDurationValues = [2000, 3000, 4000, 5000, 6000, 8000, 10000, 15000];
    private static readonly string[] HtmlDurationLabels =
    [
        "2 saniye", "3 saniye", "4 saniye", "5 saniye", "6 saniye", "8 saniye", "10 saniye", "15 saniye"
    ];

    private static readonly int[] MinPlayersValues = [0, 1, 2, 4, 6, 8, 10, 12, 16];
    private static readonly string[] MinPlayersLabels =
    [
        "0 (Her Zaman)", "1 Oyuncu", "2 Oyuncu", "4 Oyuncu", "6 Oyuncu",
        "8 Oyuncu", "10 Oyuncu", "12 Oyuncu", "16 Oyuncu"
    ];

    private static readonly int[] WeightValues = [1, 2, 3, 5, 10];
    private static readonly string[] WeightLabels =
    [
        "1 (Normal)", "2 (2x Sık)", "3 (3x Sık)", "5 (5x Çok Sık)", "10 (En Yüksek)"
    ];

    private static readonly string[] ChannelValues = ["both", "chat", "html", "center", "alert"];
    private static readonly string[] ChannelLabels =
    [
        "Chat + HTML", "Sadece Chat", "Sadece HTML", "Sadece Center", "Sadece Alert"
    ];

    private static readonly string[][] TeamPresets =
    [
        [],
        ["T"],
        ["CT"],
        ["SPEC"]
    ];
    private static readonly string[] TeamPresetLabels =
    [
        "Herkes", "Sadece T", "Sadece CT", "Sadece İzleyiciler (SPEC)"
    ];

    private static readonly string[] RotationModeValues = ["sequential", "random"];
    private static readonly string[] RotationModeLabels = ["Sıralı (Sequential)", "Ağırlıklı Rastgele (Random)"];

    private static readonly string[] PrefixPresets =
    [
        "[gold][Duyuru][default]",
        "[lime][Sunucu][default]",
        "[lightblue][CS2][default]",
        "[lightred][DUYURU][default]",
        "[purple][VIP][default]",
        "[cyan][Topluluk][default]",
        "[white]★ [gold]ANONS[white] ★[default]"
    ];

    // ============================== FIELDS ==============================

    private readonly ISwiftlyCore _core;
    private readonly IHudScope _hud;
    private readonly IPanoramaHudApi _api;
    private readonly ConfigLoader _loader;
    private readonly BroadcastService _service;
    private readonly Action _onTimerSettingsChanged;
    private PluginConfig _config;

    private readonly Dictionary<int, (string Prompt, Action<IPlayer, string> Callback, Action<IPlayer>? Reopen)> _pendingInput = new();
    private readonly HashSet<int> _openMenus = new();

    // Hızlı anons sayfası seçim durumları (admin slot bazlı veya genel)
    private int _instantChannelIdx = 0; // both
    private int _instantTeamIdx = 0;    // Herkes
    private bool _instantUsePrefix = true;

    // Menüler
    private IMenu? _mainMenu;
    private IMenu? _adsMenu;
    private IMenu? _templateWizardMenu;
    private IMenu? _welcomeMenu;
    private IMenu? _settingsMenu;
    private IMenu? _instantBroadcastMenu;
    private IMenu? _pollCountdownMenu;
    private IMenu? _guideMenu;

    public bool IsAvailable => _hud != null;

    public BroadcastPanoramaMenu(
        ISwiftlyCore core,
        IHudScope hud,
        IPanoramaHudApi api,
        ConfigLoader loader,
        PluginConfig config,
        BroadcastService service,
        Action onTimerSettingsChanged)
    {
        _core = core;
        _hud = hud;
        _api = api;
        _loader = loader;
        _config = config;
        _service = service;
        _onTimerSettingsChanged = onTimerSettingsChanged;

        _api.MenuOpened += OnMenuOpened;
        _api.MenuClosed += OnMenuClosed;
    }

    public void UpdateConfig(PluginConfig config)
    {
        _config = config;
        BuildMenus();
    }

    public void Dispose()
    {
        _api.MenuOpened -= OnMenuOpened;
        _api.MenuClosed -= OnMenuClosed;
        _pendingInput.Clear();
        _openMenus.Clear();
    }

    public bool IsMenuOpen(int slot) => _openMenus.Contains(slot);

    private void OnMenuOpened(IPlayer player, string menuId)
    {
        if (menuId.StartsWith("broadcastsystem", StringComparison.OrdinalIgnoreCase))
        {
            _openMenus.Add(player.Slot);
        }
    }

    private void OnMenuClosed(IPlayer player, string menuId, CloseReason reason)
    {
        if (menuId.StartsWith("broadcastsystem", StringComparison.OrdinalIgnoreCase))
        {
            _openMenus.Remove(player.Slot);
        }
    }

    public void OnPlayerDisconnected(int slot)
    {
        _pendingInput.Remove(slot);
        _openMenus.Remove(slot);
    }

    public void OnMapLoad()
    {
        _pendingInput.Clear();
        _openMenus.Clear();
    }

    // ============================== CONFIG SAVE & APPLY ==============================

    private void SaveAndApply(bool restartTimers = false)
    {
        try
        {
            _loader.Save(_config);
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "[BroadcastSystem] Config kaydedilemedi.");
        }

        _service.UpdateConfig(_config);
        if (restartTimers)
            _onTimerSettingsChanged();
    }

    private void SuccessToast(IPlayer player, string message, string? key = null)
    {
        _hud.Toast(player, new ToastOptions
        {
            Title = "BroadcastSystem",
            Text = message,
            Kind = ToastKind.Success,
            Duration = TimeSpan.FromSeconds(2.5),
            Key = key ?? "broadcast.toast"
        });
    }

    private void InfoToast(IPlayer player, string message, string? key = null)
    {
        _hud.Toast(player, new ToastOptions
        {
            Title = "BroadcastSystem",
            Text = message,
            Kind = ToastKind.Info,
            Duration = TimeSpan.FromSeconds(2.5),
            Key = key ?? "broadcast.toast"
        });
    }

    private void WarningToast(IPlayer player, string message, string? key = null)
    {
        _hud.Toast(player, new ToastOptions
        {
            Title = "BroadcastSystem",
            Text = message,
            Kind = ToastKind.Warning,
            Duration = TimeSpan.FromSeconds(3.0),
            Key = key ?? "broadcast.toast"
        });
    }

    // ============================== CHAT INPUT HANDLER ==============================

    public bool TryConsumeChatInput(int playerId, string text)
    {
        if (!_pendingInput.TryGetValue(playerId, out var pending)) return false;

        var admin = _core.PlayerManager.GetPlayer(playerId);
        if (!PlayerHelper.IsUsable(admin))
        {
            _pendingInput.Remove(playerId);
            return false;
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();

        _pendingInput.Remove(playerId);

        if (trimmed.Equals("iptal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("!iptal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            _service.Reply(admin!, "[lightred]Giriş iptal edildi.[default]");
            WarningToast(admin!, "İşlem iptal edildi.");
            pending.Reopen?.Invoke(admin!);
            return true;
        }

        pending.Callback(admin!, trimmed);
        pending.Reopen?.Invoke(admin!);
        return true;
    }

    private void PromptInput(IPlayer admin, string prompt, Action<IPlayer, string> callback, Action<IPlayer>? reopen = null)
    {
        _pendingInput[admin.Slot] = (prompt, callback, reopen);
        _api.CloseModal(admin);

        _service.Reply(admin, $"[gold]💬 Sohbete yazın:[default] [yellow]{prompt}[default]");
        _service.Reply(admin, "İptal etmek için sohbete [lightred]iptal[default] yazabilirsiniz.");

        _hud.Toast(admin, new ToastOptions
        {
            Title = "Giriş Bekleniyor",
            Text = "Oyun içi sohbete (Chat) yeni metni yazın.\nİptal için 'iptal' yazabilirsiniz.",
            Kind = ToastKind.Info,
            Duration = TimeSpan.FromSeconds(6.0),
            Key = "broadcast.prompt"
        });
    }

    // ============================== BUILD ALL MENUS ==============================

    public void BuildMenus()
    {
        _mainMenu = BuildMainMenu();
        _adsMenu = BuildAdsMenu();
        _templateWizardMenu = BuildTemplateWizardMenu();
        _welcomeMenu = BuildWelcomeMenu();
        _settingsMenu = BuildSettingsMenu();
        _instantBroadcastMenu = BuildInstantBroadcastMenu();
        _pollCountdownMenu = BuildPollCountdownMenu();
        _guideMenu = BuildGuideMenu();
    }

    public void Open(IPlayer admin)
    {
        if (_mainMenu == null) BuildMenus();
        _mainMenu?.Open(admin);
    }

    // ============================== 1. ANA MENÜ ==============================

    private IMenu BuildMainMenu()
    {
        return _hud.Menu("broadcastsystem.main", "📢 BroadcastSystem Yönetim")
            .Description(p =>
            {
                int active = _config.Ads.Count(a => a.Enabled);
                string rot = _config.Settings.RotationMode == "random" ? "Rastgele" : "Sıralı";
                return $"{active}/{_config.Ads.Count} aktif duyuru • Aralık: {_config.Settings.AdIntervalSeconds:0}sn • Döngü: {rot}";
            })
            .Button("📋 Zamanlı Duyuruları Yönet", ctx => _adsMenu?.Open(ctx.Player))
            .Button("➕ Yeni Duyuru Sihirbazı (Hazır Şablonlar)", ctx => _templateWizardMenu?.Open(ctx.Player))
            .Button("📢 Hızlı Anons & Canlı Yayınla", ctx => _instantBroadcastMenu?.Open(ctx.Player))
            .Button("👋 Hoş Geldin & Karşılama Ayarları", ctx => _welcomeMenu?.Open(ctx.Player))
            .Button("📊 Canlı Anket & Geri Sayım", ctx => _pollCountdownMenu?.Open(ctx.Player))
            .Button("⚙️ Genel Sunucu Yapılandırması", ctx => _settingsMenu?.Open(ctx.Player))
            .Button("ℹ️ Değişkenler & Kılavuz", ctx => _guideMenu?.Open(ctx.Player))
            .Button("⏭️ Sıradaki Duyuruyu Hemen Tetikle", ctx =>
            {
                if (_service.BroadcastNextAd(ignoreConditions: true))
                {
                    SuccessToast(ctx.Player, "[lime]Sıradaki zamanlı duyuru yayınlandı![/]");
                }
                else
                {
                    WarningToast(ctx.Player, "Yayınlanabilecek aktif duyuru bulunamadı.");
                }
            })
            .Button("❌ Menüyü Kapat", ctx => ctx.Close())
            .Build();
    }

    // ============================== 2. ZAMANLI DUYURULAR LİSTESİ ==============================

    private IMenu BuildAdsMenu()
    {
        return _hud.Menu("broadcastsystem.ads", "📋 Zamanlı Duyurular")
            .Description(p => $"Kayıtlı {_config.Ads.Count} duyuru. Düzenlemek veya test etmek için seçin:")
            .Button("➕ Yeni Duyuru Sihirbazı (Hazır Şablonlar)", ctx => _templateWizardMenu?.Open(ctx.Player))
            .Button("⏭️ Sıradaki Duyuruyu Hemen Yayınla", ctx =>
            {
                if (_service.BroadcastNextAd(ignoreConditions: true))
                    SuccessToast(ctx.Player, "Sıradaki duyuru yayınlandı!");
                else
                    WarningToast(ctx.Player, "Aktif duyuru bulunamadı.");
            })
            .Items((admin, b) =>
            {
                if (_config.Ads.Count == 0)
                {
                    b.Info("Kayıtlı zamanlı duyuru bulunamadı.");
                    return;
                }

                foreach (var ad in _config.Ads.ToList())
                {
                    var entry = ad;
                    string status = entry.Enabled ? "✓ AKTİF" : "✗ KAPALI";
                    string label = $"[{status}] {entry.Label} ({entry.ChannelsSummary()})";

                    b.Button(label, ctx => OpenAdEditMenu(ctx.Player, entry));
                }
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ============================== 3. HAZIR ŞABLONLAR SİHİRBAZI ==============================

    private IMenu BuildTemplateWizardMenu()
    {
        return _hud.Menu("broadcastsystem.wizard", "➕ Yeni Duyuru Sihirbazı")
            .Description("Sıfır teknik bilgi ile tek tıkla profesyonel duyuru oluşturun:")
            .Button("💬 Discord Topluluğu Şablonu", ctx =>
            {
                var ad = new AdEntry
                {
                    Id = "discord_" + DateTime.Now.ToString("HHmmss"),
                    Name = "Discord Topluluğu",
                    Enabled = true,
                    Chat = "💬 [lightblue]Topluluğumuza katılın, etkinlik ve çekilişleri kaçırmayın:[default] [gold]{DISCORD}[default]",
                    Center = "💬 Discord: {DISCORD}",
                    Html = "<font class='fontSize-m' color='#38BDF8'><b>✦ TOPLULUĞUMUZA KATILIN ✦</b></font><br><font class='fontSize-sm' color='#F8FAFC'>Çekilişler, etkinlikler ve destek için:</font><br><font class='fontSize-s' color='#4ADE80'><b>{DISCORD}</b></font>",
                    HtmlDurationMs = 5000,
                    Weight = 1
                };
                AddAndOpenNewAd(ctx.Player, ad);
            })
            .Button("⭐ VIP & Destekçi Ayrıcalıkları Şablonu", ctx =>
            {
                var ad = new AdEntry
                {
                    Id = "vip_" + DateTime.Now.ToString("HHmmss"),
                    Name = "VIP Ayrıcalıkları",
                    Enabled = true,
                    Permission = "!vip",
                    Chat = "★ [gold]VIP Ayrıcalıkları[default] ve özel ödüller için web sitemizi ziyaret edin: [lime]{WEBSITE}[default]",
                    Html = "<font class='fontSize-m' color='#FACC15'><b>★ VIP AYRICALIKLARI ★</b></font><br><font class='fontSize-sm' color='#E2E8F0'>Sayın <b>{NAME}</b>, avantajları incelemek için: <font color='#38BDF8'><b>{WEBSITE}</b></font></font>",
                    HtmlDurationMs = 5000,
                    Weight = 1
                };
                AddAndOpenNewAd(ctx.Player, ad);
            })
            .Button("⚖️ Sunucu Kuralları & Saygı Şablonu", ctx =>
            {
                var ad = new AdEntry
                {
                    Id = "kurallar_" + DateTime.Now.ToString("HHmmss"),
                    Name = "Sunucu Kuralları",
                    Enabled = true,
                    Chat = "[lightred]Lütfen sunucu kurallarına uyun![default] Hile, küfür ve saygısızlık kalıcı yasaklanma sebebidir.",
                    Alert = "⚖️ Sunucu kurallarına uyunuz ve adil oynayınız!",
                    Weight = 1
                };
                AddAndOpenNewAd(ctx.Player, ad);
            })
            .Button("🗺️ Sunucu & Harita Canlı Durum Şablonu", ctx =>
            {
                var ad = new AdEntry
                {
                    Id = "durum_" + DateTime.Now.ToString("HHmmss"),
                    Name = "Sunucu Durumu",
                    Enabled = true,
                    Chat = "[lime]Sunucu:[default] [gold]{SERVERNAME}[default] | Harita: [lightblue]{MAP}[default] | Oyuncu: [lime]{PLAYERS}/{MAXPLAYERS}[default] | Saat: [gold]{TIME}[default]",
                    Html = "<font class='fontSize-m' color='#38BDF8'><b>✦ {SERVERNAME} ✦</b></font><br><font class='fontSize-sm' color='#F8FAFC'>Harita: <font color='#FACC15'><b>{MAP}</b></font> • Oyuncu: <font color='#4ADE80'><b>{PLAYERS}/{MAXPLAYERS}</b></font></font>",
                    HtmlDurationMs = 4500,
                    Weight = 1
                };
                AddAndOpenNewAd(ctx.Player, ad);
            })
            .Button("🛒 Kredi & Web Mağazası Şablonu", ctx =>
            {
                var ad = new AdEntry
                {
                    Id = "market_" + DateTime.Now.ToString("HHmmss"),
                    Name = "Sunucu Mağazası",
                    Enabled = true,
                    Chat = "🛒 [gold]Kredi yüklemek ve market alışverişi için:[default] [lightblue]{WEBSITE}[default]",
                    Html = "<font class='fontSize-m' color='#4ADE80'><b>🛒 SUNUCU MAĞAZASI 🛒</b></font><br><font class='fontSize-sm' color='#F8FAFC'>Kredi yüklemek ve kasa açmak için: <font color='#FACC15'><b>{WEBSITE}</b></font></font>",
                    HtmlDurationMs = 5000,
                    Weight = 1
                };
                AddAndOpenNewAd(ctx.Player, ad);
            })
            .Button("👥 Steam Grubu Tanıtım Şablonu", ctx =>
            {
                var ad = new AdEntry
                {
                    Id = "steam_" + DateTime.Now.ToString("HHmmss"),
                    Name = "Steam Grubu",
                    Enabled = true,
                    Chat = "👥 [lightblue]Steam grubumuza katılarak özel tag ve sürpriz bonuslar kazanın![default]",
                    Center = "👥 Steam Grubumuza Katılın!",
                    Weight = 1
                };
                AddAndOpenNewAd(ctx.Player, ad);
            })
            .Button("🎮 Özel Duyuru (Sıfırdan Boş Oluştur)", ctx =>
            {
                PromptInput(ctx.Player, "Yeni duyurunun başlığını yazın (örn: Turnuva Duyurusu):", (adm, name) =>
                {
                    var ad = new AdEntry
                    {
                        Id = "ad_" + (_config.Ads.Count + 1) + "_" + DateTime.Now.ToString("HHmmss"),
                        Name = name.Trim(),
                        Enabled = true,
                        Chat = $"[gold][Duyuru][default] [lime]{name.Trim()}[default]",
                        Weight = 1
                    };
                    AddAndOpenNewAd(adm, ad);
                }, reopen: adm => _templateWizardMenu?.Open(adm));
            })
            .Button("⬅️ Duyurulara Dön", ctx => _adsMenu?.Open(ctx.Player))
            .Build();
    }

    private void AddAndOpenNewAd(IPlayer player, AdEntry ad)
    {
        _config.Ads.Add(ad);
        SaveAndApply();
        SuccessToast(player, $"'{ad.Label}' oluşturuldu ve kaydedildi!");
        OpenAdEditMenu(player, ad);
    }

    // ============================== 4. DUYURU DÜZENLEME MENÜSÜ ==============================

    public void OpenAdEditMenu(IPlayer admin, AdEntry ad)
    {
        var builder = _hud.Menu($"broadcastsystem.ad.{ad.Id}", $"✏️ {ad.Label}")
            .Description(p => $"ID: {ad.Id} • Kanallar: {ad.ChannelsSummary()} • Ağırlık: {ad.Weight}")

            // 1. Durum Toggle
            .Toggle("Duyuru Durumu",
                _ => ad.Enabled,
                (ctx, v) =>
                {
                    ad.Enabled = v;
                    SaveAndApply();
                    SuccessToast(ctx.Player, ad.Enabled ? "Duyuru aktif edildi." : "Duyuru kapatıldı.");
                    ctx.Refresh();
                })

            // 2. Anında Test & Yayınla
            .Button("🚀 Tüm Sunucuya Şimdi Yayınla", ctx =>
            {
                _service.SendAd(ad);
                SuccessToast(ctx.Player, $"'{ad.Label}' tüm sunucuya yayınlandı!");
            })
            .Button("👁️ Sadece Bana Önizle (Test)", ctx =>
            {
                _service.SendAd(ad, singlePreviewTarget: ctx.Player);
                InfoToast(ctx.Player, $"'{ad.Label}' önizlemesi ekranınıza gönderildi.");
            })

            // 3. Başlık Değiştir
            .Button(p => $"✏️ Başlık: {ad.Label}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni duyuru başlığını yazın:", (adm, val) =>
                {
                    ad.Name = val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, $"Başlık '{ad.Name}' olarak güncellendi.");
                }, reopen: adm => OpenAdEditMenu(adm, ad));
            })

            // 4. Chat Mesajı Düzenleyici Submenu
            .Submenu("💬 Chat Mesajı", sub => sub
                .Description(_ => $"Mevcut: {ShortPreview(ad.Chat)}")
                .Button("✏️ Yeni Chat Mesajı Yaz (Sohbetten)", ctx =>
                {
                    PromptInput(ctx.Player, "Yeni Chat mesajını yazın ({NAME}, {MAP}, {DISCORD} vb. kullanılabilir):", (adm, val) =>
                    {
                        ad.Chat = val.Trim();
                        SaveAndApply();
                        SuccessToast(adm, "Chat mesajı güncellendi.");
                    }, reopen: adm => OpenAdEditMenu(adm, ad));
                })
                .Button("🟢 Yeşil Renk Şablonu Yap ([lime])", ctx =>
                {
                    var text = string.IsNullOrWhiteSpace(ad.Chat) ? ad.Label : ColorHelper.StripAll(ad.Chat);
                    ad.Chat = $"[lime]{text}[default]";
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Chat mesajı yeşil renkli yapıldı.");
                    ctx.Refresh();
                })
                .Button("🟡 Altın Sarı Renk Şablonu Yap ([gold])", ctx =>
                {
                    var text = string.IsNullOrWhiteSpace(ad.Chat) ? ad.Label : ColorHelper.StripAll(ad.Chat);
                    ad.Chat = $"[gold]{text}[default]";
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Chat mesajı altın sarısı yapıldı.");
                    ctx.Refresh();
                })
                .Button("🌐 Açık Mavi Renk Şablonu Yap ([lightblue])", ctx =>
                {
                    var text = string.IsNullOrWhiteSpace(ad.Chat) ? ad.Label : ColorHelper.StripAll(ad.Chat);
                    ad.Chat = $"[lightblue]{text}[default]";
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Chat mesajı açık mavi yapıldı.");
                    ctx.Refresh();
                })
                .Button("🗑️ Chat Mesajını Kaldır (Boş Bırak)", ctx =>
                {
                    ad.Chat = null;
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Chat mesajı kaldırıldı.");
                    ctx.Refresh();
                }))

            // 5. CenterHTML Mesajı Düzenleyici Submenu
            .Submenu("🖥️ CenterHTML Mesajı", sub => sub
                .Description(_ => $"Mevcut: {ShortPreview(ad.Html)}")
                .Button("✏️ Yeni HTML Mesajı Yaz (Sohbetten)", ctx =>
                {
                    PromptInput(ctx.Player, "Yeni CenterHTML kodunu yazın:", (adm, val) =>
                    {
                        ad.Html = val.Trim();
                        SaveAndApply();
                        SuccessToast(adm, "CenterHTML mesajı güncellendi.");
                    }, reopen: adm => OpenAdEditMenu(adm, ad));
                })
                .Button("✦ Şık CS2 Banner Şablonu Oluştur", ctx =>
                {
                    var cleanTitle = WebUtility.HtmlEncode(ad.Label);
                    ad.Html = $"<font class='fontSize-m' color='#38BDF8'><b>✦ {cleanTitle} ✦</b></font><br>" +
                              $"<font class='fontSize-sm' color='#F8FAFC'>Detaylı bilgi için: <font color='#4ADE80'><b>{{DISCORD}}</b></font></font>";
                    SaveAndApply();
                    SuccessToast(ctx.Player, "CenterHTML banner şablonu uygulandı!");
                    ctx.Refresh();
                })
                .Button("★ VIP / Altın Kart Şablonu Oluştur", ctx =>
                {
                    var cleanTitle = WebUtility.HtmlEncode(ad.Label);
                    ad.Html = $"<font class='fontSize-m' color='#FACC15'><b>★ {cleanTitle} ★</b></font><br>" +
                              $"<font class='fontSize-sm' color='#E2E8F0'>Özel ayrıcalıklar: <font color='#38BDF8'><b>{{WEBSITE}}</b></font></font>";
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Altın kart HTML şablonu uygulandı!");
                    ctx.Refresh();
                })
                .Button("🗑️ HTML Mesajını Kaldır (Boş Bırak)", ctx =>
                {
                    ad.Html = null;
                    SaveAndApply();
                    SuccessToast(ctx.Player, "CenterHTML mesajı kaldırıldı.");
                    ctx.Refresh();
                }))

            // 6. Center & Alert Mesajları
            .Submenu("🎯 Center & Alert Mesajları", sub => sub
                .Description(_ => $"Center: {ShortPreview(ad.Center)} • Alert: {ShortPreview(ad.Alert)}")
                .Button("✏️ Center (Orta) Mesajı Yaz", ctx =>
                {
                    PromptInput(ctx.Player, "Yeni Center mesajını yazın:", (adm, val) =>
                    {
                        ad.Center = val.Trim();
                        SaveAndApply();
                        SuccessToast(adm, "Center mesajı güncellendi.");
                    }, reopen: adm => OpenAdEditMenu(adm, ad));
                })
                .Button("🗑️ Center Mesajını Temizle", ctx =>
                {
                    ad.Center = null;
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Center mesajı kaldırıldı.");
                    ctx.Refresh();
                })
                .Button("✏️ Alert (Kutu) Mesajı Yaz", ctx =>
                {
                    PromptInput(ctx.Player, "Yeni Alert mesajını yazın:", (adm, val) =>
                    {
                        ad.Alert = val.Trim();
                        SaveAndApply();
                        SuccessToast(adm, "Alert mesajı güncellendi.");
                    }, reopen: adm => OpenAdEditMenu(adm, ad));
                })
                .Button("🗑️ Alert Mesajını Temizle", ctx =>
                {
                    ad.Alert = null;
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Alert mesajı kaldırıldı.");
                    ctx.Refresh();
                }))

            // 7. HTML Süresi
            .Select("⏱️ HTML Gösterim Süresi",
                HtmlDurationLabels,
                _ => GetDurationIndex(ad.HtmlDurationMs ?? _config.Settings.DefaultHtmlDurationMs),
                (ctx, idx) =>
                {
                    ad.HtmlDurationMs = HtmlDurationValues[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"HTML süresi {HtmlDurationLabels[idx]} yapıldı.");
                    ctx.Refresh();
                })

            // 8. Ön Ek (Prefix) Seçimi
            .Select("🏷️ Ön Ek (Prefix)",
                ["Genel Ayar (Global)", "Zorla Açık", "Zorla Kapalı"],
                _ => ad.UsePrefix switch { true => 1, false => 2, _ => 0 },
                (ctx, idx) =>
                {
                    ad.UsePrefix = idx switch { 1 => true, 2 => false, _ => null };
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Ön ek ayarı güncellendi.");
                    ctx.Refresh();
                })

            // 9. Takım Filtresi
            .Select("🚩 Hedef Takım",
                TeamPresetLabels,
                _ => GetTeamPresetIndex(ad.Teams),
                (ctx, idx) =>
                {
                    ad.Teams = TeamPresets[idx].ToList();
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Takım filtresi: {TeamPresetLabels[idx]}");
                    ctx.Refresh();
                })

            // 10. Yetki Filtresi
            .Select("🔑 Yetki Filtresi",
                ["Herkes (Filtre Yok)", "Sadece VIP (vip)", "VIP Olmayanlar (!vip)", "Sadece Admin (broadcast.admin)", "Özel Yetki..."],
                _ => GetPermissionIndex(ad.Permission),
                (ctx, idx) =>
                {
                    if (idx == 4)
                    {
                        PromptInput(ctx.Player, "Yetki kodunu yazın (örn: vip, !vip, root veya kaldırmak için -):", (adm, val) =>
                        {
                            ad.Permission = val == "-" ? null : val.Trim();
                            SaveAndApply();
                            SuccessToast(adm, $"Yetki filtresi: {ad.Permission ?? "Herkes"}");
                        }, reopen: adm => OpenAdEditMenu(adm, ad));
                        return;
                    }

                    ad.Permission = idx switch
                    {
                        1 => "vip",
                        2 => "!vip",
                        3 => _config.Commands.AdminPermission,
                        _ => null
                    };
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Yetki filtresi: {ad.Permission ?? "Herkes"}");
                    ctx.Refresh();
                })

            // 11. Döngü Ağırlığı
            .Select("⚖️ Döngü Ağırlığı (Weight)",
                WeightLabels,
                _ => GetWeightIndex(ad.Weight),
                (ctx, idx) =>
                {
                    ad.Weight = WeightValues[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Ağırlık: {WeightLabels[idx]}");
                    ctx.Refresh();
                })

            // 12. Duyuruyu Sil Butonu (Diyalog ile güvenli)
            .Button("🗑️ Bu Duyuruyu Sil", ctx =>
            {
                _hud.Dialog(ctx.Player, "Duyuruyu Sil?", $"'{ad.Label}' duyurusunu silmek istediğinizden emin misiniz?")
                    .Button("Evet, Sil", c =>
                    {
                        _config.Ads.Remove(ad);
                        SaveAndApply();
                        SuccessToast(c.Player, $"'{ad.Label}' silindi.");
                        _adsMenu?.Open(c.Player);
                    }, ButtonStyle.Danger)
                    .Button("İptal", c => OpenAdEditMenu(c.Player, ad), ButtonStyle.Secondary)
                    .Open();
            })

            .Button("⬅️ Duyuru Listesine Dön", ctx => _adsMenu?.Open(ctx.Player));

        builder.Build().Open(admin);
    }

    // ============================== 5. HOŞ GELDİN & KARŞILAMA AYARLARI ==============================

    private IMenu BuildWelcomeMenu()
    {
        var w = _config.Welcome;

        return _hud.Menu("broadcastsystem.welcome", "👋 Hoş Geldin & Karşılama")
            .Description("Oyuncu sunucuya girdiğinde ve çıktığında yapılan bildirimler:")

            // Özel Hoş Geldin Toggle
            .Toggle("Özel Hoş Geldin Mesajı",
                _ => _config.Welcome.EnableWelcome,
                (ctx, v) =>
                {
                    _config.Welcome.EnableWelcome = v;
                    SaveAndApply();
                    SuccessToast(ctx.Player, v ? "Hoş geldin mesajı aktif edildi." : "Hoş geldin mesajı kapatıldı.");
                    ctx.Refresh();
                })

            // Hoş Geldin Kanalı
            .Select("Hoş Geldin Kanalı",
                ChannelLabels,
                _ => GetChannelIndex(_config.Welcome.PrivateChannel),
                (ctx, idx) =>
                {
                    _config.Welcome.PrivateChannel = ChannelValues[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Kanal {ChannelLabels[idx]} yapıldı.");
                    ctx.Refresh();
                })

            // Gecikme Süresi
            .Select("Karşılama Gecikmesi",
                WelcomeDelayLabels,
                _ => GetWelcomeDelayIndex(_config.Welcome.WelcomeDelaySeconds),
                (ctx, idx) =>
                {
                    _config.Welcome.WelcomeDelaySeconds = WelcomeDelayValues[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Gecikme {WelcomeDelayLabels[idx]} yapıldı.");
                    ctx.Refresh();
                })

            // Herkese Katılma Bildirimi
            .Toggle("Herkese Katılma Bildirimi (Join)",
                _ => _config.Welcome.EnablePublicJoin,
                (ctx, v) =>
                {
                    _config.Welcome.EnablePublicJoin = v;
                    SaveAndApply();
                    SuccessToast(ctx.Player, v ? "Katılma bildirimi açıldı." : "Katılma bildirimi kapatıldı.");
                    ctx.Refresh();
                })

            .Button(p => $"✏️ Katılma Mesajı: {ShortPreview(_config.Welcome.PublicJoinMessage)}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni katılma mesajını yazın ({NAME}, {COUNTRY}, {PLAYERS} vb.):", (adm, val) =>
                {
                    _config.Welcome.PublicJoinMessage = val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, "Katılma mesajı güncellendi.");
                }, reopen: adm => _welcomeMenu?.Open(adm));
            })

            // Herkese Ayrılma Bildirimi
            .Toggle("Herkese Ayrılma Bildirimi (Leave)",
                _ => _config.Welcome.EnablePublicLeave,
                (ctx, v) =>
                {
                    _config.Welcome.EnablePublicLeave = v;
                    SaveAndApply();
                    SuccessToast(ctx.Player, v ? "Ayrılma bildirimi açıldı." : "Ayrılma bildirimi kapatıldı.");
                    ctx.Refresh();
                })

            .Button(p => $"✏️ Ayrılma Mesajı: {ShortPreview(_config.Welcome.PublicLeaveMessage)}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni ayrılma mesajını yazın ({NAME}, {STEAMID2}, {PLAYTIME} vb.):", (adm, val) =>
                {
                    _config.Welcome.PublicLeaveMessage = val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, "Ayrılma mesajı güncellendi.");
                }, reopen: adm => _welcomeMenu?.Open(adm));
            })

            // Hazır Karşılama Şablonları
            .Submenu("🎨 Hazır Karşılama Şablonları", sub => sub
                .Description("Tek tıkla modern karşılama stillerini uygulayın:")
                .Button("1. Modern Renkli Banner (HTML + Çerçeveli Chat)", ctx =>
                {
                    _config.Welcome.PrivateChannel = "both";
                    _config.Welcome.PrivateChatLines = new()
                    {
                        "[gold]══════════════════════════════════════════════════[default]",
                        "👋 Hoş geldin [lime]{NAME}[default] ([gold]{COUNTRY}[default])! Sunucu: [lightblue]{SERVERNAME}[default]",
                        "🗺️ Harita: [gold]{MAP}[default] | 👥 Oyuncular: [lime]{PLAYERS}/{MAXPLAYERS}[default] | 💬 Discord: [lightblue]{DISCORD}[default]",
                        "[gold]══════════════════════════════════════════════════[default]"
                    };
                    _config.Welcome.PrivateHtmlMessage =
                        "<font class='fontSize-m' color='#38BDF8'><b>✦ HOŞ GELDİN {NAME} ✦</b></font><br>" +
                        "<font class='fontSize-sm' color='#F8FAFC'>{SERVERNAME}</font><br>" +
                        "<font class='fontSize-xs' color='#FACC15'>Harita: {MAP} • Oyuncu: {PLAYERS}/{MAXPLAYERS} • Ülke: {COUNTRY}</font><br>" +
                        "<font class='fontSize-xs' color='#4ADE80'>Discord: {DISCORD} • İyi oyunlar dileriz!</font>";
                    _config.Welcome.PrivateHtmlDurationMs = 6000;
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Modern Renkli Banner şablonu uygulandı!");
                    ctx.Refresh();
                })
                .Button("2. Sade ve Zarif Chat (Tek Satır Minimalist)", ctx =>
                {
                    _config.Welcome.PrivateChannel = "chat";
                    _config.Welcome.PrivateChatLines = new()
                    {
                        "👋 [lime]Hoş geldin [gold]{NAME}[default] ([lightblue]{COUNTRY}[default])! Sunucumuzda iyi oyunlar dileriz. Discord: [lightblue]{DISCORD}[default]"
                    };
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Sade Chat şablonu uygulandı!");
                    ctx.Refresh();
                })
                .Button("3. Görsel CS2 Kartı (Büyük HTML Banner)", ctx =>
                {
                    _config.Welcome.PrivateChannel = "html";
                    _config.Welcome.PrivateHtmlMessage =
                        "<font class='fontSize-lg' color='#FACC15'><b>★ HOŞ GELDİN {NAME} ★</b></font><br>" +
                        "<font class='fontSize-m' color='#38BDF8'><b>{SERVERNAME}</b></font><br>" +
                        "<font class='fontSize-sm' color='#FFFFFF'>Harita: <font color='#4ADE80'>{MAP}</font> • Oyuncu: <font color='#4ADE80'>{PLAYERS}/{MAXPLAYERS}</font></font>";
                    _config.Welcome.PrivateHtmlDurationMs = 5000;
                    SaveAndApply();
                    SuccessToast(ctx.Player, "Görsel CS2 Kartı şablonu uygulandı!");
                    ctx.Refresh();
                }))

            // Kendinde Test Et Butonu
            .Button("🧪 Hoş Geldin Mesajını Kendimde Test Et", ctx =>
            {
                _service.SendWelcomeToPlayer(ctx.Player, forceEvenIfDisabled: true);
                InfoToast(ctx.Player, "Hoş geldin mesajı kendinize test edildi.");
            })

            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ============================== 6. GENEL YAPILANDIRMA MENÜSÜ ==============================

    private IMenu BuildSettingsMenu()
    {
        var s = _config.Settings;

        return _hud.Menu("broadcastsystem.settings", "⚙️ Genel Yapılandırma")
            .Description("Zamanlayıcı, ön ekler ve genel sunucu değişkenleri:")

            // Otomatik Duyurular Toggle
            .Toggle("Otomatik Zamanlı Duyurular",
                _ => _config.Settings.EnableAds,
                (ctx, v) =>
                {
                    _config.Settings.EnableAds = v;
                    SaveAndApply(restartTimers: true);
                    SuccessToast(ctx.Player, v ? "Otomatik duyurular açıldı." : "Otomatik duyurular kapatıldı.");
                    ctx.Refresh();
                })

            // Duyuru Aralığı
            .Select("Duyuru Gönderim Aralığı",
                AdIntervalLabels,
                _ => GetAdIntervalIndex(_config.Settings.AdIntervalSeconds),
                (ctx, idx) =>
                {
                    _config.Settings.AdIntervalSeconds = AdIntervalValues[idx];
                    SaveAndApply(restartTimers: true);
                    SuccessToast(ctx.Player, $"Duyuru aralığı {AdIntervalLabels[idx]} yapıldı.");
                    ctx.Refresh();
                })

            // Sıralama Modu
            .Select("Döngü Sıralama Modu",
                RotationModeLabels,
                _ => _config.Settings.RotationMode == "random" ? 1 : 0,
                (ctx, idx) =>
                {
                    _config.Settings.RotationMode = RotationModeValues[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Döngü modu: {RotationModeLabels[idx]}");
                    ctx.Refresh();
                })

            // Ön Ek Toggle
            .Toggle("Duyuru Ön Eki (Prefix)",
                _ => _config.Settings.EnablePrefix,
                (ctx, v) =>
                {
                    _config.Settings.EnablePrefix = v;
                    SaveAndApply();
                    SuccessToast(ctx.Player, v ? "Ön ek açıldı." : "Ön ek kapatıldı.");
                    ctx.Refresh();
                })

            // Hazır Ön Ek Seçimi
            .Select("🎨 Hazır Ön Ek Şablonu",
                PrefixPresets,
                _ => GetPrefixIndex(_config.Settings.Prefix),
                (ctx, idx) =>
                {
                    _config.Settings.Prefix = PrefixPresets[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Ön ek: {_config.Settings.Prefix}");
                    ctx.Refresh();
                })

            // Özel Ön Ek Yaz
            .Button(p => $"✏️ Özel Ön Ek: {_config.Settings.Prefix}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni ön ek metnini yazın (örn: [gold][Sunucu][default]):", (adm, val) =>
                {
                    _config.Settings.Prefix = val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, $"Ön ek güncellendi: {_config.Settings.Prefix}");
                }, reopen: adm => _settingsMenu?.Open(adm));
            })

            // Discord Linki
            .Button(p => $"💬 Discord: {_config.Settings.DiscordInvite}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni Discord davet linkini yazın (örn: discord.gg/sunucu):", (adm, val) =>
                {
                    _config.Settings.DiscordInvite = val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, $"Discord linki güncellendi: {_config.Settings.DiscordInvite}");
                }, reopen: adm => _settingsMenu?.Open(adm));
            })

            // Web Sitesi Linki
            .Button(p => $"🌐 Web Sitesi: {_config.Settings.WebsiteUrl}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni web sitesi adresini yazın (örn: www.sunucu.com):", (adm, val) =>
                {
                    _config.Settings.WebsiteUrl = val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, $"Web sitesi güncellendi: {_config.Settings.WebsiteUrl}");
                }, reopen: adm => _settingsMenu?.Open(adm));
            })

            // Sunucu Adı Özel Belirtme
            .Button(p => $"🏷️ Sunucu Adı: {(string.IsNullOrWhiteSpace(_config.Settings.ServerNameOverride) ? "(Otomatik Hostname)" : _config.Settings.ServerNameOverride)}", ctx =>
            {
                PromptInput(ctx.Player, "Özel sunucu adını yazın (otomatik hostname için - yazın):", (adm, val) =>
                {
                    _config.Settings.ServerNameOverride = val == "-" ? "" : val.Trim();
                    SaveAndApply();
                    SuccessToast(adm, "Sunucu adı güncellendi.");
                }, reopen: adm => _settingsMenu?.Open(adm));
            })

            // Sadece Ölü İken HTML Göster
            .Toggle("Sadece Ölü İken HTML Göster",
                _ => _config.Settings.ShowHtmlWhenDeadOnly,
                (ctx, v) =>
                {
                    _config.Settings.ShowHtmlWhenDeadOnly = v;
                    SaveAndApply();
                    SuccessToast(ctx.Player, v ? "HTML sadece ölü iken gösterilecek." : "HTML herkese gösterilecek.");
                    ctx.Refresh();
                })

            // Minimum Oyuncu Sayısı
            .Select("Minimum Oyuncu Sayısı",
                MinPlayersLabels,
                _ => GetMinPlayersIndex(_config.Settings.MinPlayersForAds),
                (ctx, idx) =>
                {
                    _config.Settings.MinPlayersForAds = MinPlayersValues[idx];
                    SaveAndApply();
                    SuccessToast(ctx.Player, $"Minimum oyuncu sayısı {MinPlayersLabels[idx]} yapıldı.");
                    ctx.Refresh();
                })

            .Button("💾 Tüm Ayarları Kaydet & Yenile", ctx =>
            {
                SaveAndApply(restartTimers: true);
                SuccessToast(ctx.Player, "Tüm ayarlar kaydedildi ve zamanlayıcılar yenilendi!");
            })

            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ============================== 7. HIZLI ANONS MENÜSÜ ==============================

    private IMenu BuildInstantBroadcastMenu()
    {
        return _hud.Menu("broadcastsystem.instant", "📢 Hızlı Anons & Canlı Yayınla")
            .Description("Kanal ve hedef kitle seçerek anında duyuru gönderin:")
            .Select("📡 Gönderim Kanalı",
                ChannelLabels,
                _ => _instantChannelIdx,
                (ctx, idx) =>
                {
                    _instantChannelIdx = idx;
                    ctx.Refresh();
                })
            .Select("👥 Hedef Kitle",
                TeamPresetLabels,
                _ => _instantTeamIdx,
                (ctx, idx) =>
                {
                    _instantTeamIdx = idx;
                    ctx.Refresh();
                })
            .Toggle("🏷️ Ön Ek (Prefix) Kullan",
                _ => _instantUsePrefix,
                (ctx, v) =>
                {
                    _instantUsePrefix = v;
                    ctx.Refresh();
                })
            .Button("💬 Mesajı Yaz ve Hemen Yayınla", ctx =>
            {
                PromptInput(ctx.Player, "Yayınlanacak anlık duyuru mesajını yazın:", (adm, text) =>
                {
                    var targetTeam = TeamPresets[_instantTeamIdx].FirstOrDefault() ?? "";
                    int count = _service.SendInstantBroadcast(
                        text,
                        ChannelValues[_instantChannelIdx],
                        targetTeam,
                        _instantUsePrefix);

                    SuccessToast(adm, $"Duyuru {count} oyuncuya ({ChannelLabels[_instantChannelIdx]}) gönderildi!");
                }, reopen: adm => _instantBroadcastMenu?.Open(adm));
            })
            .Button("⏭️ Sıradaki Zamanlı Duyuruyu Hemen Tetikle", ctx =>
            {
                if (_service.BroadcastNextAd(ignoreConditions: true))
                    SuccessToast(ctx.Player, "Sıradaki zamanlı duyuru yayınlandı!");
                else
                    WarningToast(ctx.Player, "Aktif duyuru bulunamadı.");
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ============================== 8. ANKET & GERİ SAYIM MENÜSÜ ==============================

    private IMenu BuildPollCountdownMenu()
    {
        return _hud.Menu("broadcastsystem.poll", "📊 Canlı Anket & Geri Sayım")
            .Description("Oyun içi interaktif geri sayım ve oylama araçları:")
            .Button("⏳ 10 Saniye Hızlı Geri Sayım", ctx =>
            {
                _api.CloseModal(ctx.Player);
                _service.StartCountdown(10, "Etkinlik Başlangıcı", PlayerHelper.GetName(ctx.Player));
                SuccessToast(ctx.Player, "10 saniye geri sayım başlatıldı!");
            })
            .Button("⏳ 30 Saniye Hızlı Geri Sayım", ctx =>
            {
                _api.CloseModal(ctx.Player);
                _service.StartCountdown(30, "Hazırlık Süresi", PlayerHelper.GetName(ctx.Player));
                SuccessToast(ctx.Player, "30 saniye geri sayım başlatıldı!");
            })
            .Button("⏳ 60 Saniye Geri Sayım", ctx =>
            {
                _api.CloseModal(ctx.Player);
                _service.StartCountdown(60, "Süre Doluyor", PlayerHelper.GetName(ctx.Player));
                SuccessToast(ctx.Player, "60 saniye geri sayım başlatıldı!");
            })
            .Button("⏳ Özel Başlıklı Geri Sayım (15 sn)", ctx =>
            {
                PromptInput(ctx.Player, "Geri sayım başlığını yazın (örn: Saklambaç Başlıyor):", (adm, title) =>
                {
                    _service.StartCountdown(15, title, PlayerHelper.GetName(adm));
                    SuccessToast(adm, $"'{title}' geri sayımı başlatıldı!");
                });
            })
            .Button("📊 Hızlı Evet / Hayır Anketi Başlat", ctx =>
            {
                PromptInput(ctx.Player, "Anket sorusunu yazın (örn: Harita değişsin mi?):", (adm, question) =>
                {
                    _service.StartPoll(question, new List<string> { "Evet", "Hayır" }, 30, PlayerHelper.GetName(adm));
                    SuccessToast(adm, "30 saniyelik anket başlatıldı!");
                });
            })
            .Button("🛑 Aktif Geri Sayımı / Anketi Durdur", ctx =>
            {
                _service.StopCountdown(silent: false);
                _service.StopPoll(announceResults: true);
                SuccessToast(ctx.Player, "Aktif sayaç ve anketler sonlandırıldı.");
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ============================== 9. REHBER & BİLGİ KUTUSU MENÜSÜ ==============================

    private IMenu BuildGuideMenu()
    {
        return _hud.Menu("broadcastsystem.guide", "ℹ️ Değişkenler & Kılavuz")
            .Description("Mesajlarınızda otomatik dolan dinamik değişkenler:")
            .Info("• {NAME} = Oyuncunun Adı")
            .Info("• {SERVERNAME} = Sunucu Adı")
            .Info("• {MAP} = Mevcut Harita")
            .Info("• {PLAYERS} / {MAXPLAYERS} = Aktif/Maks Oyuncu")
            .Info("• {DISCORD} = Discord Davet Linki")
            .Info("• {WEBSITE} = Web Sitesi / Mağaza")
            .Info("• {TIME} = Sunucu Saati (HH:mm)")
            .Info("• {COUNTRY} = Ülke Adı (GeoIP)")
            .Info("• {STEAMID2} = Steam ID (STEAM_1:...)")
            .Info("• {PLAYTIME} = Oyuncunun Oynama Süresi")
            .Button("💬 Değişkenleri Chat'te Test Et", ctx =>
            {
                _service.Reply(ctx.Player, "[gold]═══════ Değişken Testi ═══════[default]");
                _service.Reply(ctx.Player, "Oyuncu: [lime]{NAME}[default] | Ülke: [gold]{COUNTRY}[default]");
                _service.Reply(ctx.Player, "Harita: [lightblue]{MAP}[default] | Oyuncular: [lime]{PLAYERS}/{MAXPLAYERS}[default]");
                _service.Reply(ctx.Player, "Saat: [gold]{TIME}[default] | Discord: [lightblue]{DISCORD}[default]");
                _service.Reply(ctx.Player, "[gold]═════════════════════════════[default]");
                SuccessToast(ctx.Player, "Test satırları sohbete basıldı!");
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ============================== HELPER INDEX RESOLVERS ==============================

    private static int GetAdIntervalIndex(float seconds)
    {
        int bestIdx = 0;
        float minDiff = float.MaxValue;
        for (int i = 0; i < AdIntervalValues.Length; i++)
        {
            float diff = Math.Abs(AdIntervalValues[i] - seconds);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private static int GetWelcomeDelayIndex(float seconds)
    {
        int bestIdx = 0;
        float minDiff = float.MaxValue;
        for (int i = 0; i < WelcomeDelayValues.Length; i++)
        {
            float diff = Math.Abs(WelcomeDelayValues[i] - seconds);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private static int GetDurationIndex(int ms)
    {
        int bestIdx = 0;
        int minDiff = int.MaxValue;
        for (int i = 0; i < HtmlDurationValues.Length; i++)
        {
            int diff = Math.Abs(HtmlDurationValues[i] - ms);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private static int GetMinPlayersIndex(int count)
    {
        int idx = Array.IndexOf(MinPlayersValues, count);
        return idx >= 0 ? idx : 0;
    }

    private static int GetWeightIndex(int weight)
    {
        int idx = Array.IndexOf(WeightValues, weight);
        return idx >= 0 ? idx : 0;
    }

    private static int GetChannelIndex(string ch)
    {
        int idx = Array.FindIndex(ChannelValues, c => c.Equals(ch, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 0;
    }

    private static int GetTeamPresetIndex(List<string> teams)
    {
        if (teams.Count == 1)
        {
            if (teams[0].Equals("T", StringComparison.OrdinalIgnoreCase)) return 1;
            if (teams[0].Equals("CT", StringComparison.OrdinalIgnoreCase)) return 2;
            if (teams[0].Equals("SPEC", StringComparison.OrdinalIgnoreCase)) return 3;
        }
        return 0;
    }

    private static int GetPrefixIndex(string prefix)
    {
        int idx = Array.IndexOf(PrefixPresets, prefix);
        return idx >= 0 ? idx : 0;
    }

    private int GetPermissionIndex(string? perm)
    {
        if (string.IsNullOrWhiteSpace(perm)) return 0;
        if (perm.Equals("vip", StringComparison.OrdinalIgnoreCase)) return 1;
        if (perm.Equals("!vip", StringComparison.OrdinalIgnoreCase)) return 2;
        if (perm.Equals(_config.Commands.AdminPermission, StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    private static string ShortPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(boş)";
        var clean = ColorHelper.StripAll(text).Replace("\r", "").Replace("\n", " ");
        return clean.Length <= 24 ? clean : clean[..24] + "…";
    }
}

