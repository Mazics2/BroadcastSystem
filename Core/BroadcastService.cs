using System.Net;
using System.Text;
using BroadcastSystem.Config;
using BroadcastSystem.Models;
using BroadcastSystem.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace BroadcastSystem.Core;

/// <summary>
/// Zamanli otomatik duyurular, gelismis hos geldin / ayrilma mesajlari,
/// anlik admin anonslari, canli geri sayim ve oylama (anket) islemlerini yonetir.
/// </summary>
public class BroadcastService
{
    private readonly ISwiftlyCore _core;
    private readonly GeoIpHelper _geoIp;
    private PluginConfig _config;
    private Func<int, bool>? _isMenuOpenPredicate;

    private readonly Dictionary<int, PlayerSessionInfo> _sessions = new();
    private readonly Random _rng = new();
    private int _sequentialIndex;

    private CancellationTokenSource? _countdownCts;
    private int _countdownRemaining;
    private string _countdownTitle = string.Empty;

    private CancellationTokenSource? _pollCts;
    private ActivePoll? _activePoll;

    public bool IsCountdownActive => _countdownCts != null && _countdownRemaining > 0;
    public ActivePoll? CurrentPoll => _activePoll;

    public BroadcastService(ISwiftlyCore core, PluginConfig config, GeoIpHelper geoIp)
    {
        _core = core;
        _config = config;
        _geoIp = geoIp;
    }

    public void SetMenuOpenPredicate(Func<int, bool> predicate) => _isMenuOpenPredicate = predicate;

    public void UpdateConfig(PluginConfig config) => _config = config;

    public void StopAllInteractiveTimers()
    {
        StopCountdown(silent: true);
        StopPoll(announceResults: false);
    }

    // ========================== PLAYER SESSIONS & WELCOME ==========================

    public PlayerSessionInfo TrackOrUpdatePlayer(IPlayer player, bool resetWelcome = false)
    {
        var slot = player.Slot;
        var sid = PlayerHelper.GetSteamId64(player);
        var name = PlayerHelper.GetName(player);
        var ip = PlayerHelper.GetIpAddress(player);
        var (countryName, countryCode) = _geoIp.Resolve(player, ip);

        if (!_sessions.TryGetValue(slot, out var session) || resetWelcome)
        {
            var delay = Math.Max(0.5f, _config.Welcome.WelcomeDelaySeconds);
            session = new PlayerSessionInfo
            {
                Slot = slot,
                SteamId64 = sid,
                Name = name,
                IpAddress = ip,
                CountryName = countryName,
                CountryCode = countryCode,
                ConnectedAtUtc = DateTime.UtcNow,
                WelcomeDueUtc = DateTime.UtcNow.AddSeconds(delay),
                Welcomed = false,
                PublicJoinAnnounced = false
            };
            _sessions[slot] = session;
        }
        else
        {
            if (sid != 0) session.SteamId64 = sid;
            if (!string.IsNullOrWhiteSpace(name) && name != "Unknown") session.Name = name;
            if (!string.IsNullOrWhiteSpace(ip)) session.IpAddress = ip;
            if (!string.IsNullOrWhiteSpace(countryName))
            {
                session.CountryName = countryName;
                session.CountryCode = countryCode;
            }
        }

        return session;
    }

    public PlayerSessionInfo? GetSession(int slot) =>
        _sessions.TryGetValue(slot, out var s) ? s : null;

    /// <summary>Periyodik olarak (1 sn'de bir) yeni giren oyuncularin gecikmeli hos geldin mesajlarini gonderir.</summary>
    public void ProcessWelcomeTick()
    {
        var now = DateTime.UtcNow;
        var players = _core.PlayerManager.GetAllPlayers()
            .Where(p => PlayerHelper.IsUsable(p) && !p!.IsFakeClient)
            .ToList();

        foreach (var p in players)
        {
            var session = TrackOrUpdatePlayer(p!);
            if (session.Welcomed || now < session.WelcomeDueUtc)
                continue;

            session.Welcomed = true;

            if (_config.Welcome.EnableWelcome)
                SendWelcomeToPlayer(p!, session);

            if (_config.Welcome.EnablePublicJoin && !session.PublicJoinAnnounced)
            {
                session.PublicJoinAnnounced = true;
                AnnouncePublicJoin(p!, session);
            }
        }
    }

