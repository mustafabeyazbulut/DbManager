# DbManager

SQL Server veritabanlarını yönetmek için geliştirilmiş Windows Forms masaüstü uygulaması.

## Özellikler

- **Sunucu Bilgisi** — Sürüm, edition, toplam/online/offline DB sayısı, toplam disk kullanımı
- **Veritabanı Listesi** — Durum, kurtarma modeli, boyut, son backup tarihi, salt-okunur bilgisi
- **Tablo / Prosedür / Dosya Detayları** — Seçili veritabanının içeriğine detaylı bakış
- **Backup** — Seçili veritabanının tam yedeğini alır
- **Shrink** — Veritabanı veya log dosyasını küçültür
- **Index Yönetimi** — Fragmentasyon analizi; rebuild / reorganize işlemleri
- **Aktif Bağlantılar** — Oturum, CPU, bellek, bekleme türü, bloklamalar
- **Uzun Süren Sorgular** — Çalışan sorgular ve SQL metinleri
- **Kilit Bilgisi** — Kilitli kaynaklar ve bekleme süreleri
- **TempDB Kullanımı** — Oturum bazında tempdb kullanım bilgisi
- **Kullanıcılar** — Veritabanı kullanıcıları ve rol üyelikleri
- **SQL Agent Jobs** — İş listesi, son çalışma zamanı ve durum
- **Excel Export** — Tüm grid görünümleri xlsx olarak dışa aktarılabilir
- **Loglama** — İşlemler `Log/DatabaseLog/` klasörüne otomatik olarak yazılır

## Gereksinimler

| Bileşen | Sürüm |
|---|---|
| .NET Framework | 4.8 |
| DevExpress WinForms | 21.2 |
| Microsoft SQL Server | 2012+ |

> DevExpress lisansı ayrıca edinilmelidir. Kütüphaneler `bin/Debug/` içinde yer alır ancak `.gitignore` tarafından izlenmez.

## Kurulum

1. Repoyu klonlayın:
   ```
   git clone <repo-url>
   ```
2. `DbManager.sln` dosyasını Visual Studio 2019/2022 ile açın.
3. DevExpress 21.2 bileşenlerinin yüklü olduğundan emin olun.
4. `App.config` içindeki bağlantı dizesini kendi SQL Server örneğinize göre ayarlayın.
5. Derleme ve çalıştırma: `F5` veya `Ctrl+F5`.

## Kullanım

Uygulama açıldığında bağlantı bilgilerini girerek sunucuya bağlanın. Sol panelden işlem kategorisini seçin; ilgili veriler DevExpress grid üzerinde listelenir. Toolbar üzerindeki butonlarla backup, shrink, index rebuild gibi işlemleri başlatabilirsiniz.

## Proje Yapısı

```
DbManager/
├── Form1.cs               # Ana form ve UI mantığı
├── Form1.Designer.cs      # Tasarımcı tarafından üretilen kod
├── DatabaseHelper.cs      # SQL Server sorgular ve veritabanı işlemleri
├── GridHelper.cs          # DevExpress grid yardımcı metotları ve Excel export
├── Program.cs             # Giriş noktası
├── App.config             # Uygulama yapılandırması
└── Properties/            # AssemblyInfo, Resources, Settings
```
