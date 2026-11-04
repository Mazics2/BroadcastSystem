using System.Globalization;
using System.Text;
using BroadcastSystem.Models;
using Tomlyn;
using Tomlyn.Model;

namespace BroadcastSystem.Config;

/// <summary>
/// broadcastconfig.toml dosyasini okur, dogrular ve atomik olarak kaydeder.
/// Ayrica eski CS2_Broadcast config.jsonc dosyasi mevcutsa ve TOML henuz olusturulmamissa
/// eski reklamlari otomatik olarak ice aktarabilir.
/// </summary>
public class ConfigLoader
{
    public string ConfigPath { get; }
    public List<string> Warnings { get; } = new();

    public ConfigLoader(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        ConfigPath = Path.Combine(configDirectory, "broadcastconfig.toml");
    }

    public PluginConfig Load()
    {
        Warnings.Clear();

        if (!File.Exists(ConfigPath))
        {
            File.WriteAllText(ConfigPath, DefaultConfigTemplate.Content, new UTF8Encoding(false));
        }

        var content = File.ReadAllText(ConfigPath, Encoding.UTF8);
        return Parse(content);
    }

    public void Save(PluginConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ==============================================================================");
        sb.AppendLine("#  BroadcastSystem - Otomatik Duyuru, Hoş Geldin, Anket & Anons Sistemi");
        sb.AppendLine("#  Oyun içi görsel admin menüsü: !duyurumenu");
        sb.AppendLine("#  Hızlı komutlar: !duyuru, !centerduyuru, !htmlduyuru, !alertduyuru, !gerisayim, !anket");
        sb.AppendLine("# ==============================================================================");
        sb.AppendLine();

        var st = config.Settings;
        sb.AppendLine("[settings]");
        sb.AppendLine($"enable_ads = {B(st.EnableAds)}");
        sb.AppendLine($"ad_interval_seconds = {st.AdIntervalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"rotation_mode = {Q(st.RotationMode)}");
        sb.AppendLine($"min_players_for_ads = {st.MinPlayersForAds}");
        sb.AppendLine($"skip_bots = {B(st.SkipBots)}");
        sb.AppendLine($"show_html_when_dead_only = {B(st.ShowHtmlWhenDeadOnly)}");
        sb.AppendLine($"default_html_duration_ms = {st.DefaultHtmlDurationMs}");
        sb.AppendLine($"enable_prefix = {B(st.EnablePrefix)}");
        sb.AppendLine($"prefix = {Q(st.Prefix)}");
        sb.AppendLine($"server_name_override = {Q(st.ServerNameOverride)}");
        sb.AppendLine($"server_domain = {Q(st.ServerDomain)}");
        sb.AppendLine($"discord_invite = {Q(st.DiscordInvite)}");
        sb.AppendLine($"website_url = {Q(st.WebsiteUrl)}");
        sb.AppendLine($"geoip_database_path = {Q(st.GeoIpDatabasePath)}");
        sb.AppendLine($"freeze_while_menu_open = {B(st.FreezeWhileMenuOpen)}");
        sb.AppendLine($"log_to_console = {B(st.LogToConsole)}");
        sb.AppendLine($"debug = {B(st.Debug)}");
        sb.AppendLine();

        var w = config.Welcome;
        sb.AppendLine("[welcome]");
        sb.AppendLine($"enable_welcome = {B(w.EnableWelcome)}");
        sb.AppendLine($"welcome_delay_seconds = {w.WelcomeDelaySeconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"private_channel = {Q(w.PrivateChannel)}");
        sb.AppendLine("private_chat_lines = [");
        foreach (var line in w.PrivateChatLines)
            sb.AppendLine($"    {Q(line)},");
        sb.AppendLine("]");
        sb.AppendLine($"private_html_message = {Q(w.PrivateHtmlMessage)}");
        sb.AppendLine($"private_html_duration_ms = {w.PrivateHtmlDurationMs}");
        sb.AppendLine($"enable_public_join = {B(w.EnablePublicJoin)}");
        sb.AppendLine($"public_join_message = {Q(w.PublicJoinMessage)}");
        sb.AppendLine($"enable_public_leave = {B(w.EnablePublicLeave)}");
        sb.AppendLine($"public_leave_message = {Q(w.PublicLeaveMessage)}");
        sb.AppendLine();

        var cm = config.Commands;
        sb.AppendLine("[commands]");
        sb.AppendLine($"menu_command = {Q(cm.MenuCommand)}");
        sb.AppendLine($"say_broadcast_command = {Q(cm.SayBroadcastCommand)}");
        sb.AppendLine($"center_broadcast_command = {Q(cm.CenterBroadcastCommand)}");
        sb.AppendLine($"html_broadcast_command = {Q(cm.HtmlBroadcastCommand)}");
        sb.AppendLine($"alert_broadcast_command = {Q(cm.AlertBroadcastCommand)}");
        sb.AppendLine($"countdown_command = {Q(cm.CountdownCommand)}");
        sb.AppendLine($"poll_command = {Q(cm.PollCommand)}");
        sb.AppendLine($"vote_command = {Q(cm.VoteCommand)}");
        sb.AppendLine($"next_ad_command = {Q(cm.NextAdCommand)}");
        sb.AppendLine($"reload_command = {Q(cm.ReloadCommand)}");
        sb.AppendLine($"admin_permission = {Q(cm.AdminPermission)}");
        sb.AppendLine();

        if (config.Ads.Count > 0)
        {
            sb.AppendLine("# ---------------------------- ZAMANLI DUYURULAR -------------------------------");
            foreach (var ad in config.Ads)
            {
                sb.AppendLine("[[ads]]");
                sb.AppendLine($"id = {Q(ad.Id)}");
                if (!string.IsNullOrWhiteSpace(ad.Name)) sb.AppendLine($"name = {Q(ad.Name)}");
                sb.AppendLine($"enabled = {B(ad.Enabled)}");
                if (ad.UsePrefix.HasValue) sb.AppendLine($"use_prefix = {B(ad.UsePrefix.Value)}");
                if (!string.IsNullOrWhiteSpace(ad.Chat)) sb.AppendLine($"chat = {Q(ad.Chat)}");
                if (!string.IsNullOrWhiteSpace(ad.Center)) sb.AppendLine($"center = {Q(ad.Center)}");
                if (!string.IsNullOrWhiteSpace(ad.Html)) sb.AppendLine($"html = {Q(ad.Html)}");
                if (!string.IsNullOrWhiteSpace(ad.Alert)) sb.AppendLine($"alert = {Q(ad.Alert)}");
                if (ad.HtmlDurationMs.HasValue) sb.AppendLine($"html_duration_ms = {ad.HtmlDurationMs.Value}");
                if (ad.Teams.Count > 0) sb.AppendLine($"teams = [{string.Join(", ", ad.Teams.Select(Q))}]");
                if (ad.Maps.Count > 0) sb.AppendLine($"maps = [{string.Join(", ", ad.Maps.Select(Q))}]");
                if (!string.IsNullOrWhiteSpace(ad.Permission)) sb.AppendLine($"permission = {Q(ad.Permission)}");
                sb.AppendLine($"weight = {Math.Max(1, ad.Weight)}");
                sb.AppendLine();
            }
        }

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    private PluginConfig Parse(string toml)
    {
        var syntax = Toml.Parse(toml, ConfigPath);
        if (syntax.HasErrors)
        {
            var errors = string.Join("; ", syntax.Diagnostics.Select(d => d.ToString()));
            throw new InvalidDataException("TOML sözdizimi hatası: " + errors);
        }

        var model = syntax.ToModel();
        var config = new PluginConfig();

        if (Table(model, "settings") is { } s)
        {
            var st = config.Settings;
            st.EnableAds = Bool(s, "enable_ads", st.EnableAds);
            st.AdIntervalSeconds = Math.Max(3.0f, (float)Double(s, "ad_interval_seconds", st.AdIntervalSeconds));
            st.RotationMode = NormalizeRotation(Str(s, "rotation_mode") ?? st.RotationMode);
            st.MinPlayersForAds = Math.Max(0, Int(s, "min_players_for_ads", st.MinPlayersForAds));
            st.SkipBots = Bool(s, "skip_bots", st.SkipBots);
            st.ShowHtmlWhenDeadOnly = Bool(s, "show_html_when_dead_only", st.ShowHtmlWhenDeadOnly);
            st.DefaultHtmlDurationMs = Math.Clamp(Int(s, "default_html_duration_ms", st.DefaultHtmlDurationMs), 1000, 30000);
            st.EnablePrefix = Bool(s, "enable_prefix", st.EnablePrefix);
            st.Prefix = Str(s, "prefix") ?? st.Prefix;
            st.ServerNameOverride = Str(s, "server_name_override") ?? st.ServerNameOverride;
            st.ServerDomain = Str(s, "server_domain") ?? st.ServerDomain;
            st.DiscordInvite = Str(s, "discord_invite") ?? st.DiscordInvite;
            st.WebsiteUrl = Str(s, "website_url") ?? st.WebsiteUrl;
            st.GeoIpDatabasePath = Str(s, "geoip_database_path") ?? st.GeoIpDatabasePath;
            st.FreezeWhileMenuOpen = Bool(s, "freeze_while_menu_open", st.FreezeWhileMenuOpen);
            st.LogToConsole = Bool(s, "log_to_console", st.LogToConsole);
            st.Debug = Bool(s, "debug", st.Debug);
        }

        if (Table(model, "welcome") is { } w)
        {
            var wl = config.Welcome;
            wl.EnableWelcome = Bool(w, "enable_welcome", wl.EnableWelcome);
            wl.WelcomeDelaySeconds = Math.Clamp((float)Double(w, "welcome_delay_seconds", wl.WelcomeDelaySeconds), 0.5f, 60.0f);
            wl.PrivateChannel = (Str(w, "private_channel") ?? wl.PrivateChannel).Trim().ToLowerInvariant();
            if (StrList(w, "private_chat_lines") is { } lines) wl.PrivateChatLines = lines;
            wl.PrivateHtmlMessage = Str(w, "private_html_message") ?? wl.PrivateHtmlMessage;
            wl.PrivateHtmlDurationMs = Math.Clamp(Int(w, "private_html_duration_ms", wl.PrivateHtmlDurationMs), 1000, 30000);
            wl.EnablePublicJoin = Bool(w, "enable_public_join", wl.EnablePublicJoin);
            wl.PublicJoinMessage = Str(w, "public_join_message") ?? wl.PublicJoinMessage;
            wl.EnablePublicLeave = Bool(w, "enable_public_leave", wl.EnablePublicLeave);
            wl.PublicLeaveMessage = Str(w, "public_leave_message") ?? wl.PublicLeaveMessage;
        }

        if (Table(model, "commands") is { } cm)
        {
            var co = config.Commands;
            co.MenuCommand = Str(cm, "menu_command") ?? co.MenuCommand;
            co.SayBroadcastCommand = Str(cm, "say_broadcast_command") ?? co.SayBroadcastCommand;
            co.CenterBroadcastCommand = Str(cm, "center_broadcast_command") ?? co.CenterBroadcastCommand;
            co.HtmlBroadcastCommand = Str(cm, "html_broadcast_command") ?? co.HtmlBroadcastCommand;
            co.AlertBroadcastCommand = Str(cm, "alert_broadcast_command") ?? co.AlertBroadcastCommand;
            co.CountdownCommand = Str(cm, "countdown_command") ?? co.CountdownCommand;
            co.PollCommand = Str(cm, "poll_command") ?? co.PollCommand;
            co.VoteCommand = Str(cm, "vote_command") ?? co.VoteCommand;
            co.NextAdCommand = Str(cm, "next_ad_command") ?? co.NextAdCommand;
            co.ReloadCommand = Str(cm, "reload_command") ?? co.ReloadCommand;
            co.AdminPermission = Str(cm, "admin_permission") ?? co.AdminPermission;
        }

        if (model.TryGetValue("ads", out var adsObj) && adsObj is TomlTableArray adsArray)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (var at in adsArray)
            {
                index++;
                var ad = new AdEntry
                {
                    Id = (Str(at, "id") ?? $"ad_{index}").Trim(),
                    Name = Str(at, "name") ?? string.Empty,
                    Enabled = Bool(at, "enabled", true),
                    Chat = Str(at, "chat"),
                    Center = Str(at, "center"),
                    Html = Str(at, "html"),
                    Alert = Str(at, "alert"),
                    Permission = Str(at, "permission"),
                    Weight = Math.Max(1, Int(at, "weight", 1)),
                };

                if (at.ContainsKey("use_prefix"))
                    ad.UsePrefix = Bool(at, "use_prefix", true);
                if (at.ContainsKey("html_duration_ms"))
                    ad.HtmlDurationMs = Math.Clamp(Int(at, "html_duration_ms", config.Settings.DefaultHtmlDurationMs), 1000, 30000);
                if (StrList(at, "teams") is { } teams)
                    ad.Teams = teams.Select(NormalizeTeam).Where(x => x.Length > 0).ToList();
                if (StrList(at, "maps") is { } maps)
                    ad.Maps = maps.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

                if (string.IsNullOrWhiteSpace(ad.Id))
                    ad.Id = $"ad_{index}";

                if (!seenIds.Add(ad.Id))
                {
                    var uniqueId = $"{ad.Id}_{index}";
                    Warnings.Add($"[[ads]] '{ad.Id}' birden fazla kez tanımlı, '{uniqueId}' olarak yeniden adlandırıldı.");
                    ad.Id = uniqueId;
                    seenIds.Add(ad.Id);
                }

                if (!ad.HasAnyContent())
                    Warnings.Add($"[[ads]] '{ad.Id}': chat/center/html/alert alanlarının tümü boş.");

                config.Ads.Add(ad);
            }
        }

        return config;
    }

    private static string NormalizeRotation(string mode) =>
        mode.Trim().Equals("random", StringComparison.OrdinalIgnoreCase) ? "random" : "sequential";

    private static string NormalizeTeam(string t) => t.Trim().ToUpperInvariant() switch
    {
        "T" or "TERRORIST" or "TR" => "T",
        "CT" or "COUNTERTERRORIST" or "COUNTER-TERRORIST" => "CT",
        "SPEC" or "SPECTATOR" or "SPECTATORS" => "SPEC",
        _ => string.Empty
    };

    private static string B(bool v) => v ? "true" : "false";
    private static string Q(string? s) =>
        $"\"{(s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")}\"";

    private static TomlTable? Table(TomlTable model, string key) =>
        model.TryGetValue(key, out var o) ? o as TomlTable : null;

    private static string? Str(TomlTable t, string key) =>
        t.TryGetValue(key, out var o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : null;

    private static bool Bool(TomlTable t, string key, bool fallback)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return fallback;
        if (o is bool b) return b;
        return bool.TryParse(o.ToString(), out var r) ? r : fallback;
    }

    private static int Int(TomlTable t, string key, int fallback)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return fallback;
        try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    private static double Double(TomlTable t, string key, double fallback)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return fallback;
        try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    private static List<string>? StrList(TomlTable t, string key)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return null;
        if (o is TomlArray arr)
            return arr.Where(x => x != null).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)!).ToList();
        if (o is string s) return new List<string> { s };
        return null;
    }
}

