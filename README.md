# MetroKafe DVD2ISO

DVD ve CD'lerden ISO kalıbı oluşturan, sade ve modern bir Windows uygulaması.

## Özellikler
- DVD/CD sürücülerini otomatik bulur; disk takılıp çıkarılınca listeyi yeniler
- Disk boyutunu dosya sisteminden (ISO9660/UDF) okur; yazılabilir disklerin sonundaki boş sektörleri hata saymaz
- Diske erişen işler ayrı işlemde çalışır; sürücü takılsa da pencere donmaz, çarpı her zaman çalışır
- Kayıt konumu (varsayılan Masaüstü) ve dosya adı ayrı seçilir
- Hazır dosya adları: Disk adı, Disk adı + tarih, Disk adı + tarih + saat, Tarih + saat
- Anlık kopyalama hızı (MB/s ve DVD/CD hız katı), kalan süre, hatalı sektör sayısı
- Çizik/hasarlı disk uyarısı: okuma takıldığında veya belirgin şekilde yavaşladığında bilgi verir
- Okunamayan sektörleri tekrar dener; okunamazsa sıfırla doldurup devam eder (isteğe bağlı)
- Sürücü takılı kaldığında bile İptal çalışır
- Boş alan ve FAT32 4 GB sınırı denetimi
- Takılan bölgeyi belirlenen süre sonra atlama; önce sağlam kısımları kopyalayıp hasarlı bölgelere en sonda dönme
- Hasarlı bölgede sürücü hızını düşürme
- Bitince doğrulama (diskle karşılaştırma + SHA-256) ve diski çıkarma
- Video CD / Super Video CD: ISO diskteki dosyalardan oluşturulur (görüntü sektörleri ISO'ya sektör sektör sığmaz)
- Kopyalama yöntemi: Otomatik / Sektör sektör / Dosya tabanlı
- Ayarlar sekmesi (seçenekler kalıcı)
- Güncellemeler sayfası, açılışta otomatik denetim ve tek tıkla güncelleme (GitHub Releases)

## MP4'e dönüştürme
"MP4'e Dönüştür" sekmesi ISO'ları [HandBrake](https://handbrake.fr) komut satırı aracıyla MP4'e çevirir: çözünürlük ve kare hızı kaynakla aynı kalır, yalnız ortalama bit hızı seçilir (yanında tahmini boyut görünür). Toplu dönüştürme, ekran kartıyla kodlama ve taramalı görüntü düzeltme desteklenir. HandBrakeCLI (GPL) programa gömülü değildir; ilk kullanımda izin istenip resmi GitHub sürümlerinden indirilir.

## Kullanım
1. Diski takın, uygulamayı açın.
2. Sürücüyü, kayıt konumunu ve dosya adını seçin.
3. **ISO Oluştur**'a basın.

İndirme: [Releases](../../releases) sayfasındaki `MetroKafeDVD2ISO.exe` (kurulum gerekmez, .NET gerektirmez).

> Not: Kopya korumalı (CSS) film DVD'lerinin korumasını kaldırmaz.

## Derleme
.NET 10 SDK gerekir.

```bash
dotnet publish -c Release -o publish
```

Çıktı: `publish/MetroKafeDVD2ISO.exe` (tek dosya, Windows x64).

## Yeni sürüm yayınlama
1. `MetroKafeDVD2ISO.csproj` içindeki `<Version>` değerini artırın.
2. `CHANGELOG.md` dosyasına sürüm notlarını ekleyin.
3. Derleyip `vX.Y.Z` etiketiyle bir GitHub Release oluşturun ve exe'yi ekleyin.