    public void SendWelcomeToPlayer(IPlayer player, PlayerSessionInfo? session = null, bool forceEvenIfDisabled = false)
    {
        if (!PlayerHelper.IsUsable(player) || player.IsFakeClient) return;
        if (!_config.Welcome.EnableWelcome && !forceEvenIfDisabled) return;

        session ??= TrackOrUpdatePlayer(player);
        var serverValues = PlaceholderHelper.BuildServerValues(_core, _config);
        var values = PlaceholderHelper.WithPlayerValues(serverValues, player, session);
        var team = PlayerHelper.GetTeam(player);
        var ch = _config.Welcome.PrivateChannel.Trim().ToLowerInvariant();

        try
        {
            if (ch is "both" or "chat")
            {
                foreach (var rawLine in _config.Welcome.PrivateChatLines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    var filled = PlaceholderHelper.Apply(rawLine, values);
                    var colored = Helper.Colored(ColorHelper.NormalizeTags(filled, team));
                    player.SendChat(colored);
                }
            }

            if (ch is "both" or "html")
            {
                if (!string.IsNullOrWhiteSpace(_config.Welcome.PrivateHtmlMessage) && !IsMenuOpen(player.Slot))
                {
                    var html = PlaceholderHelper.Apply(_config.Welcome.PrivateHtmlMessage, values);
                    player.SendCenterHTML(html, _config.Welcome.PrivateHtmlDurationMs);
                }
            }
            else if (ch == "center")
            {
                var line = _config.Welcome.PrivateChatLines.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Hoş geldin {NAME}!";
                var clean = ColorHelper.StripAll(PlaceholderHelper.Apply(line, values));
                player.SendCenter(clean);
            }
            else if (ch == "alert")
            {
                var line = _config.Welcome.PrivateChatLines.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Hoş geldin {NAME}!";
                var clean = ColorHelper.StripAll(PlaceholderHelper.Apply(line, values));
                player.SendAlert(clean);
            }
        }
        catch (Exception ex)
        {
            if (_config.Settings.Debug)
                _core.Logger.LogWarning(ex, "[BroadcastSystem] Hoş geldin mesajı gönderilemedi.");
        }
    }

    private void AnnouncePublicJoin(IPlayer joiningPlayer, PlayerSessionInfo session)
    {
        if (string.IsNullOrWhiteSpace(_config.Welcome.PublicJoinMessage)) return;

        var serverValues = PlaceholderHelper.BuildServerValues(_core, _config);
        var values = PlaceholderHelper.WithPlayerValues(serverValues, joiningPlayer, session);
        var raw = PrependPrefixIfEnabled(_config.Welcome.PublicJoinMessage, _config.Settings.EnablePrefix);
        var filled = PlaceholderHelper.Apply(raw, values);
        var line = Helper.Colored(ColorHelper.NormalizeTags(filled, PlayerHelper.GetTeam(joiningPlayer)));

        BroadcastChatLineToAll(line);
    }

    public void OnPlayerDisconnected(int slot)
    {
        if (!_sessions.Remove(slot, out var session)) return;
        if (!session.Welcomed) return; // Henuz sunucuya tam girmemis/yukleme ekranindan cikmis oyuncu

        if (_config.Welcome.EnablePublicLeave && !string.IsNullOrWhiteSpace(_config.Welcome.PublicLeaveMessage))
        {
            var serverValues = PlaceholderHelper.BuildServerValues(_core, _config);
            var values = PlaceholderHelper.WithPlayerValues(serverValues, null, session);
            var raw = PrependPrefixIfEnabled(_config.Welcome.PublicLeaveMessage, _config.Settings.EnablePrefix);
            var filled = PlaceholderHelper.Apply(raw, values);
            var line = Helper.Colored(ColorHelper.NormalizeTags(filled));

            BroadcastChatLineToAll(line);
        }
    }

