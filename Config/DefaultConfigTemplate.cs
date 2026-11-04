namespace BroadcastSystem.Config;

/// <summary>Ilk calistirmada olusturulan, kapsamli Turkce aciklamali broadcastconfig.toml sablonu.</summary>
public static class DefaultConfigTemplate
{
    public const string Content = """
# ==============================================================================
#  BroadcastSystem - Otomatik Duyuru, Hoş Geldin, Anket & Anons Sistemi (SwiftlyS2)
# ==============================================================================
#  Oyun içi görsel admin menüsü (W/S/A/D/E/R):  !duyurumenu  (veya !bmenu)
#
#  Hızlı Admin Komutları:
#    !duyuru <mesaj>                          -> Tüm sunucuya renkli Chat + HTML anons geçer
#    !centerduyuru <mesaj>                    -> Ekranın ortasında (Center) anons geçer
#    !htmlduyuru <mesaj>                      -> Zengin CenterHTML kutusuyla anons geçer
#    !alertduyuru <mesaj>                     -> CS2 Alert panelinde anons geçer
#    !gerisayim <saniye> "<başlık>"           -> Ekran ortasında canlı geri sayım başlatır
#    !anket "<soru>" "<seçenek1>" "<seçenek2>"-> Canlı oylama başlatır (oyuncular !oy 1 yazar)
#    !sonrakiduyuru                           -> Sıradaki zamanlı duyuruyu beklemeden yayınlar
#    !duyurureload                            -> Yapılandırmayı (broadcastconfig.toml) yeniden yükler
#
#  Desteklenen Değişkenler (Placeholders):
#    Sunucu : {SERVERNAME}, {SERVERIP}, {DOMAIN}, {MAP}, {TIME}, {SHORTTIME}, {DATE},
#             {DATETIME}, {PLAYERS}, {BOTS}, {TOTALPLAYERS}, {MAXPLAYERS},
#             {T_COUNT}, {CT_COUNT}, {SPEC_COUNT}, {DISCORD}, {WEBSITE}, {PREFIX}
#    Oyuncu : {NAME}, {STEAMID2}, {STEAMID3}, {STEAMID64}, {SLOT}, {USERID}, {TEAM},
#             {PING}, {SCORE}, {KILLS}, {DEATHS}, {MVPS}, {COUNTRY}, {COUNTRY_CODE},
#             {IP}, {PLAYTIME}
#
#  Desteklenen Renkler (hem [red] hem de {RED} formatı çalışır):
#    [default], [white], [red], [lightred], [darkred], [gold], [yellow], [lightyellow],
#    [orange], [green], [lime], [olive], [lightblue], [blue], [darkblue], [purple],
#    [lightpurple], [magenta], [grey], [silver], [bluegrey], [teamcolor] veya [#RRGGBB]
# ==============================================================================

[settings]
enable_ads = true
ad_interval_seconds = 45.0
# "sequential" = sırayla döner | "random" = ağırlıklı (weight) rastgele seçer
rotation_mode = "sequential"
min_players_for_ads = 0
skip_bots = true
show_html_when_dead_only = false
default_html_duration_ms = 5000
enable_prefix = true
prefix = "[gold][Duyuru][default]"
# Boş bırakılırsa sunucunun 'hostname' convar değeri otomatik okunur
server_name_override = ""
server_domain = "oyun.sunucunuz.com"
discord_invite = "discord.gg/sunucu"
website_url = "www.sunucunuz.com"
geoip_database_path = "GeoLite2-Country.mmdb"
freeze_while_menu_open = true
log_to_console = false
debug = false

[welcome]
enable_welcome = true
welcome_delay_seconds = 3.0
# Giriş yapan oyuncuya özel hoş geldin mesajının kanalı: "both" (Chat+HTML), "chat", "html", "center", "alert", "none"
private_channel = "both"
private_chat_lines = [
    "[gold]══════════════════════════════════════════════════[default]",
    "👋 Hoş geldin [lime]{NAME}[default] ([gold]{COUNTRY}[default])! Sunucu: [lightblue]{SERVERNAME}[default]",
    "🗺️ Harita: [gold]{MAP}[default] | 👥 Oyuncular: [lime]{PLAYERS}/{MAXPLAYERS}[default] | 💬 Discord: [lightblue]{DISCORD}[default]",
    "[gold]══════════════════════════════════════════════════[default]"
]
private_html_message = "<font class='fontSize-m' color='#38BDF8'><b>✦ HOŞ GELDİN {NAME} ✦</b></font><br><font class='fontSize-sm' color='#F8FAFC'>{SERVERNAME}</font><br><font class='fontSize-xs' color='#FACC15'>Harita: {MAP} • Oyuncu: {PLAYERS}/{MAXPLAYERS} • Ülke: {COUNTRY}</font><br><font class='fontSize-xs' color='#4ADE80'>Discord: {DISCORD} • İyi oyunlar dileriz!</font>"
private_html_duration_ms = 6000

# Tüm sunucuya oyuncunun katıldığını/ayrıldığını duyur
enable_public_join = true
public_join_message = "[lime]➕ {NAME}[default] ([gold]{COUNTRY}[default]) sunucuya katıldı! ([lightblue]{PLAYERS}/{MAXPLAYERS}[default])"
enable_public_leave = true
public_leave_message = "[lightred]➖ {NAME}[default] ([grey]{STEAMID2}[default]) sunucudan ayrıldı. (Oynama: [gold]{PLAYTIME}[default])"

[commands]
menu_command = "duyurumenu"
say_broadcast_command = "duyuru"
center_broadcast_command = "centerduyuru"
html_broadcast_command = "htmlduyuru"
alert_broadcast_command = "alertduyuru"
countdown_command = "gerisayim"
poll_command = "anket"
vote_command = "oy"
next_ad_command = "sonrakiduyuru"
reload_command = "duyurureload"
admin_permission = "broadcast.admin"

# ---------------------------- ZAMANLI DUYURULAR -------------------------------

[[ads]]
id = "hosgeldin_bilgi"
name = "Sunucu Durumu"
enabled = true
chat = "[lime]Sunucu Durumu:[default] [gold]{PLAYERS}/{MAXPLAYERS}[default] Oyuncu | Harita: [lightblue]{MAP}[default] | Saat: [gold]{TIME}[default]"
html = "<font class='fontSize-m' color='#38BDF8'><b>✦ {SERVERNAME} ✦</b></font><br><font class='fontSize-sm' color='#F8FAFC'>Aktif Oyuncu: <font color='#4ADE80'><b>{PLAYERS}/{MAXPLAYERS}</b></font> • Harita: <font color='#FACC15'><b>{MAP}</b></font></font>"
html_duration_ms = 4500
weight = 1

[[ads]]
id = "discord"
name = "Discord Topluluğu"
enabled = true
chat = "Çekilişler, duyurular ve destek için Discord sunucumuza katılın: [lightblue]{DISCORD}[default]"
center = "💬 Discord: {DISCORD}"
weight = 1

[[ads]]
id = "vip_tanitim"
name = "VIP Ayrıcalıkları"
enabled = true
# Başında '!' olduğu için yalnızca 'vip' yetkisine SAHİP OLMAYAN oyunculara gösterilir
permission = "!vip"
chat = "[gold]★ VIP Üyelik ★[default] Özel etiketler ve ayrıcalıklar için: [lime]{WEBSITE}[default]"
html = "<font class='fontSize-m' color='#FACC15'><b>★ VIP AYRICALIKLARI ★</b></font><br><font class='fontSize-sm' color='#E2E8F0'>Sayın <b>{NAME}</b>, VIP avantajlarını incelemek için: <font color='#38BDF8'><b>{WEBSITE}</b></font></font>"
html_duration_ms = 5000
weight = 1

[[ads]]
id = "kurallar"
name = "Sunucu Kuralları"
enabled = true
chat = "[lightred]Lütfen sunucu kurallarına uyun![default] Hile, küfür ve toksik davranışlar kalıcı yasaklanma sebebidir."
alert = "⚖️ Kurallara uyun ve adil oynayın! İyi eğlenceler!"
weight = 1
""";
}

