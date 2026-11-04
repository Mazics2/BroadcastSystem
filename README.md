# 📢 BroadcastSystem (SwiftlyS2 CS2 Duyuru ve Anons Sistemi)

Counter-Strike 2 (CS2) ve **SwiftlyS2 (.NET 10 / v1.5.0+)** altyapısı için geliştirilmiş, yüksek performanslı, TOML tabanlı, GeoIP destekli gelişmiş zamanlı duyuru, hoş geldin/ayrılma, admin anonsu, canlı geri sayım ve oylama eklentisidir.

---

## ✨ Özellikler

- **⏰ Zamanlı Otomatik Duyurular:**
  - Sıralı (sequential) veya ağırlıklı rastgele (weighted random) duyuru döngüsü.
  - Aynı anda veya ayrı ayrı Sohbet (Chat), Ekran Ortası (Center), Zengin Panorama HTML (CenterHTML) ve CS2 Alert kutusu çıktıları.
  - Takım bazlı (T, CT, Spec) ve harita bazlı (`de_dust2`, `surf_*`) filtreleme.
  - Yetki bazlı duyuru gösterimi (`@admin`, `#vip` vb.).
- **👋 Gelişmiş Hoş Geldin ve Ayrılma Bildirimleri:**
  - Oyuncu bağlandığında gecikmeli hoş geldin mesajı ve ses efekti.
  - Ayrılma anında oturum süre takibi (`{PLAYTIME}`) ve SteamID/ülke bilgileri.
- **🌍 GeoIP Ülke Tespiti (`MaxMind.GeoIP2`):**
  - `GeoLite2-Country.mmdb` dosyasıyla IP'den ülke bayrağı/kodu çözümleme (`{COUNTRY}`, `{COUNTRY_CODE}`).
  - Veritabanı yoksa istemci dilinden (`PlayerLanguage`) otomatik güvenli ülke tahmini.
- **🎮 Oyun İçi Görsel Admin Menüsü (`!duyurumenu`):**
  - Panorama CenterHTML ve `W / S / A / D / E / R` tuş navigasyonuyla çalışan etkileşimli yönetim menüsü.
- **⏱️ Canlı Geri Sayım (`!gerisayim`):**
  - Ekran ortasında saniye saniye canlı ilerleme çubuğuyla animasyonlu geri sayım.
- **🗳️ Canlı Oylama / Anket (`!anket` & `!oy`):**
  - Sunucu içi anlık oylama başlatma ve canlı oy dağılımı gösterimi.
- **🎨 Zengin Renk ve Değişken Desteği:**
  - Hem modern `[red]`, `[gold]`, `[#RRGGBB]` etiketleri hem de `{RED}`, `{BLUE}` formatları.
  - `{NAME}`, `{STEAMID2}`, `{STEAMID64}`, `{IP}`, `{TIME}`, `{DATE}`, `{MAP}`, `{PLAYERS}`, `{MAXPLAYERS}`, `{NEXTMAP}` değişkenleri.

---

## 🕹️ Komutlar

| Komut | Kısayollar | Yetki | Açıklama |
| :--- | :--- | :--- | :--- |
| `!duyurumenu` | `!bmenu`, `!admenu` | `broadcast.admin` | Görsel `W/S/A/D/E/R` admin yönetim menüsünü açar. |
| `!duyuru <mesaj>` | `!bchat`, `!anons` | `broadcast.admin` | Tüm sunucuya renkli Chat + CenterHTML anons geçer. |
| `!centerduyuru <mesaj>` | `!bcenter` | `broadcast.admin` | Ekranın ortasında (Center) anons geçer. |
| `!htmlduyuru <mesaj>` | `!bhtml` | `broadcast.admin` | Zengin CenterHTML kutusuyla anons geçer. |
| `!alertduyuru <mesaj>` | `!balert` | `broadcast.admin` | CS2 Alert panelinde anons geçer. |
| `!gerisayim <sn> "[başlık]"` | `!sayac` | `broadcast.admin` | Canlı geri sayım başlatır (`!gerisayim dur` ile durdurulur). |
| `!anket "<soru>" "<s1>" "<s2>"` | `!poll` | `broadcast.admin` | Canlı oylama başlatır (`!anket bitir` ile sonlandırılır). |
| `!oy <numara>` | `!vote` | Herkes | Aktif ankette oy kullanır. |
| `!sonrakiduyuru` | `!nextad` | `broadcast.admin` | Sıradaki zamanlı duyuruyu anında yayınlar. |
| `!duyurureload` | `!broadcast_reload` | `broadcast.admin` | `broadcastconfig.toml` dosyasını yeniden yükler. |

---

## 📥 Kurulum

1. `BroadcastSystem.dll` dosyasını derleyin veya hazır paketi indirin.
2. CS2 sunucunuzda eklenti klasörünü oluşturun:
   ```text
   addons/swiftlys2/plugins/BroadcastSystem/
   ```
3. Aşağıdaki dosyaları bu klasöre kopyalayın:
   - `BroadcastSystem.dll`
   - `BroadcastSystem.deps.json`
   - `broadcastconfig.example.toml` -> `broadcastconfig.toml` olarak adlandırın.
   - *(İsteğe bağlı)* `GeoLite2-Country.mmdb` dosyasını yapılandırma klasörüne ekleyin.
4. Sunucu konsolundan `sw_plugins reload BroadcastSystem` yazın.

---

## 📄 Lisans

Bu proje **Mazics2** bünyesinde geliştirilmiştir.