    public void OnMapChanged()
    {
        StopAllInteractiveTimers();
        _sessions.Clear();
    }

    // ========================== TIMED BROADCASTS ([[ads]]) ==========================

    public bool BroadcastNextAd(bool ignoreConditions = false)
    {
        if (!_config.Settings.EnableAds && !ignoreConditions) return false;

        var recipients = GetEligibleHumanPlayers();
        if (!ignoreConditions && recipients.Count < _config.Settings.MinPlayersForAds)
            return false;

        string currentMap = string.Empty;
        try { currentMap = _core.Engine.GlobalVars.MapName.ToString(); } catch { /* yoksay */ }

        var candidates = _config.Ads
            .Where(a => a.Enabled && a.HasAnyContent() && (ignoreConditions || a.MatchesMap(currentMap)))
            .ToList();

        if (candidates.Count == 0) return false;

        AdEntry selected;
        if (_config.Settings.RotationMode.Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            int totalWeight = candidates.Sum(a => Math.Max(1, a.Weight));
            int roll = _rng.Next(0, totalWeight);
            int acc = 0;
            selected = candidates[0];
            foreach (var c in candidates)
            {
                acc += Math.Max(1, c.Weight);
                if (roll < acc)
                {
                    selected = c;
                    break;
                }
            }
        }
        else
        {
            selected = candidates[_sequentialIndex % candidates.Count];
            _sequentialIndex = (_sequentialIndex + 1) % Math.Max(1, candidates.Count);
        }

        SendAd(selected);
        return true;
    }

    public void SendAd(AdEntry ad, IPlayer? singlePreviewTarget = null)
    {
        if (!ad.HasAnyContent()) return;

        var serverValues = PlaceholderHelper.BuildServerValues(_core, _config);
        var targets = singlePreviewTarget != null
            ? new List<IPlayer> { singlePreviewTarget }
            : _core.PlayerManager.GetAllPlayers().Where(PlayerHelper.IsUsable).Cast<IPlayer>().ToList();

        bool usePrefix = ad.UsePrefix ?? _config.Settings.EnablePrefix;
        int htmlDuration = ad.HtmlDurationMs ?? _config.Settings.DefaultHtmlDurationMs;

        foreach (var player in targets)
        {
            if (_config.Settings.SkipBots && player.IsFakeClient && singlePreviewTarget == null)
                continue;

            var team = PlayerHelper.GetTeam(player);
            if (singlePreviewTarget == null)
            {
                if (!ad.MatchesTeam(team)) continue;
                if (!MatchesPermission(player, ad.Permission)) continue;
            }

            var session = GetSession(player.Slot);
            var values = PlaceholderHelper.WithPlayerValues(serverValues, player, session);

            try
            {
                // 1) Chat (coklu satir '\n' destekli)
                if (!string.IsNullOrWhiteSpace(ad.Chat))
                {
                    SendFormattedChat(player, ad.Chat, values, team, usePrefix);
                }

                // 2) Center
                if (!string.IsNullOrWhiteSpace(ad.Center))
                {
                    var centerText = ColorHelper.StripAll(PlaceholderHelper.Apply(ad.Center, values));
                    if (!string.IsNullOrWhiteSpace(centerText))
                        player.SendCenter(centerText);
                }

                // 3) CenterHTML (admin menusu veya canli anket/sayac acikken ustune yazma)
                if (!string.IsNullOrWhiteSpace(ad.Html) && !IsCenterHtmlBusy(player.Slot))
                {
                    bool canShowHtml = singlePreviewTarget != null ||
                                       !_config.Settings.ShowHtmlWhenDeadOnly ||
                                       !PlayerHelper.IsAlive(player);
                    if (canShowHtml)
                    {
                        var htmlText = PlaceholderHelper.Apply(ad.Html, values);
                        player.SendCenterHTML(htmlText, htmlDuration);
                    }
                }

                // 4) Alert
                if (!string.IsNullOrWhiteSpace(ad.Alert))
                {
                    var alertText = ColorHelper.StripAll(PlaceholderHelper.Apply(ad.Alert, values));
                    if (!string.IsNullOrWhiteSpace(alertText))
                        player.SendAlert(alertText);
                }
            }
            catch (Exception ex)
            {
                if (_config.Settings.Debug)
                    _core.Logger.LogWarning(ex, "[BroadcastSystem] Duyuru gönderilirken hata oluştu (slot {Slot}).", player.Slot);
            }
        }

        if (_config.Settings.LogToConsole)
        {
            _core.Logger.LogInformation("[BroadcastSystem] Duyuru yayınlandı: {Id} ({Channels})", ad.Id, ad.ChannelsSummary());
        }
    }

