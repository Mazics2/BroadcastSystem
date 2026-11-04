using System.Net;
using System.Text;
using BroadcastSystem.Config;
using BroadcastSystem.Models;
using BroadcastSystem.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace BroadcastSystem.Core;

/// <summary>
/// Adminler icin oyun ici gorsel (CenterHTML) duyuru, hos geldin, anket ve ayar yonetim menusu (!duyurumenu).
/// W/S: Yukari/Asagi | A/D: Deger degistir | E: Sec | R: Geri/Kapat
/// Metin girisi gerektiginde bir sonraki chat mesajini yakalar.
/// </summary>
public sealed class BroadcastAdminMenu
{
    private const int RowsPerPage = 6;
    private const int HtmlDurationMs = 2000;
    private const float ResendSeconds = 0.25f;

    private static readonly string[] ChannelValues = { "both", "chat", "html", "center", "alert" };
    private static readonly string[] ChannelLabels = { "Chat + HTML", "Sadece Chat", "Sadece HTML", "Sadece Center", "Sadece Alert" };

    private static readonly string[][] TeamPresets =
    {
        Array.Empty<string>(),
        new[] { "T" },
        new[] { "CT" },
        new[] { "SPEC" }
    };
    private static readonly string[] TeamPresetLabels = { "Herkes", "Sadece T", "Sadece CT", "Sadece İzleyici" };

    private readonly ISwiftlyCore _core;
    private readonly ConfigLoader _loader;
    private readonly BroadcastService _service;
    private readonly Action _onTimerSettingsChanged;
    private PluginConfig _config;

    private readonly Dictionary<int, Session> _sessions = new();
    private readonly Dictionary<int, (string Prompt, Action<IPlayer, string> Callback)> _pendingInput = new();
    private readonly Dictionary<int, (string Html, float Time)> _lastHtml = new();

    // Hizli anons (Instant Broadcast) sayfasi icin admin secimleri
    private int _instantChannelIdx = 0; // "both"
    private int _instantTeamIdx = 0;    // Herkes
    private bool _instantUsePrefix = true;

    private enum RowStyle { Normal, Accent, On, Off, Danger, Muted }

    private sealed record Row(
        string Label,
        string? Value = null,
        bool Arrows = false,
        RowStyle Style = RowStyle.Normal,
        Action<IPlayer>? OnSelect = null,
        Action<IPlayer, int>? OnAdjust = null);

