# Sürüm notları

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