    // ========================== INSTANT ADMIN BROADCASTS ==========================

    /// <summary>
    /// Admin tarafindan komuttan veya menuden gonderilen anlik duyuruyu yayinlar.
    /// channel: "all", "chat", "center", "html", "alert"
    /// </summary>
    public int SendInstantBroadcast(
        string rawMessage,
        string channel = "all",
        string targetTeam = "",
        bool? usePrefixOverride = null,
        int? htmlDurationMs = null)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return 0;

        var serverValues = PlaceholderHelper.BuildServerValues(_core, _config);
        bool usePrefix = usePrefixOverride ?? _config.Settings.EnablePrefix;
        int duration = htmlDurationMs ?? _config.Settings.DefaultHtmlDurationMs;
        var normChannel = channel.Trim().ToLowerInvariant();
        int sentCount = 0;

        foreach (var p in _core.PlayerManager.GetAllPlayers())
        {
            if (!PlayerHelper.IsUsable(p) || p!.IsFakeClient) continue;
            var team = PlayerHelper.GetTeam(p);
            if (!string.IsNullOrWhiteSpace(targetTeam) && !team.Equals(targetTeam, StringComparison.OrdinalIgnoreCase))
                continue;

            var values = PlaceholderHelper.WithPlayerValues(serverValues, p, GetSession(p.Slot));

            try
            {
                if (normChannel is "all" or "chat" or "both")
                {
                    SendFormattedChat(p, rawMessage, values, team, usePrefix);
                }

                if (normChannel is "center")
                {
                    var text = ColorHelper.StripAll(PlaceholderHelper.Apply(rawMessage, values));
                    p.SendCenter(text);
                }

                if (normChannel is "all" or "html" or "both")
                {
                    if (!IsMenuOpen(p.Slot))
                    {
                        var htmlContent = BuildInstantHtmlBox(PlaceholderHelper.Apply(rawMessage, values));
                        p.SendCenterHTML(htmlContent, duration);
                    }
                }

                if (normChannel is "alert")
                {
                    var text = ColorHelper.StripAll(PlaceholderHelper.Apply(rawMessage, values));
                    p.SendAlert(text);
                }

                sentCount++;
            }
            catch { /* yoksay */ }
        }