    private sealed class Page
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public List<Row> Rows { get; init; } = new();
    }

    private delegate Page PageBuilder(IPlayer admin);

    private sealed class Session(PageBuilder root)
    {
        private readonly Stack<(PageBuilder Builder, int Cursor)> _history = new();
        public PageBuilder Current { get; private set; } = root;
        public int Cursor { get; set; }
        public GameButtonFlags LastButtons { get; set; }
        public float LastKeyTime { get; set; }

        public void Push(PageBuilder next)
        {
            _history.Push((Current, Cursor));
            Current = next;
            Cursor = 0;
        }

        public bool Back()
        {
            if (_history.Count == 0) return false;
            var (b, c) = _history.Pop();
            Current = b;
            Cursor = c;
            return true;
        }
    }

    public BroadcastAdminMenu(
        ISwiftlyCore core,
        ConfigLoader loader,
        PluginConfig config,
        BroadcastService service,
        Action onTimerSettingsChanged)
    {
        _core = core;
        _loader = loader;
        _config = config;
        _service = service;
        _onTimerSettingsChanged = onTimerSettingsChanged;
    }

    public void UpdateConfig(PluginConfig config) => _config = config;

    public bool IsMenuOpen(int slot) => _sessions.ContainsKey(slot);

    public void Toggle(IPlayer admin)
    {
        if (IsMenuOpen(admin.Slot)) Close(admin);
        else Open(admin);
    }

    public void Open(IPlayer admin)
    {
        _sessions[admin.Slot] = new Session(BuildMainPage)
        {
            LastButtons = admin.PressedButtons,
            LastKeyTime = _core.Engine.GlobalVars.CurrentTime
        };
        SetFrozen(admin, true);
        _service.Reply(admin, "Duyuru yönetim menüsü açıldı! [lime]W/S[default]: Gezin | [lime]A/D[default]: Değiştir | [lime]E[default]: Seç | [lime]R[default]: Geri/Kapat");
    }

    public void Close(IPlayer admin)
    {
        _pendingInput.Remove(admin.Slot);
        if (_sessions.Remove(admin.Slot))
        {
            SetFrozen(admin, false);
            if (_lastHtml.Remove(admin.Slot))
                admin.SendCenterHTML(" ", 1);
        }
    }

    public void OnPlayerDisconnected(int slot)
    {
        _sessions.Remove(slot);
        _pendingInput.Remove(slot);
        _lastHtml.Remove(slot);
    }

    public void CloseAll()
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(PlayerHelper.IsUsable))
        {
            if (_sessions.ContainsKey(p!.Slot)) Close(p);
        }
        _sessions.Clear();
        _pendingInput.Clear();
        _lastHtml.Clear();
    }

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
            _service.Reply(admin, "[lightred]Giriş iptal edildi.[default]");
            return true;
        }

        pending.Callback(admin!, trimmed);
        return true;
    }

    private void PromptInput(IPlayer admin, string prompt, Action<IPlayer, string> callback)
    {
        _pendingInput[admin.Slot] = (prompt, callback);
        _service.Reply(admin, $"[gold]{prompt}[default] (İptal için sohbete [lightred]iptal[default] yazın)");
    }

    // ============================== TICK & INPUT ==============================

    public void OnTick()
    {
        if (_sessions.Count == 0) return;

        foreach (var kv in _sessions.ToList())
        {
            var admin = _core.PlayerManager.GetPlayer(kv.Key);
            if (!PlayerHelper.IsUsable(admin))
            {
                OnPlayerDisconnected(kv.Key);
                continue;
            }

            PollButtons(admin!, kv.Value);
            Render(admin!, kv.Value);
        }
    }

    private void PollButtons(IPlayer admin, Session session)
    {
        if (_config.Settings.FreezeWhileMenuOpen)
            SetFrozen(admin, true);

        var buttons = admin.PressedButtons;
        var rising = buttons & ~session.LastButtons;
        session.LastButtons = buttons;
        if (rising == 0) return;

        if ((rising & GameButtonFlags.W) != 0) OnKeyPressed(admin, KeyKind.W);
        else if ((rising & GameButtonFlags.S) != 0) OnKeyPressed(admin, KeyKind.S);
        else if ((rising & GameButtonFlags.A) != 0) OnKeyPressed(admin, KeyKind.A);
        else if ((rising & GameButtonFlags.D) != 0) OnKeyPressed(admin, KeyKind.D);
        else if ((rising & GameButtonFlags.E) != 0) OnKeyPressed(admin, KeyKind.E);
        else if ((rising & GameButtonFlags.R) != 0) OnKeyPressed(admin, KeyKind.R);
    }

    public void OnKeyPressed(IPlayer admin, KeyKind key)
    {
        if (!_sessions.TryGetValue(admin.Slot, out var session)) return;

        var now = _core.Engine.GlobalVars.CurrentTime;
        if (now - session.LastKeyTime < 0.12f) return;
        session.LastKeyTime = now;

        var page = session.Current(admin);
        var count = page.Rows.Count;
        if (count == 0) return;
        session.Cursor = Math.Clamp(session.Cursor, 0, count - 1);

        switch (key)
        {
            case KeyKind.W:
                session.Cursor = (session.Cursor - 1 + count) % count;
                break;
            case KeyKind.S:
                session.Cursor = (session.Cursor + 1) % count;
                break;
            case KeyKind.A:
                page.Rows[session.Cursor].OnAdjust?.Invoke(admin, -1);
                break;
            case KeyKind.D:
                page.Rows[session.Cursor].OnAdjust?.Invoke(admin, +1);
                break;
            case KeyKind.E:
                var r = page.Rows[session.Cursor];
                if (r.OnSelect != null) r.OnSelect(admin);
                else r.OnAdjust?.Invoke(admin, +1);
                break;
            case KeyKind.R:
            case KeyKind.Esc:
                _pendingInput.Remove(admin.Slot);
                if (!session.Back()) Close(admin);
                break;
        }
    }

    private void Render(IPlayer admin, Session session)
    {
        var page = session.Current(admin);
        if (page.Rows.Count > 0)
            session.Cursor = Math.Clamp(session.Cursor, 0, page.Rows.Count - 1);

        int pageIdx = session.Cursor / RowsPerPage;
        int pageCount = Math.Max(1, (page.Rows.Count + RowsPerPage - 1) / RowsPerPage);
        var slice = page.Rows.Skip(pageIdx * RowsPerPage).Take(RowsPerPage).ToList();
        int selectedInSlice = session.Cursor - pageIdx * RowsPerPage;

        var sb = new StringBuilder();
        sb.Append($"<font class='fontSize-m' color='#38BDF8'><b>✦ {E(page.Title)} ✦</b></font>");
        if (pageCount > 1)
            sb.Append($" <font class='fontSize-s' color='#94A3B8'>[{pageIdx + 1}/{pageCount}]</font>");
        sb.Append("<br>");

        if (!string.IsNullOrEmpty(page.Subtitle))
            sb.Append($"<font class='fontSize-xs' color='#CBD5E1'>{E(page.Subtitle)}</font><br>");

        for (int i = 0; i < slice.Count; i++)
        {
            var row = slice[i];
            bool sel = i == selectedInSlice;
            var labelColor = sel ? "#FACC15" : row.Style switch
            {
                RowStyle.Danger => "#F87171",
                RowStyle.Accent => "#38BDF8",
                RowStyle.Muted => "#64748B",
                _ => "#F8FAFC"
            };

            sb.Append("<font class='fontSize-sm'>");
            sb.Append(sel ? "<font color='#FACC15'><b>➤ </b></font>" : "<font color='#475569'>• </font>");
            sb.Append(sel
                ? $"<font color='{labelColor}'><b>{E(row.Label)}</b></font>"
                : $"<font color='{labelColor}'>{E(row.Label)}</font>");

            if (!string.IsNullOrEmpty(row.Value))
            {
                var valColor = row.Style switch
                {
                    RowStyle.On => "#4ADE80",
                    RowStyle.Off => "#F87171",
                    _ => "#E2E8F0"
                };
                var v = row.Arrows ? $"◀ {E(row.Value)} ▶" : $"[{E(row.Value)}]";
                sb.Append($" <font color='{valColor}'><b>{v}</b></font>");
            }
            sb.Append("</font><br>");
        }

        if (_pendingInput.TryGetValue(admin.Slot, out var pending))
            sb.Append($"<font class='fontSize-s' color='#FBBF24'><b>💬 Sohbete yazın: {E(pending.Prompt)}</b></font><br>");

        sb.Append("<font class='fontSize-xs' color='#94A3B8'>[W/S] Gezin • [A/D] Değiştir • [E] Seç • [R] Geri/Kapat</font>");

        var html = sb.ToString();
        var now = _core.Engine.GlobalVars.CurrentTime;
        if (_lastHtml.TryGetValue(admin.Slot, out var last) && last.Html == html && now - last.Time < ResendSeconds)
            return;

        _lastHtml[admin.Slot] = (html, now);
        admin.SendCenterHTML(html, HtmlDurationMs);
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private void SetFrozen(IPlayer player, bool freeze)
    {
        if (!_config.Settings.FreezeWhileMenuOpen && freeze) return;
        try
        {
            var pawn = player.PlayerPawn;
            if (pawn is not { IsValid: true }) return;
            var target = freeze ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
            if (pawn.MoveType != target)
            {
                pawn.MoveType = target;
                pawn.ActualMoveType = target;
                pawn.MoveTypeUpdated();
            }
            if (freeze)
            {
                pawn.AbsVelocity.X = 0f;
                pawn.AbsVelocity.Y = 0f;
                pawn.AbsVelocity.Z = 0f;
            }
        }
        catch { /* yoksay */ }
    }

    private void SaveAndApply(bool restartTimers = false)
    {
        try { _loader.Save(_config); }
        catch (Exception ex) { _core.Logger.LogError(ex, "[BroadcastSystem] Config kaydedilemedi."); }

        _service.UpdateConfig(_config);
        if (restartTimers)
            _onTimerSettingsChanged();
    }

    // ============================== MENU PAGES ==============================

    private Page BuildMainPage(IPlayer admin)
    {
        int activeAds = _config.Ads.Count(a => a.Enabled);
        return new Page
        {
            Title = "BroadcastSystem Yönetim",
            Subtitle = $"{activeAds}/{_config.Ads.Count} aktif duyuru • Aralık: {_config.Settings.AdIntervalSeconds:0} sn",
            Rows =
            {
                new Row("📢 Anlık Duyuru Gönder (Hızlı Anons)", Style: RowStyle.Accent,
                    OnSelect: p => _sessions[p.Slot].Push(BuildInstantBroadcastPage)),
                new Row("📋 Zamanlı Duyuruları Yönet", $"{_config.Ads.Count} kayıt", Style: RowStyle.Accent,
                    OnSelect: p => _sessions[p.Slot].Push(BuildAdsListPage)),
                new Row("➕ Yeni Zamanlı Duyuru Oluştur", Style: RowStyle.Accent,
                    OnSelect: StartCreateAdFlow),
                new Row("👋 Hoş Geldin & Ayrılma Ayarları", _config.Welcome.EnableWelcome ? "AÇIK" : "KAPALI",
                    Style: _config.Welcome.EnableWelcome ? RowStyle.On : RowStyle.Off,
                    OnSelect: p => _sessions[p.Slot].Push(BuildWelcomePage)),
                new Row("📊 Anket & Geri Sayım Araçları",
                    OnSelect: p => _sessions[p.Slot].Push(BuildPollAndCountdownPage)),
                new Row("🔧 Genel Ayarlar",
                    OnSelect: p => _sessions[p.Slot].Push(BuildSettingsPage)),
                new Row("❌ Menüyü Kapat", Style: RowStyle.Danger, OnSelect: Close)
            }
        };
    }

    private Page BuildInstantBroadcastPage(IPlayer admin)
    {
        return new Page
        {
            Title = "Anlık Duyuru Gönder",
            Subtitle = "Kanal ve hedef kitle seçip mesajınızı gönderin",
            Rows =
            {
                new Row("📡 Gönderim Kanalı", ChannelLabels[_instantChannelIdx], Arrows: true, Style: RowStyle.Accent,
                    OnAdjust: (_, dir) =>
                        _instantChannelIdx = ((_instantChannelIdx + dir) % ChannelValues.Length + ChannelValues.Length) % ChannelValues.Length),

                new Row("👥 Hedef Kitle", TeamPresetLabels[_instantTeamIdx], Arrows: true, Style: RowStyle.Accent,
                    OnAdjust: (_, dir) =>
                        _instantTeamIdx = ((_instantTeamIdx + dir) % TeamPresets.Length + TeamPresets.Length) % TeamPresets.Length),

                new Row("🏷️ Ön Ek (Prefix)", _instantUsePrefix ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: _instantUsePrefix ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => _instantUsePrefix = !_instantUsePrefix),

                new Row("💬 Mesajı Yaz ve Şimdi Yayınla (E)", Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Yayınlanacak duyuru mesajını sohbete yazın:", (adm, text) =>
                    {
                        var targetTeam = TeamPresets[_instantTeamIdx].FirstOrDefault() ?? "";
                        int count = _service.SendInstantBroadcast(
                            text,
                            ChannelValues[_instantChannelIdx],
                            targetTeam,
                            _instantUsePrefix);
                        _service.Reply(adm, $"[lime]Duyuru {count} oyuncuya ({ChannelLabels[_instantChannelIdx]}) gönderildi![default]");
                    })),

                new Row("⏭️ Sıradaki Zamanlı Duyuruyu Şimdi Tetikle",
                    OnSelect: a =>
                    {
                        if (_service.BroadcastNextAd(ignoreConditions: true))
                            _service.Reply(a, "[lime]Sıradaki zamanlı duyuru yayınlandı![default]");
                        else
                            _service.Reply(a, "[lightred]Yayınlanabilecek aktif duyuru bulunamadı.[default]");
                    })
            }
        };
    }

    private Page BuildAdsListPage(IPlayer admin)
    {
        var rows = new List<Row>
        {
            new("➕ Yeni Zamanlı Duyuru Ekle", Style: RowStyle.Accent, OnSelect: StartCreateAdFlow),
            new("⏭️ Sıradaki Duyuruyu Şimdi Yayınla", Style: RowStyle.Accent,
                OnSelect: a =>
                {
                    if (_service.BroadcastNextAd(ignoreConditions: true))
                        _service.Reply(a, "[lime]Sıradaki zamanlı duyuru yayınlandı![default]");
                    else
                        _service.Reply(a, "[lightred]Aktif duyuru bulunamadı.[default]");
                })
        };

        foreach (var ad in _config.Ads.ToList())
        {
            var entry = ad;
            rows.Add(new Row(
                Label: entry.Label,
                Value: $"{(entry.Enabled ? "AKTİF" : "KAPALI")} • {entry.ChannelsSummary()}",
                Style: entry.Enabled ? RowStyle.On : RowStyle.Off,
                OnSelect: a => _sessions[a.Slot].Push(_ => BuildAdEditPage(entry))));
        }

        if (_config.Ads.Count == 0)
            rows.Add(new Row("Kayıtlı zamanlı duyuru yok", Style: RowStyle.Muted));

        return new Page
        {
            Title = "Zamanlı Duyurular",
            Subtitle = "Düzenlemek veya test etmek istediğiniz duyuruyu seçin",
            Rows = rows
        };
    }

    private void StartCreateAdFlow(IPlayer admin)
    {
        PromptInput(admin, "Yeni duyurunun başlığını/adını yazın (örn: Turnuva Duyurusu):", (adm, rawName) =>
        {
            var id = "ad_" + (_config.Ads.Count + 1) + "_" + DateTime.Now.ToString("HHmmss");
            var ad = new AdEntry
            {
                Id = id,
                Name = rawName.Trim(),
                Enabled = true,
                Chat = $"[lime]{rawName.Trim()}[default]",
                Weight = 1
            };

            _config.Ads.Add(ad);
            SaveAndApply();
            _service.Reply(adm, $"[lime]'{ad.Name}'[default] duyurusu oluşturuldu! Şimdi mesaj kanallarını düzenleyebilirsiniz.");
            if (_sessions.TryGetValue(adm.Slot, out var session))
                session.Push(_ => BuildAdEditPage(ad));
        });
    }

    private Page BuildAdEditPage(AdEntry ad)
    {
        int curTeamIdx = GetTeamPresetIndex(ad.Teams);
        int curDuration = ad.HtmlDurationMs ?? _config.Settings.DefaultHtmlDurationMs;
        string prefixLabel = ad.UsePrefix switch
        {
            null => "Genel Ayar",
            true => "AÇIK",
            false => "KAPALI"
        };

        var rows = new List<Row>
        {
            new("Durum", ad.Enabled ? "AKTİF" : "KAPALI", Arrows: true,
                Style: ad.Enabled ? RowStyle.On : RowStyle.Off,
                OnAdjust: (_, _) => { ad.Enabled = !ad.Enabled; SaveAndApply(); }),

            new("🚀 Tüm Sunucuya Şimdi Yayınla", Style: RowStyle.Accent,
                OnSelect: a =>
                {
                    _service.SendAd(ad);
                    _service.Reply(a, $"[lime]'{ad.Label}'[default] duyurusu tüm sunucuya yayınlandı!");
                }),

            new("👁️ Sadece Bana Önizle", Style: RowStyle.Accent,
                OnSelect: a =>
                {
                    Close(a);
                    _service.SendAd(ad, singlePreviewTarget: a);
                    _service.Reply(a, $"[gold]'{ad.Label}'[default] önizlemesi gösterildi. Menüyü tekrar açmak için [lime]!{_config.Commands.MenuCommand}[default]");
                }),

            new("✏️ Başlık (Name)", ad.Label, Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni duyuru başlığını yazın:", (adm, val) =>
                {
                    ad.Name = val;
                    SaveAndApply();
                    _service.Reply(adm, $"Başlık [lime]{val}[default] olarak güncellendi.");
                })),

            new("💬 Chat Mesajı (Yazmak için E)", ShortPreview(ad.Chat), Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni Chat mesajını yazın (kaldırmak için - yazın):", (adm, val) =>
                {
                    ad.Chat = val == "-" ? null : val;
                    SaveAndApply();
                    _service.Reply(adm, "Chat mesajı güncellendi ve kaydedildi.");
                })),

            new("🖥️ CenterHTML Mesajı (Yazmak için E)", ShortPreview(ad.Html), Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni CenterHTML metnini yazın (kaldırmak için - yazın):", (adm, val) =>
                {
                    ad.Html = val == "-" ? null : val;
                    SaveAndApply();
                    _service.Reply(adm, "CenterHTML mesajı güncellendi ve kaydedildi.");
                })),

            new("🎯 Center Mesajı (Yazmak için E)", ShortPreview(ad.Center), Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni Center mesajını yazın (kaldırmak için - yazın):", (adm, val) =>
                {
                    ad.Center = val == "-" ? null : val;
                    SaveAndApply();
                    _service.Reply(adm, "Center mesajı güncellendi ve kaydedildi.");
                })),

            new("🔔 Alert Mesajı (Yazmak için E)", ShortPreview(ad.Alert), Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni Alert mesajını yazın (kaldırmak için - yazın):", (adm, val) =>
                {
                    ad.Alert = val == "-" ? null : val;
                    SaveAndApply();
                    _service.Reply(adm, "Alert mesajı güncellendi ve kaydedildi.");
                })),

            new("⏱️ HTML Süresi (ms)", $"{curDuration} ms", Arrows: true,
                OnAdjust: (_, dir) =>
                {
                    ad.HtmlDurationMs = Math.Clamp(curDuration + dir * 500, 1000, 20000);
                    SaveAndApply();
                }),

            new("🏷️ Ön Ek (Prefix)", prefixLabel, Arrows: true,
                OnAdjust: (_, _) =>
                {
                    ad.UsePrefix = ad.UsePrefix switch
                    {
                        null => true,
                        true => false,
                        false => null
                    };
                    SaveAndApply();
                }),

            new("🚩 Takım Filtresi", TeamPresetLabels[curTeamIdx], Arrows: true,
                OnAdjust: (_, dir) =>
                {
                    int next = ((curTeamIdx + dir) % TeamPresets.Length + TeamPresets.Length) % TeamPresets.Length;
                    ad.Teams = TeamPresets[next].ToList();
                    SaveAndApply();
                }),

            new("🔑 Yetki Filtresi", string.IsNullOrWhiteSpace(ad.Permission) ? "Herkes" : ad.Permission, Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yetki filtresini yazın (örn: vip, !vip veya kaldırmak için -):", (adm, val) =>
                {
                    ad.Permission = val == "-" ? null : val.Trim();
                    SaveAndApply();
                    _service.Reply(adm, $"Yetki filtresi güncellendi: [lime]{ad.Permission ?? "Herkes"}[default]");
                })),

            new("⚖️ Döngü Ağırlığı (Weight)", ad.Weight.ToString(), Arrows: true,
                OnAdjust: (_, dir) =>
                {
                    ad.Weight = Math.Clamp(ad.Weight + dir, 1, 20);
                    SaveAndApply();
                }),

            new("🗑️ Bu Duyuruyu Sil", Style: RowStyle.Danger,
                OnSelect: a =>
                {
                    _config.Ads.Remove(ad);
                    SaveAndApply();
                    _service.Reply(a, $"[lightred]'{ad.Label}' duyurusu silindi.[default]");
                    if (_sessions.TryGetValue(a.Slot, out var s)) s.Back();
                })
        };

        return new Page
        {
            Title = $"Duyuru: {ad.Label}",
            Subtitle = $"ID: {ad.Id} • Kanallar: {ad.ChannelsSummary()}",
            Rows = rows
        };
    }

    private Page BuildWelcomePage(IPlayer admin)
    {
        var w = _config.Welcome;
        int chIdx = Array.FindIndex(ChannelValues, c => c.Equals(w.PrivateChannel, StringComparison.OrdinalIgnoreCase));
        if (chIdx < 0) chIdx = 0;

        return new Page
        {
            Title = "Hoş Geldin & Ayrılma",
            Subtitle = "Oyuncu giriş/çıkış karşılama mesajları",
            Rows =
            {
                new Row("Özel Hoş Geldin Mesajı", w.EnableWelcome ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: w.EnableWelcome ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { w.EnableWelcome = !w.EnableWelcome; SaveAndApply(); }),

                new Row("Hoş Geldin Kanalı", ChannelLabels[chIdx], Arrows: true, Style: RowStyle.Accent,
                    OnAdjust: (_, dir) =>
                    {
                        int next = ((chIdx + dir) % ChannelValues.Length + ChannelValues.Length) % ChannelValues.Length;
                        w.PrivateChannel = ChannelValues[next];
                        SaveAndApply();
                    }),

                new Row("Karşılama Gecikmesi", $"{w.WelcomeDelaySeconds:0.0} sn", Arrows: true,
                    OnAdjust: (_, dir) =>
                    {
                        w.WelcomeDelaySeconds = Math.Clamp(w.WelcomeDelaySeconds + dir * 0.5f, 0.5f, 30.0f);
                        SaveAndApply();
                    }),

                new Row("Herkese Katılma Duyurusu", w.EnablePublicJoin ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: w.EnablePublicJoin ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { w.EnablePublicJoin = !w.EnablePublicJoin; SaveAndApply(); }),

                new Row("✏️ Katılma Mesajını Düzenle (E)", ShortPreview(w.PublicJoinMessage), Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Yeni katılma duyuru mesajını yazın ({NAME}, {COUNTRY}, {PLAYERS} kullanılabilir):", (adm, val) =>
                    {
                        w.PublicJoinMessage = val;
                        SaveAndApply();
                        _service.Reply(adm, "Katılma mesajı güncellendi.");
                    })),

                new Row("Herkese Ayrılma Duyurusu", w.EnablePublicLeave ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: w.EnablePublicLeave ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { w.EnablePublicLeave = !w.EnablePublicLeave; SaveAndApply(); }),

                new Row("✏️ Ayrılma Mesajını Düzenle (E)", ShortPreview(w.PublicLeaveMessage), Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Yeni ayrılma duyuru mesajını yazın ({NAME}, {STEAMID2}, {PLAYTIME} kullanılabilir):", (adm, val) =>
                    {
                        w.PublicLeaveMessage = val;
                        SaveAndApply();
                        _service.Reply(adm, "Ayrılma mesajı güncellendi.");
                    })),

                new Row("🧪 Hoş Geldin Mesajını Kendimde Test Et", Style: RowStyle.Accent,
                    OnSelect: a =>
                    {
                        Close(a);
                        _service.SendWelcomeToPlayer(a, forceEvenIfDisabled: true);
                    })
            }
        };
    }

    private Page BuildPollAndCountdownPage(IPlayer admin)
    {
        return new Page
        {
            Title = "Anket & Geri Sayım",
            Subtitle = "Canlı geri sayım ve oyuncu oylaması araçları",
            Rows =
            {
                new Row("⏳ 10 Saniye Hızlı Geri Sayım Başlat", Style: RowStyle.Accent,
                    OnSelect: a =>
                    {
                        Close(a);
                        _service.StartCountdown(10, "Etkinlik Başlangıcı", PlayerHelper.GetName(a));
                    }),

                new Row("⏳ 30 Saniye Hızlı Geri Sayım Başlat", Style: RowStyle.Accent,
                    OnSelect: a =>
                    {
                        Close(a);
                        _service.StartCountdown(30, "Hazırlık Süresi", PlayerHelper.GetName(a));
                    }),

                new Row("⏳ Özel Başlıklı Geri Sayım (15 sn)", Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Geri sayım başlığını sohbete yazın (örn: Saklambaç Başlıyor):", (adm, title) =>
                    {
                        Close(adm);
                        _service.StartCountdown(15, title, PlayerHelper.GetName(adm));
                    })),

                new Row("📊 Hızlı Evet / Hayır Anketi Başlat", Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Anket sorusunu sohbete yazın (örn: Harita değişsin mi?):", (adm, question) =>
                    {
                        Close(adm);
                        _service.StartPoll(question, new List<string> { "Evet", "Hayır" }, 30, PlayerHelper.GetName(adm));
                    })),

                new Row("🛑 Aktif Geri Sayımı / Anketi Bitir", Style: RowStyle.Danger,
                    OnSelect: a =>
                    {
                        _service.StopCountdown(silent: false);
                        _service.StopPoll(announceResults: true);
                        _service.Reply(a, "[lime]Aktif geri sayım ve anketler sonlandırıldı.[default]");
                    })
            }
        };
    }

    private Page BuildSettingsPage(IPlayer admin)
    {
        var s = _config.Settings;
        bool isRandom = s.RotationMode.Equals("random", StringComparison.OrdinalIgnoreCase);

        return new Page
        {
            Title = "Genel Ayarlar",
            Subtitle = "Değişiklikler anında kaydedilir ve uygulanır",
            Rows =
            {
                new Row("Otomatik Duyurular", s.EnableAds ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: s.EnableAds ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { s.EnableAds = !s.EnableAds; SaveAndApply(restartTimers: true); }),

                new Row("Duyuru Aralığı", $"{s.AdIntervalSeconds:0} sn", Arrows: true, Style: RowStyle.Accent,
                    OnAdjust: (_, dir) =>
                    {
                        s.AdIntervalSeconds = Math.Clamp(s.AdIntervalSeconds + dir * 5.0f, 5.0f, 600.0f);
                        SaveAndApply(restartTimers: true);
                    }),

                new Row("Döngü Modu", isRandom ? "Rastgele (Random)" : "Sıralı (Sequential)", Arrows: true, Style: RowStyle.Accent,
                    OnAdjust: (_, _) =>
                    {
                        s.RotationMode = isRandom ? "sequential" : "random";
                        SaveAndApply();
                    }),

                new Row("Duyuru Ön Eki (enable_prefix)", s.EnablePrefix ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: s.EnablePrefix ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { s.EnablePrefix = !s.EnablePrefix; SaveAndApply(); }),

                new Row("✏️ Ön Ek Metnini Düzenle (E)", s.Prefix, Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Yeni ön eki yazın (örn: [gold][Sunucu][default]):", (adm, val) =>
                    {
                        s.Prefix = val;
                        SaveAndApply();
                        _service.Reply(adm, $"Ön ek güncellendi: {s.Prefix}");
                    })),

                new Row("✏️ Discord Adresi (E)", s.DiscordInvite, Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Yeni Discord davet adresini yazın:", (adm, val) =>
                    {
                        s.DiscordInvite = val;
                        SaveAndApply();
                        _service.Reply(adm, $"Discord adresi güncellendi: [lightblue]{val}[default]");
                    })),

                new Row("✏️ Web Sitesi Adresi (E)", s.WebsiteUrl, Style: RowStyle.Accent,
                    OnSelect: a => PromptInput(a, "Yeni web sitesi adresini yazın:", (adm, val) =>
                    {
                        s.WebsiteUrl = val;
                        SaveAndApply();
                        _service.Reply(adm, $"Web sitesi güncellendi: [lime]{val}[default]");
                    })),

                new Row("Sadece Ölü İken HTML Göster", s.ShowHtmlWhenDeadOnly ? "EVET" : "HAYIR", Arrows: true,
                    Style: s.ShowHtmlWhenDeadOnly ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { s.ShowHtmlWhenDeadOnly = !s.ShowHtmlWhenDeadOnly; SaveAndApply(); }),

                new Row("Minimum Oyuncu Sayısı", s.MinPlayersForAds.ToString(), Arrows: true,
                    OnAdjust: (_, dir) =>
                    {
                        s.MinPlayersForAds = Math.Clamp(s.MinPlayersForAds + dir, 0, 32);
                        SaveAndApply();
                    })
            }
        };
    }

    private static string ShortPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(boş)";
        var clean = ColorHelper.StripAll(text).Replace("\r", "").Replace("\n", " ");
        return clean.Length <= 22 ? clean : clean[..22] + "…";
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
}

