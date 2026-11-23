# Sürüm notları

## v1.8.0 — 2026-11-23
- İngilizce arayüz: program ilk açılışta Windows dili Türkçe ise Türkçe, değilse İngilizce açılıyor; seçim kalıcı olarak saklanıyor
- Sağ üstte bayraklı dil seçici (Türkçe / English); dil değişince pencere aynı yerde yeni dilde yeniden açılıyor (kopyalama veya dönüştürme sürerken değiştirilemez)
- Günlükler, uyarılar, disk motoru mesajları ve sürüm notları da seçilen dilde; sayılar dile göre biçimleniyor (1,8 GB / 1.8 GB)
- Güncellemeler sekmesi kaldırıldı: güncelleme denetimi Ayarlar sayfasının en üstünde ayrı bölüm; sürüm notları "Sürüm notları" düğmesiyle açılıyor
- "Yeni sürüm" çipi artık Ayarlar sayfasını açıyor

## v1.7.0 — 2026-11-23
- MP4 listesi tabloya dönüştü: ISO dosyası · boyut (MB) · başlık/süre · bit hızı · tahmini MP4 · durum
- Her ISO için ayrı bit hızı: tablodaki bit hızı hücresine tıklayıp yazın; tahmini MP4 boyutu anında güncellenir
- Otomatik bit hızı: ISO taranınca MP4 yaklaşık ISO boyutunda çıkacak şekilde hesaplanıyor, 100'ün katına yuvarlanıyor (ör. 462 MB / 45 dk → 1300 kbps); tabloda "(oto)" olarak görünür
- Hazır bit hızı düğmeleri ve yeni "Otomatik" düğmesi seçili satırlara (seçim yoksa bekleyen tümüne) uygulanıyor
- Ayarlar'daki varsayılan bit hızı artık yalnız süresi okunamayan ISO'larda kullanılıyor

## v1.6.1 — 2026-11-23
- Düzeltme: Video CD ISO'larında film bulunamıyordu. Windows'un ISO oluşturucusu ISO9660 tarafında adları 8.3'e sıkıştırıyor (AVSEQ01.DAT → AVSEQ010); artık gerçek adlar Joliet'ten okunuyor ve MPEGAV/MPEG2 klasöründeki her dosya uzantıya bakılmadan alınıyor. Gerçek bir VCD ile uçtan uca doğrulandı
- MP4 ekranı sadeleşti: bit hızı değeri etiketin yanında, kodlayıcı açıklaması ipucunda, taramalı görüntü düzeltme tıklayınca açılan bölümde; ses ve çözünürlük notu onun altında; "ISO ile aynı klasöre kaydet" işaretliyken konum satırı gizli. Günlük alanı belirgin şekilde büyüdü
- Sistem bilgisinde ekran kartı video yükü tek değer olarak gösteriliyor (Intel kartlar kodlamayı çözme motorunda yapıyor)

## v1.6.0 — 2026-11-23
- Video CD ISO'ları da MP4'e dönüştürülüyor: ISO içindeki MPEGAV\*.DAT (Super Video CD'de MPEG2\*.MPG) dosyaları bulunuyor, ham CD sektörlerinden düz MPEG akışı çıkarılıp HandBrake'e veriliyor; birden fazla video varsa en uzunu ana MP4, diğerleri ayrı MP4
- Ayarlar → MP4 dönüştürme: varsayılan video bit hızı ve dört hazır bit hızı değiştirilebiliyor; MP4 sekmesi her açılışta varsayılanla başlıyor
- Dönüştürme bitince hata alan ISO sayısı da yazılıyor

## v1.5.1 — 2026-11-23
- MP4 sekmesinde ilerleme çubuğunun altında sistem bilgisi: işlemci kullanımı, RAM (kullanılan / toplam), ekran kartının video kodlama / video çözme / 3D kullanımı; NVIDIA kartlarda ekran kartı sıcaklığı, destekleyen bilgisayarlarda anakart (ACPI) sıcaklığı
- AMD ekran kartları HandBrake'in yeni adıyla (vcn) da tanınıyor
- Ekran kartı seçilince videonun açılması (kod çözme) da ekran kartında yapılıyor (NVIDIA, Intel)
- Tanılama: sekme açılınca ekran kartları ve kullanılabilen kodlayıcılar, dönüştürmede HandBrake'in gerçekte kullandığı kodlayıcı günlüğe yazılıyor; takılı kartın kodlayıcısı yoksa nedeni söyleniyor

## v1.5.0 — 2026-11-23
- Yeni "MP4'e Dönüştür" sekmesi: ISO'lar HandBrakeCLI ile MP4'e (H.264 + AAC) dönüştürülüyor
- Çözünürlük ve kare hızı kaynakla aynı kalıyor (kırpma/ölçekleme yok, sabit kare hızı, DVD en-boy oranı korunuyor)
- Ortalama bit hızı ayarı ve hazır değerler; yanında tahmini çıktı boyutu
- Çok geçişli kodlama ve turbo analiz kapalı
- Kodlayıcı seçimi: işlemci (x264) veya bilgisayarda varsa NVIDIA / Intel / AMD ekran kartı
- Taramalı görüntü düzeltme (Decomb) açık/kapalı, açıklamalarıyla
- Toplu dönüştürme: birden fazla ISO veya bir klasördeki tüm ISO'lar sırayla; sürükle-bırak
- DVD'de birden fazla video başlığı varsa ana başlık + diğerleri ayrı MP4 olarak; "hepsini oynat" ve kopya başlıklar atlanıyor
- Ayarlar: "ISO'yu MP4'e de dönüştür" — kopyalama bitince dönüştürme kendiliğinden başlıyor
- HandBrakeCLI ilk kullanımda izinle resmi GitHub sayfasından indirilip programın klasöründeki tools\ altına kaydediliyor

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
