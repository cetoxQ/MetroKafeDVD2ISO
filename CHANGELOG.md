# Sürüm notları

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
