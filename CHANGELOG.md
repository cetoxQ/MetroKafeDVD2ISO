# Sürüm notları

## v1.4.2 — 2026-11-23
- Kopyalama başlayınca kaynak/hedef alanı tek satırlık özete iniyor; günlük ekranı çok daha geniş
- Durum alanı kopyalama boyunca sabit yerde ve sabit yükseklikte: sorun yokken "Okuma normal", sorun olunca sarı uyarı; ekran artık zıplamıyor
- Uyarılar en az 5 sn ekranda kalıyor (gidip gelmiyor); "okuma hızı düştü" uyarısı ancak hız 5 sn boyunca düşük kalırsa çıkıyor
- Her uyarı başladığında ve okuma normale döndüğünde günlüğe bir satır yazılıyor

## v1.4.1 — 2026-11-23
- Ayarlar ve günlükler artık programın bulunduğu klasöre kaydediliyor (MetroKafeDVD2ISO.settings.json, logs\); klasör yazılabilir değilse %AppData% kullanılıyor. Eski ayarlar bir kez taşınıyor
- Dosya tabanlı yöntem iki adımda çalışıyor: dosyalar önce geçici klasöre kopyalanıyor (ilerleme, hız ve takılma uyarısı normal çalışıyor; okunamayan bölümler atlanıp günlüğe yazılıyor), ardından ISO yerel kopyadan saniyeler içinde oluşturuluyor
- Dosya tabanlı yöntemde boş alan denetimi geçici kopya için gereken yeri de hesaba katıyor

## v1.4.0 — 2026-11-23
- Video CD / Super Video CD desteği: görüntü sektörleri (Mode 2 Form 2) ISO'ya sektör sektör sığmadığı için bu disklerde ISO, diskteki dosyalardan oluşturuluyor (eski DVD2ISO gibi); artık "hasarlı bölge" hatası vermiyor
- Ayarlara "Kopyalama yöntemi" eklendi: Otomatik / Sektör sektör / Dosya tabanlı
- Sürücü bilgisinde disk türü "Video CD" olarak gösteriliyor
- Aynı oturumdaki ikinci veri izi artık yanlışlıkla "çok oturumlu disk" sayılmıyor
- UDF disklerin sonundaki çapa sektörleri her yerleşimde doğru hesaplanıyor (bazı disklerin sondan eksik kopyalanması önlendi)

## v1.3.1 — 2026-11-23
- Diskin iz/oturum düzeni (TOC) okunuyor; yalnız veri izleri kopyalanıyor
- Çok oturumlu disklerde oturum aralarındaki hiç okunamayan boşluklar okunmadan sıfırla dolduruluyor ve hasarlı sayılmıyor; ISO açıldığında son eklenen dosyalar da görünüyor
- Karma (veri + ses) disklerde ses izleri okunmaya çalışılmıyor
- Disk düzeni günlüğe yazılıyor; her kopyalamanın günlüğü %AppData%\MetroKafeDVD2ISO\logs altına kaydediliyor
- "Günlüğü kopyala" düğmesi

## v1.3.0 — 2026-11-23
- Disk boyutu artık dosya sisteminden (ISO9660 / UDF) okunuyor; yazılabilir disklerin sonundaki veri içermeyen sektörler "hatalı" sayılmıyor
- Sürücünün bildirdiği fazladan sektörler en sonda deneniyor: okunabilenler ISO'ya ekleniyor, okunamayanlar hata sayılmıyor
- Pencere artık diske hiç erişmiyor: tanıma, kopyalama ve diski çıkarma ayrı bir işlemde çalışıyor, sürücü takılsa da pencere donmuyor
- Yanıt vermeyen sürücü 15 sn sonra listede "sürücü yanıt vermiyor" olarak gösteriliyor
- Kopyalama sırasında sürücü takılsa bile çarpı düğmesi programı kapatıyor

## v1.2.0 — 2026-11-22
- Yeni Ayarlar sekmesi; seçenekler kalıcı olarak saklanıyor
- Takılan bölgeyi belirlenen süreden (5–60 sn) sonra atlama; kilitlenip kalma önlendi
- Önce sağlam kısımları kopyalama: hasarlı bölgeler hızla atlanıp en sonda tekrar deneniyor
- Hasarlı bölgede sürücü hızını düşürme (destekleyen sürücülerde)
- Bitince doğrulama: disk ikinci kez okunup ISO ile karşılaştırılıyor, SHA-256 hesaplanıp .sha256 dosyasına yazılıyor
- Bitince diski çıkarma seçeneği
- Otomatik güncelleme: açılışta yeni sürüm denetimi ve tek tıkla güncelleme

## v1.1.0 — 2026-11-22
- Yeni, modern koyu arayüz; özel başlık çubuğu ve logo
- Kayıt konumu ve dosya adı ayrı seçiliyor; açılışta konum Masaüstü
- Hazır dosya adı seçenekleri: Disk adı, Disk adı + tarih, Disk adı + tarih + saat, Tarih + saat
- Anlık ve ortalama kopyalama hızı (MB/s ve DVD/CD hız katı) gösteriliyor
- Çizik/hasarlı disk uyarısı: okuma takıldığında veya yavaşladığında bilgi verir
- Sürücü takılı kaldığında da İptal düğmesi çalışıyor
- Güncellemeler sayfası ve güncelleme denetimi

## v1.0.0 — 2026-11-22
- İlk sürüm: DVD/CD'den ISO kalıbı oluşturma
- Okunamayan sektörleri atlayıp devam etme
- Boş alan ve FAT32 4 GB sınırı denetimi