        return sentCount;
    }

    private static string BuildInstantHtmlBox(string message)
    {
        // Eger admin dogrudan <font ...> gibi HTML yazdiysa oldugu gibi kullan; degilse sik bir kart icine sar.
        if (message.Contains('<') && message.Contains('>'))
            return message;

        var clean = WebUtility.HtmlEncode(ColorHelper.StripAll(message));
        return $"<font class='fontSize-m' color='#FACC15'><b>📢 SUNUCU DUYURUSU</b></font><br>" +
               $"<font class='fontSize-sm' color='#F8FAFC'><b>{clean}</b></font>";
    }

    // ========================== LIVE COUNTDOWN (!gerisayim) ==========================

    public void StartCountdown(int seconds, string title, string startedBy)
    {
        StopCountdown(silent: true);
        _countdownRemaining = Math.Clamp(seconds, 1, 600);
        _countdownTitle = string.IsNullOrWhiteSpace(title) ? "Geri Sayım" : title.Trim();
        int totalSeconds = _countdownRemaining;

        BroadcastChatToAll($"[gold]⏳ {_countdownTitle}[default] için [lime]{_countdownRemaining} saniyelik[default] geri sayım başladı! (Başlatan: [lightblue]{startedBy}[default])");

        RenderCountdownFrame(totalSeconds);

        _countdownCts = _core.Scheduler.RepeatBySeconds(1.0f, () =>
        {
            _countdownRemaining--;
            if (_countdownRemaining > 0)
            {
                RenderCountdownFrame(totalSeconds);
                if (_countdownRemaining <= 5 || _countdownRemaining % 10 == 0)
                    BroadcastChatToAll($"[gold]⏳ {_countdownTitle}:[default] Son [lime]{_countdownRemaining}[default] saniye!");
            }
            else
            {
                RenderCountdownFinished();
                BroadcastChatToAll($"[lime]🚀 {_countdownTitle} BAŞLADI![default]");
                StopCountdown(silent: true);
            }
        });
    }

    public void StopCountdown(bool silent = false)
    {
        try { _countdownCts?.Cancel(); } catch { /* yoksay */ }
        _countdownCts = null;
        if (!silent && _countdownRemaining > 0)
        {
            BroadcastChatToAll($"[lightred]⏳ '{_countdownTitle}' geri sayımı durduruldu.[default]");
        }
        _countdownRemaining = 0;
    }

    private void RenderCountdownFrame(int totalSeconds)
    {
        int filledBars = Math.Clamp((int)Math.Round((double)_countdownRemaining / Math.Max(1, totalSeconds) * 10), 0, 10);
        var bar = new string('█', filledBars) + new string('░', 10 - filledBars);
        var color = _countdownRemaining <= 5 ? "#F87171" : "#38BDF8";

        var html =
            $"<font class='fontSize-m' color='#FACC15'><b>⏳ {WebUtility.HtmlEncode(_countdownTitle)}</b></font><br>" +
            $"<font class='fontSize-l' color='{color}'><b>{_countdownRemaining} sn</b></font><br>" +
            $"<font class='fontSize-sm' color='#94A3B8'>[{bar}]</font>";

        foreach (var p in GetEligibleHumanPlayers())
        {
            if (!IsMenuOpen(p.Slot))
                p.SendCenterHTML(html, 1500);
        }
    }

    private void RenderCountdownFinished()
    {
        var html =
            $"<font class='fontSize-m' color='#4ADE80'><b>🚀 {WebUtility.HtmlEncode(_countdownTitle)}</b></font><br>" +
            $"<font class='fontSize-l' color='#FACC15'><b>BAŞLADI!</b></font>";

        foreach (var p in GetEligibleHumanPlayers())
        {
            if (!IsMenuOpen(p.Slot))
                p.SendCenterHTML(html, 3500);
            p.SendAlert($"🚀 {_countdownTitle} BAŞLADI!");
        }
    }

    // ========================== LIVE POLL / VOTING (!anket & !oy) ==========================

    public void StartPoll(string question, List<string> options, int durationSeconds, string startedBy)
    {
        StopPoll(announceResults: false);

        var cleanOptions = options
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Take(6)
            .ToList();

        if (cleanOptions.Count < 2)
        {
            cleanOptions = new List<string> { "Evet", "Hayır" };
        }

        int duration = Math.Clamp(durationSeconds, 10, 300);
        _activePoll = new ActivePoll
        {
            Question = question.Trim(),
            Options = cleanOptions,
            Votes = new int[cleanOptions.Count],
            EndsAtUtc = DateTime.UtcNow.AddSeconds(duration),
            StartedBy = startedBy
        };

        var voteCmd = _config.Commands.VoteCommand;
        BroadcastChatToAll($"[gold]📊 YENİ ANKET:[default] [lime]{_activePoll.Question}[default] ({duration} sn)");
        for (int i = 0; i < cleanOptions.Count; i++)
        {
            BroadcastChatToAll($"  [gold][{i + 1}][default] {cleanOptions[i]}  [grey](Oy vermek için: !{voteCmd} {i + 1})[default]");
        }

        RenderPollFrame(_activePoll);

        _pollCts = _core.Scheduler.RepeatBySeconds(1.0f, () =>
        {
            if (_activePoll == null) return;
            if (_activePoll.RemainingSeconds <= 0)
            {
                StopPoll(announceResults: true);
                return;
            }
            RenderPollFrame(_activePoll);
        });
    }

    public bool CastVote(IPlayer voter, int optionOneBased)
    {
        if (_activePoll == null)
        {
            Reply(voter, "[lightred]Şu anda aktif bir anket bulunmuyor.[default]");
            return false;
        }

        int idx = optionOneBased - 1;
        if (idx < 0 || idx >= _activePoll.Options.Count)
        {
            Reply(voter, $"[lightred]Geçersiz seçenek![default] Lütfen [gold]1[default] ile [gold]{_activePoll.Options.Count}[default] arasında bir sayı girin.");
            return false;
        }

        var sid = PlayerHelper.GetSteamId64(voter);
        if (sid != 0)
        {
            if (_activePoll.VoterChoicesBySid.TryGetValue(sid, out int prevIdx))
            {
                if (prevIdx == idx)
                {
                    Reply(voter, $"[grey]Zaten [gold]{_activePoll.Options[idx]}[grey] seçeneğine oy verdiniz.[default]");
                    return true;
                }
                _activePoll.Votes[prevIdx] = Math.Max(0, _activePoll.Votes[prevIdx] - 1);
            }
            _activePoll.VoterChoicesBySid[sid] = idx;
        }
        else
        {
            if (_activePoll.VoterChoicesBySlot.TryGetValue(voter.Slot, out int prevIdx))
            {
                if (prevIdx == idx)
                {
                    Reply(voter, $"[grey]Zaten [gold]{_activePoll.Options[idx]}[grey] seçeneğine oy verdiniz.[default]");
                    return true;
                }
                _activePoll.Votes[prevIdx] = Math.Max(0, _activePoll.Votes[prevIdx] - 1);
            }
            _activePoll.VoterChoicesBySlot[voter.Slot] = idx;
        }

        _activePoll.Votes[idx]++;
        Reply(voter, $"Oyunuz kaydedildi: [lime]{_activePoll.Options[idx]}[default] (Toplam oy: [gold]{_activePoll.TotalVotes}[default])");
        RenderPollFrame(_activePoll);
        return true;
    }

    public void StopPoll(bool announceResults)
    {
        try { _pollCts?.Cancel(); } catch { /* yoksay */ }
        _pollCts = null;

        var poll = _activePoll;
        _activePoll = null;

        if (poll == null || !announceResults) return;

        int total = poll.TotalVotes;
        int bestVote = poll.Votes.Length > 0 ? poll.Votes.Max() : 0;
        var winners = poll.Options
            .Where((_, i) => poll.Votes[i] == bestVote && bestVote > 0)
            .ToList();

        BroadcastChatToAll($"[gold]📊 ANKET SONA ERDİ:[default] [lime]{poll.Question}[default] (Toplam Oy: [gold]{total}[default])");
        for (int i = 0; i < poll.Options.Count; i++)
        {
            int count = poll.Votes[i];
            int pct = total > 0 ? (int)Math.Round(count * 100.0 / total) : 0;
            BroadcastChatToAll($"  • [gold]{poll.Options[i]}[default]: [lime]{count} oy[default] (%{pct})");
        }

        string winnerSummary = winners.Count switch
        {
            0 => "Oy kullanılmadı",
            1 => $"Kazanan: {winners[0]} ({bestVote} oy)",
            _ => $"Berabere: {string.Join(" / ", winners)} ({bestVote} oy)"
        };

        BroadcastChatToAll($"[lime]🏆 Sonuç:[default] [gold]{winnerSummary}[default]");

        var resultHtml =
            $"<font class='fontSize-m' color='#4ADE80'><b>📊 ANKET SONUCU</b></font><br>" +
            $"<font class='fontSize-sm' color='#F8FAFC'><b>{WebUtility.HtmlEncode(poll.Question)}</b></font><br>" +
            $"<font class='fontSize-sm' color='#FACC15'>🏆 {WebUtility.HtmlEncode(winnerSummary)}</font>";

        foreach (var p in GetEligibleHumanPlayers())
        {
            if (!IsMenuOpen(p.Slot))
                p.SendCenterHTML(resultHtml, 4500);
        }
    }

    private void RenderPollFrame(ActivePoll poll)
    {
        int total = poll.TotalVotes;
        var sb = new StringBuilder();
        sb.Append($"<font class='fontSize-m' color='#38BDF8'><b>📊 ANKET ({poll.RemainingSeconds} sn)</b></font><br>");
        sb.Append($"<font class='fontSize-sm' color='#FACC15'><b>{WebUtility.HtmlEncode(poll.Question)}</b></font><br>");

        for (int i = 0; i < poll.Options.Count; i++)
        {
            int v = poll.Votes[i];
            int pct = total > 0 ? (int)Math.Round(v * 100.0 / total) : 0;
            sb.Append($"<font class='fontSize-xs' color='#F8FAFC'><b>!{_config.Commands.VoteCommand} {i + 1}</b> • {WebUtility.HtmlEncode(poll.Options[i])}: <font color='#4ADE80'><b>{v} oy (%{pct})</b></font></font><br>");
        }

        var html = sb.ToString();
        foreach (var p in GetEligibleHumanPlayers())
        {
            if (!IsMenuOpen(p.Slot))
                p.SendCenterHTML(html, 1500);
        }
    }

    // ================================ HELPERS ================================

    public void Reply(IPlayer? player, string text)
    {
        if (!PlayerHelper.IsUsable(player)) return;
        var raw = PrependPrefixIfEnabled(text, _config.Settings.EnablePrefix);
        var line = Helper.Colored(ColorHelper.NormalizeTags(raw, PlayerHelper.GetTeam(player)));
        player!.SendChat(line);
    }

    public void BroadcastChatToAll(string text)
    {
        var raw = PrependPrefixIfEnabled(text, _config.Settings.EnablePrefix);
        var line = Helper.Colored(ColorHelper.NormalizeTags(raw));
        BroadcastChatLineToAll(line);
    }

    private void BroadcastChatLineToAll(string coloredLine)
    {
        foreach (var p in GetEligibleHumanPlayers())
        {
            try { p.SendChat(coloredLine); } catch { /* yoksay */ }
        }
    }

    private void SendFormattedChat(IPlayer player, string rawText, IReadOnlyDictionary<string, string> values, string team, bool usePrefix)
    {
        var expanded = PlaceholderHelper.Apply(rawText.Replace("\\n", "\n"), values);
        var lines = expanded.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0) continue;
            var withPrefix = PrependPrefixIfEnabled(trimmed, usePrefix);
            var final = Helper.Colored(ColorHelper.NormalizeTags(withPrefix, team));
            player.SendChat(final);
        }
    }

    private string PrependPrefixIfEnabled(string message, bool usePrefix)
    {
        if (!usePrefix || string.IsNullOrWhiteSpace(_config.Settings.Prefix))
            return message;
        return $"{_config.Settings.Prefix} {message}";
    }

    private bool MatchesPermission(IPlayer player, string? permissionFilter)
    {
        if (string.IsNullOrWhiteSpace(permissionFilter)) return true;

        var filter = permissionFilter.Trim();
        bool negate = filter.StartsWith('!');
        if (negate) filter = filter[1..].Trim();
        if (string.IsNullOrEmpty(filter)) return true;

        ulong sid = PlayerHelper.GetSteamId64(player);
        bool hasPerm = false;
        if (sid != 0)
        {
            try
            {
                hasPerm = _core.Permission.PlayerHasPermission(sid, filter);
            }
            catch
            {
                hasPerm = false;
            }
        }

        return negate ? !hasPerm : hasPerm;
    }

    private List<IPlayer> GetEligibleHumanPlayers() =>
        _core.PlayerManager.GetAllPlayers()
            .Where(p => PlayerHelper.IsUsable(p) && !p!.IsFakeClient)
            .Cast<IPlayer>()
            .ToList();

    private bool IsMenuOpen(int slot) => _isMenuOpenPredicate?.Invoke(slot) ?? false;

    private bool IsCenterHtmlBusy(int slot) =>
        IsMenuOpen(slot) || IsCountdownActive || _activePoll != null;
}

