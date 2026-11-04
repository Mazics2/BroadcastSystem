# BroadcastSystem - Geliştirme ve Mimari Notları

Bu belge, `CS2_Broadcast` eklentisinin `TagSystem` mimarisiyle sıfırdan yeniden yazılmış tam teşekküllü sürümü olan **`BroadcastSystem`** eklentisinin mimarisini, özelliklerini ve teknik detaylarını özetler.

---

## 1. Proje Özeti ve Mimari

`BroadcastSystem`, **SwiftlyS2 v1.5.0 (.NET 10.0)** üzerinde çalışan, TOML tabanlı (`Tomlyn 0.17.0`) ve `MaxMind.GeoIP2` destekli bir **Zamanlı Otomatik Duyuru, Gelişmiş Hoş Geldin/Ayrılma, Anlık Admin Anonsu, Canlı Geri Sayım ve Oylama (Anket)** sistemidir.

### Klasör ve Dosya Yapısı
* **[`BroadcastSystem.csproj`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/BroadcastSystem.csproj)**: `net10.0`, `SwiftlyS2.CS2` (1.5.0), `Tomlyn` (0.17.0) ve `MaxMind.GeoIP2` (5.2.0) referansları. Build sonrası yerel sunucu klasörü mevcutsa otomatik kopyalama (`DeployToLocalServer`) hedefi içerir.
* **[`Plugin.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Plugin.cs)**: Ana eklenti giriş noktası (`BasePlugin`). Yaşam döngüsü, event abonelikleri, zamanlayıcılar ve oyun içi/konsol komutları.
* **Config/**
  * **[`PluginConfig.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Config/PluginConfig.cs)**: `Settings`, `Welcome`, `Commands` ve `Ads` (`[[ads]]`) bölümlerini barındıran yapılandırma modeli.
  * **[`ConfigLoader.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Config/ConfigLoader.cs)**: `broadcastconfig.toml` okuma (`Load`) ve atomik kaydetme (`Save`) sınıfı.
  * **[`DefaultConfigTemplate.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Config/DefaultConfigTemplate.cs)**: İlk çalıştırmada oluşturulan kapsamlı Türkçe açıklamalı varsayılan `broadcastconfig.toml` şablonu.
* **Models/**
  * **[`AdEntry.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Models/AdEntry.cs)**: Zamanlı duyuru öğesi (`Id`, `Name`, `Enabled`, `Chat`, `Center`, `Html`, `Alert`, `Teams`, `Maps`, `Permission`, `Weight`).
  * **[`PlayerSessionInfo.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Models/PlayerSessionInfo.cs)**: Oyuncunun giriş zamanı, ülke bilgisi, SteamID ve hoş geldin durumunu tutan oturum modeli. Oyuncu çıktığında `IPlayer` geçersizleşse bile `{NAME}`, `{STEAMID2}`, `{COUNTRY}` ve `{PLAYTIME}` değerlerinin ayrılma mesajında eksiksiz görünmesini sağlar.
  * **[`ActivePoll.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Models/ActivePoll.cs)**: Canlı anket/oylama durumu ve oy dağılımı modeli.
* **Core/**
  * **[`BroadcastService.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Core/BroadcastService.cs)**: Zamanlı duyuru döngüsü (sıralı veya ağırlıklı rastgele), gecikmeli hoş geldin/ayrılma mesajları, anlık admin duyuruları, canlı geri sayım (`!gerisayim`) ve canlı oylama (`!anket` / `!oy`) motoru.
  * **[`BroadcastAdminMenu.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Core/BroadcastAdminMenu.cs)**: `CenterHTML` ve `W/S/A/D/E/R` tuş navigasyonuyla çalışan oyun içi görsel yönetim menüsü (`!duyurumenu`).
* **Utils/**
  * **[`ColorHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Utils/ColorHelper.cs)**: Hem `TagSystem` stilindeki `[red]`, `[gold]`, `[#RRGGBB]` etiketlerini hem de eski `CS2_Broadcast` stilindeki `{RED}`, `{BLUE}`, `{DEFAULT}` etiketlerini destekleyen renk dönüştürücü.
  * **[`PlaceholderHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Utils/PlaceholderHelper.cs)**: Tek geçişli sunucu ve oyuncu değişkeni değiştirici.
  * **[`GeoIpHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Utils/GeoIpHelper.cs)**: `GeoLite2-Country.mmdb` varsa IP'den ülke çözen, yoksa istemci dilinden (`PlayerLanguage`) otomatik tahmin yapan güvenli ülke çözümleyici.
  * **[`PlayerHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Utils/PlayerHelper.cs)** & **[`SteamIdHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/BroadcastSystem/Utils/SteamIdHelper.cs)**: Güvenli oyuncu verisi ve SteamID2/3/64 dönüştürücüler.

---

## 2. Komutlar

| Komut | Alternatifler | Açıklama |
| :--- | :--- | :--- |
| `!duyurumenu` | `!bmenu`, `!admenu` | Görsel `W/S/A/D/E/R` admin yönetim menüsünü açar. |
| `!duyuru <mesaj>` | `!bchat`, `!anons` | Tüm sunucuya renkli Chat + CenterHTML anons geçer. |
| `!centerduyuru <mesaj>` | `!bcenter` | Ekranın ortasında (Center) anons geçer. |
| `!htmlduyuru <mesaj>` | `!bhtml` | Zengin CenterHTML kutusuyla anons geçer. |
| `!alertduyuru <mesaj>` | `!balert` | CS2 Alert panelinde anons geçer. |
| `!gerisayim <sn> "[başlık]"` | `!countdown`, `!sayac` | Ekran ortasında ilerleme çubuklu canlı geri sayım başlatır (`!gerisayim dur` ile durdurulur). |
| `!anket "<soru>" "<s1>" "<s2>"` | `!oylama`, `!poll` | Canlı oylama başlatır (`!anket bitir` ile sonlandırılır). |
| `!oy <numara>` | `!vote` | Aktif ankette oy kullanır (tüm oyuncular kullanabilir). |
| `!sonrakiduyuru` | `!nextad`, `!adtest` | Sıradaki zamanlı duyuruyu beklemeden hemen yayınlar. |
| `!duyurureload` | `!broadcast_reload` | `broadcastconfig.toml` dosyasını yeniden yükler. |

