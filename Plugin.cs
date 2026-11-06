using BroadcastSystem.Config;
using BroadcastSystem.Core;
using BroadcastSystem.Utils;
using Microsoft.Extensions.Logging;
using PanoramaHud.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace BroadcastSystem;

[PluginMetadata(
    Id = "broadcastsystem",
    Version = "2.1.0",
    Name = "BroadcastSystem",
    Author = "surfevo",
    Description = "Zamanlı otomatik duyurular, gelişmiş hoş geldin/ayrılma mesajları, anlık admin anonsları, canlı anket/geri sayım ve Panorama HUD görsel yönetim menüsü."
)]
public class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private PluginConfig _config = new();
    private ConfigLoader? _configLoader;
    private readonly GeoIpHelper _geoIp = new();
    private BroadcastService? _broadcastService;
    private BroadcastAdminMenu? _adminMenu;
    private IPanoramaHudApi? _hudApi;
    private IHudScope? _hudScope;
    private BroadcastPanoramaMenu? _panoramaMenu;

    private readonly List<Guid> _commandGuids = new();
    private Guid _chatHookGuid;
    private CancellationTokenSource? _adTimer;
    private CancellationTokenSource? _welcomeTimer;
    private readonly Dictionary<int, float> _lastCmdTime = new();

    public override void Load(bool hotReload)
    {
        var configDir = ResolveConfigDirectory();
        _configLoader = new ConfigLoader(configDir);

        try
        {
            _config = _configLoader.Load();
            LogConfigWarnings();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "[BroadcastSystem] Config yüklenemedi, varsayılan ayarlar kullanılıyor.");
            _config = new PluginConfig { Ads = PluginConfig.CreateDefaultAds() };
        }

        _geoIp.TryLoadDatabase(configDir, Core.PluginPath, _config.Settings.GeoIpDatabasePath);

        _broadcastService = new BroadcastService(Core, _config, _geoIp);
        _adminMenu = new BroadcastAdminMenu(Core, _configLoader, _config, _broadcastService, StartTimers);
        _broadcastService.SetMenuOpenPredicate(slot =>
            (_adminMenu != null && _adminMenu.IsMenuOpen(slot)) ||
            (_panoramaMenu != null && _panoramaMenu.IsMenuOpen(slot)));

        if (_hudScope != null && _hudApi != null && _panoramaMenu == null)
        {
            _panoramaMenu = new BroadcastPanoramaMenu(
                Core,
                _hudScope,
                _hudApi,
                _configLoader,
                _config,
                _broadcastService,
                StartTimers);
            _panoramaMenu.BuildMenus();
        }

        RegisterCommands();
        RegisterEvents();
        StartTimers();

        if (hotReload)
        {
            Core.Scheduler.NextTick(() =>
            {
                foreach (var p in Core.PlayerManager.GetAllPlayers().Where(x => PlayerHelper.IsUsable(x) && !x!.IsFakeClient))
                {
                    var s = _broadcastService.TrackOrUpdatePlayer(p!);
                    s.Welcomed = true;
                    s.PublicJoinAnnounced = true;
                }
            });
        }

        Core.Logger.LogInformation(
            "[BroadcastSystem] v2.1.0 yüklendi ({Ads} duyuru, Aralık: {Interval:0}sn, GeoIP: {Geo}, PanoramaHud: {Phud}). Menü: !{MenuCmd}",
            _config.Ads.Count,
            _config.Settings.AdIntervalSeconds,
            _geoIp.IsDatabaseLoaded ? "Aktif" : "Dil/IP Fallback",
            _panoramaMenu?.IsAvailable == true ? "Aktif (Görsel GUI)" : "Devre Dışı (Klasik Fallback)",
            _config.Commands.MenuCommand);
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (!interfaceManager.HasSharedInterface("PanoramaHud")) return;
        try
        {
            _hudApi = interfaceManager.GetSharedInterface<IPanoramaHudApi>("PanoramaHud");
            _hudScope = _hudApi.CreateScope("broadcastsystem");
            if (_broadcastService != null && _configLoader != null && _panoramaMenu == null)
            {
                _panoramaMenu = new BroadcastPanoramaMenu(
                    Core,
                    _hudScope,
                    _hudApi,
                    _configLoader,
                    _config,
                    _broadcastService,
                    StartTimers);
                _panoramaMenu.BuildMenus();
            }
            Core.Logger.LogInformation("[BroadcastSystem] Panorama HUD arayüzü bağlandı.");
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "[BroadcastSystem] Panorama HUD başlatılırken hata oluştu.");
        }
    }

    public override void Unload()
    {
        StopTimers();
        _broadcastService?.StopAllInteractiveTimers();
        _panoramaMenu?.Dispose();
        _hudScope?.Dispose();
        _adminMenu?.CloseAll();
        UnregisterEvents();
        UnregisterCommands();
        _geoIp.Dispose();

        Core.Logger.LogInformation("[BroadcastSystem] Eklenti kapatıldı.");
    }

    private string ResolveConfigDirectory()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Core.Configuration.BasePath))
                return Core.Configuration.BasePath;
        }
        catch { /* fallback */ }

        return Path.Combine(Core.PluginPath, "BroadcastSystem");
    }

    private void LogConfigWarnings()
    {
        if (_configLoader == null) return;
        foreach (var w in _configLoader.Warnings)
            Core.Logger.LogWarning("[BroadcastSystem] Config uyarısı: {Warning}", w);
    }

    // ============================== EVENTS & TIMERS ==============================

    private void RegisterEvents()
    {
        _chatHookGuid = Core.Command.HookClientChat(OnClientChat);

        Core.Event.OnTick += OnTick;
        Core.Event.OnClientKeyStateChanged += OnClientKeyStateChanged;
        Core.Event.OnClientPutInServer += OnClientPutInServer;
        Core.Event.OnClientSteamAuthorize += OnClientSteamAuthorize;
        Core.Event.OnClientDisconnected += OnClientDisconnected;
        Core.Event.OnMapLoad += OnMapLoad;
    }

    private void UnregisterEvents()
    {
        if (_chatHookGuid != Guid.Empty)
        {
            Core.Command.UnhookClientChat(_chatHookGuid);
            _chatHookGuid = Guid.Empty;
        }

        Core.Event.OnTick -= OnTick;
        Core.Event.OnClientKeyStateChanged -= OnClientKeyStateChanged;
        Core.Event.OnClientPutInServer -= OnClientPutInServer;
        Core.Event.OnClientSteamAuthorize -= OnClientSteamAuthorize;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;
        Core.Event.OnMapLoad -= OnMapLoad;
    }

    private void StartTimers()
    {
        StopTimers();

        if (_config.Settings.EnableAds && _config.Settings.AdIntervalSeconds >= 3.0f)
        {
            _adTimer = Core.Scheduler.RepeatBySeconds(_config.Settings.AdIntervalSeconds, () =>
            {
                _broadcastService?.BroadcastNextAd();
            });
        }

        _welcomeTimer = Core.Scheduler.RepeatBySeconds(1.0f, () =>
        {
            _broadcastService?.ProcessWelcomeTick();
        });
    }

    private void StopTimers()
    {
        try { _adTimer?.Cancel(); } catch { /* yoksay */ }
        try { _welcomeTimer?.Cancel(); } catch { /* yoksay */ }
        _adTimer = null;
        _welcomeTimer = null;
    }

    private void OnTick() => _adminMenu?.OnTick();

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent e)
    {
        if (!e.Pressed || _adminMenu == null || !_adminMenu.IsMenuOpen(e.PlayerId)) return;
        var player = Core.PlayerManager.GetPlayer(e.PlayerId);
        if (PlayerHelper.IsUsable(player))
            _adminMenu.OnKeyPressed(player!, e.Key);
    }

    private HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        if (_panoramaMenu != null && _panoramaMenu.TryConsumeChatInput(playerId, text))
            return HookResult.Stop;

        if (_adminMenu != null && _adminMenu.TryConsumeChatInput(playerId, text))
            return HookResult.Stop;

        if (TryHandleFallbackChatCommand(playerId, text))
            return HookResult.Stop;

        return HookResult.Continue;
    }

    private void OnClientPutInServer(IOnClientPutInServerEvent e)
    {
        var playerId = e.PlayerId;
        Core.Scheduler.NextTick(() =>
        {
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (!PlayerHelper.IsUsable(player) || player!.IsFakeClient) return;
            _broadcastService?.TrackOrUpdatePlayer(player, resetWelcome: true);
        });
    }

    private void OnClientSteamAuthorize(IOnClientSteamAuthorizeEvent e)
    {
        var playerId = e.PlayerId;
        Core.Scheduler.NextTick(() =>
        {
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (!PlayerHelper.IsUsable(player) || player!.IsFakeClient) return;
            _broadcastService?.TrackOrUpdatePlayer(player);
        });
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent e)
    {
        _lastCmdTime.Remove(e.PlayerId);
        _panoramaMenu?.OnPlayerDisconnected(e.PlayerId);
        _adminMenu?.OnPlayerDisconnected(e.PlayerId);
        _broadcastService?.OnPlayerDisconnected(e.PlayerId);
    }

    private void OnMapLoad(IOnMapLoadEvent e)
    {
        _lastCmdTime.Clear();
        _panoramaMenu?.OnMapLoad();
        _adminMenu?.CloseAll();
        _broadcastService?.OnMapChanged();
    }

    // ================================= COMMANDS =================================

    private void RegisterCommands()
    {
        UnregisterCommands();
        var c = _config.Commands;

        RegisterDual(c.MenuCommand, OnMenuCommand, "BroadcastSystem görsel admin menüsünü açar.");
        RegisterDual(c.SayBroadcastCommand, OnSayBroadcastCommand, "Tüm sunucuya Chat + HTML duyuru gönderir: !duyuru <mesaj>");
        RegisterDual(c.CenterBroadcastCommand, OnCenterBroadcastCommand, "Ekranın ortasında (Center) duyuru gönderir: !centerduyuru <mesaj>");
        RegisterDual(c.HtmlBroadcastCommand, OnHtmlBroadcastCommand, "Zengin CenterHTML duyuru gönderir: !htmlduyuru <mesaj>");
        RegisterDual(c.AlertBroadcastCommand, OnAlertBroadcastCommand, "CS2 Alert kutusunda duyuru gönderir: !alertduyuru <mesaj>");
        RegisterDual(c.CountdownCommand, OnCountdownCommand, "Canlı geri sayım başlatır: !gerisayim <saniye> [başlık]");
        RegisterDual(c.PollCommand, OnPollCommand, "Canlı anket başlatır: !anket \"<soru>\" \"<seçenek1>\" \"<seçenek2>\"");
        RegisterDual(c.VoteCommand, OnVoteCommand, "Aktif ankette oy kullanır: !oy <numara>");
        RegisterDual(c.NextAdCommand, OnNextAdCommand, "Sıradaki zamanlı duyuruyu hemen yayınlar.");
        RegisterDual(c.ReloadCommand, OnReloadCommand, "BroadcastSystem yapılandırmasını yeniden yükler.");
        RegisterDual("broadcast_reload", OnReloadCommand, "BroadcastSystem yapılandırmasını yeniden yükler.");
    }

    private void RegisterDual(string? name, ICommandService.CommandListener handler, string help)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _commandGuids.Add(Core.Command.RegisterCommand(name, handler, registerRaw: false, helpText: help));
        _commandGuids.Add(Core.Command.RegisterCommand(name, handler, registerRaw: true, helpText: help));
    }

    private void UnregisterCommands()
    {
        foreach (var g in _commandGuids)
            Core.Command.UnregisterCommand(g);
        _commandGuids.Clear();
    }

    private bool TryHandleFallbackChatCommand(int playerId, string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();

        if (trimmed.Length < 2 || (trimmed[0] != '!' && trimmed[0] != '/'))
            return false;

        var args = SplitArguments(trimmed[1..]);
        if (args.Count == 0) return false;

        var cmd = args[0];
        var rest = args.Skip(1).ToArray();
        var rawRest = trimmed.Length > cmd.Length + 1 ? trimmed[(cmd.Length + 1)..].Trim() : string.Empty;

        var player = Core.PlayerManager.GetPlayer(playerId);
        if (!PlayerHelper.IsUsable(player)) return false;

        var c = _config.Commands;

        if (Matches(cmd, c.MenuCommand, "bmenu", "broadcastmenu", "admenu"))
        {
            ExecuteMenuCommand(player!);
            return true;
        }
        if (Matches(cmd, c.SayBroadcastCommand, "bchat", "anons"))
        {
            ExecuteInstantCommand(player, null, rawRest, "both");
            return true;
        }
        if (Matches(cmd, c.CenterBroadcastCommand, "bcenter"))
        {
            ExecuteInstantCommand(player, null, rawRest, "center");
            return true;
        }
        if (Matches(cmd, c.HtmlBroadcastCommand, "bhtml"))
        {
            ExecuteInstantCommand(player, null, rawRest, "html");
            return true;
        }
        if (Matches(cmd, c.AlertBroadcastCommand, "balert"))
        {
            ExecuteInstantCommand(player, null, rawRest, "alert");
            return true;
        }
        if (Matches(cmd, c.CountdownCommand, "countdown", "sayac"))
        {
            ExecuteCountdownCommand(player, null, rest);
            return true;
        }
        if (Matches(cmd, c.PollCommand, "oylama", "poll"))
        {
            ExecutePollCommand(player, null, rest);
            return true;
        }
        if (Matches(cmd, c.VoteCommand, "vote"))
        {
            ExecuteVoteCommand(player!, rest);
            return true;
        }
        if (Matches(cmd, c.NextAdCommand, "nextad", "adtest"))
        {
            ExecuteNextAdCommand(player, null);
            return true;
        }
        if (Matches(cmd, c.ReloadCommand, "broadcast_reload", "breload"))
        {
            ExecuteReloadCommand(player, null);
            return true;
        }

        return false;
    }

    private static bool Matches(string input, params string?[] names) =>
        names.Any(n => !string.IsNullOrWhiteSpace(n) && n.Equals(input, StringComparison.OrdinalIgnoreCase));

    private static List<string> SplitArguments(string commandLine)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return list;

        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
            }
            else
            {
                cur.Append(ch);
            }
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    private bool CheckCallerPermission(IPlayer? caller)
    {
        if (!PlayerHelper.IsUsable(caller)) return true; // sunucu konsolu
        if (IsThrottled(caller!)) return false;
        if (!HasAdminPermission(caller!))
        {
            _broadcastService?.Reply(caller, "[lightred]Bu komutu kullanmak için yetkiniz yok.[default]");
            return false;
        }
        return true;
    }

    private bool IsThrottled(IPlayer player)
    {
        var now = Core.Engine.GlobalVars.CurrentTime;
        if (_lastCmdTime.TryGetValue(player.Slot, out var last) && MathF.Abs(now - last) < 0.2f)
            return true;
        _lastCmdTime[player.Slot] = now;
        return false;
    }

    private bool HasAdminPermission(IPlayer player)
    {
        var perm = _config.Commands.AdminPermission;
        if (string.IsNullOrWhiteSpace(perm)) return true;
        var sid = PlayerHelper.GetSteamId64(player);
        if (sid == 0) return false;
        try
        {
            return Core.Permission.PlayerHasPermission(sid, perm) ||
                   Core.Permission.PlayerHasPermission(sid, "admin") ||
                   Core.Permission.PlayerHasPermission(sid, "root");
        }
        catch { return false; }
    }

    // 1) !duyurumenu
    private void OnMenuCommand(ICommandContext ctx)
    {
        if (!PlayerHelper.IsUsable(ctx.Sender))
        {
            ctx.Reply("[BroadcastSystem] Görsel menü yalnızca oyun içinden (!duyurumenu) açılabilir. Konsol komutları: sw_duyuru, sw_gerisayim, sw_anket, sw_sonrakiduyuru, sw_duyurureload");
            return;
        }
        ExecuteMenuCommand(ctx.Sender!);
    }

    private void ExecuteMenuCommand(IPlayer admin)
    {
        if (!CheckCallerPermission(admin)) return;

        if (_panoramaMenu != null && _panoramaMenu.IsAvailable)
        {
            _panoramaMenu.Open(admin);
            return;
        }

        _adminMenu?.Toggle(admin);
    }

    // 2) !duyuru / !centerduyuru / !htmlduyuru / !alertduyuru
    private void OnSayBroadcastCommand(ICommandContext ctx) =>
        ExecuteInstantCommand(ctx.Sender, ctx, string.Join(" ", ctx.Args), "both");

    private void OnCenterBroadcastCommand(ICommandContext ctx) =>
        ExecuteInstantCommand(ctx.Sender, ctx, string.Join(" ", ctx.Args), "center");

    private void OnHtmlBroadcastCommand(ICommandContext ctx) =>
        ExecuteInstantCommand(ctx.Sender, ctx, string.Join(" ", ctx.Args), "html");

    private void OnAlertBroadcastCommand(ICommandContext ctx) =>
        ExecuteInstantCommand(ctx.Sender, ctx, string.Join(" ", ctx.Args), "alert");

    private void ExecuteInstantCommand(IPlayer? caller, ICommandContext? ctx, string message, string channel)
    {
        if (!CheckCallerPermission(caller)) return;
        if (_broadcastService == null) return;

        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.SayBroadcastCommand} <mesaj>[default]");
            return;
        }

        int count = _broadcastService.SendInstantBroadcast(trimmed, channel);
        SendOutput(caller, ctx, $"[lime]Duyuru {count} oyuncuya yayınlandı.[default]");
    }

    // 3) !gerisayim <saniye> [başlık]
    private void OnCountdownCommand(ICommandContext ctx) =>
        ExecuteCountdownCommand(ctx.Sender, ctx, SplitArguments(string.Join(" ", ctx.Args)).ToArray());

    private void ExecuteCountdownCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;
        if (_broadcastService == null) return;

        if (args.Length == 0)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.CountdownCommand} <saniye> \"[başlık]\"[default] (Durdurmak için: !{_config.Commands.CountdownCommand} dur)");
            return;
        }

        if (args[0].Equals("dur", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("stop", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("iptal", StringComparison.OrdinalIgnoreCase))
        {
            _broadcastService.StopCountdown(silent: false);
            return;
        }

        if (!int.TryParse(args[0], out int seconds) || seconds <= 0)
        {
            SendOutput(caller, ctx, "[lightred]Lütfen geçerli bir saniye değeri girin (örn: !gerisayim 10 \"Turnuva Başlıyor\").[default]");
            return;
        }

        var title = args.Length >= 2 ? string.Join(" ", args.Skip(1)) : "Geri Sayım";
        var senderName = PlayerHelper.IsUsable(caller) ? PlayerHelper.GetName(caller) : "Konsol";
        _broadcastService.StartCountdown(seconds, title, senderName);
    }

    // 4) !anket "<soru>" ["<seçenek1>" "<seçenek2>" ...]
    private void OnPollCommand(ICommandContext ctx) =>
        ExecutePollCommand(ctx.Sender, ctx, SplitArguments(string.Join(" ", ctx.Args)).ToArray());

    private void ExecutePollCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;
        if (_broadcastService == null) return;

        if (args.Length == 0)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.PollCommand} \"<soru>\" \"<seçenek1>\" \"<seçenek2>\"[default] (Bitirmek için: !{_config.Commands.PollCommand} bitir)");
            return;
        }

        if (args[0].Equals("bitir", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("dur", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            _broadcastService.StopPoll(announceResults: true);
            return;
        }

        var question = args[0];
        var options = args.Length >= 3
            ? args.Skip(1).ToList()
            : new List<string> { "Evet", "Hayır" };

        var senderName = PlayerHelper.IsUsable(caller) ? PlayerHelper.GetName(caller) : "Konsol";
        _broadcastService.StartPoll(question, options, 30, senderName);
    }

    // 5) !oy <seçenek_no> (tüm oyuncular kullanabilir)
    private void OnVoteCommand(ICommandContext ctx)
    {
        if (!PlayerHelper.IsUsable(ctx.Sender))
        {
            ctx.Reply("[BroadcastSystem] Bu komut yalnızca oyun içinden kullanılabilir.");
            return;
        }
        ExecuteVoteCommand(ctx.Sender!, ctx.Args);
    }

    private void ExecuteVoteCommand(IPlayer voter, string[] args)
    {
        if (IsThrottled(voter) || _broadcastService == null) return;

        if (args.Length == 0 || !int.TryParse(args[0], out int choice))
        {
            _broadcastService.Reply(voter, $"Kullanım: [lime]!{_config.Commands.VoteCommand} <seçenek_numarası>[default]");
            return;
        }

        _broadcastService.CastVote(voter, choice);
    }

    // 6) !sonrakiduyuru
    private void OnNextAdCommand(ICommandContext ctx) => ExecuteNextAdCommand(ctx.Sender, ctx);

    private void ExecuteNextAdCommand(IPlayer? caller, ICommandContext? ctx)
    {
        if (!CheckCallerPermission(caller)) return;
        if (_broadcastService == null) return;

        if (_broadcastService.BroadcastNextAd(ignoreConditions: true))
            SendOutput(caller, ctx, "[lime]Sıradaki zamanlı duyuru yayınlandı.[default]");
        else
            SendOutput(caller, ctx, "[lightred]Yayınlanabilecek aktif bir duyuru bulunamadı.[default]");
    }

    // 7) !duyurureload / !broadcast_reload
    private void OnReloadCommand(ICommandContext ctx) => ExecuteReloadCommand(ctx.Sender, ctx);

    private void ExecuteReloadCommand(IPlayer? caller, ICommandContext? ctx)
    {
        if (!CheckCallerPermission(caller)) return;
        if (_configLoader == null || _broadcastService == null) return;

        try
        {
            var newConfig = _configLoader.Load();
            _config = newConfig;

            _geoIp.TryLoadDatabase(ResolveConfigDirectory(), Core.PluginPath, _config.Settings.GeoIpDatabasePath);
            _broadcastService.UpdateConfig(_config);
            _adminMenu?.UpdateConfig(_config);
            _panoramaMenu?.UpdateConfig(_config);

            RegisterCommands();
            StartTimers();
            LogConfigWarnings();

            var msg = $"Config yeniden yüklendi ({_config.Ads.Count} duyuru, aralık: {_config.Settings.AdIntervalSeconds:0}sn, {_configLoader.Warnings.Count} uyarı).";
            SendOutput(caller, ctx, $"[lime]{msg}[default]");
            Core.Logger.LogInformation("[BroadcastSystem] {Message}", msg);
        }
        catch (Exception ex)
        {
            var err = $"Config yüklenemedi (eski ayarlar korundu): {ex.Message}";
            SendOutput(caller, ctx, $"[lightred]{err}[default]");
            Core.Logger.LogError(ex, "[BroadcastSystem] Config reload hatası.");
        }
    }

    private void SendOutput(IPlayer? caller, ICommandContext? ctx, string message)
    {
        if (PlayerHelper.IsUsable(caller))
            _broadcastService?.Reply(caller, message);
        else
            ctx?.Reply("[BroadcastSystem] " + ColorHelper.StripAll(message));
    }
}

