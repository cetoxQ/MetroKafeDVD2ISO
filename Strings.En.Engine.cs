namespace MetroKafeDVD2ISO;

static partial class L
{
    static readonly (string Tr, string En)[] EnEngine =
    [
        // DiscLayout
        ("iz bilgisi alınamadı", "track information unavailable"),
        ("veri izi bulunamadı", "no data track found"),
        ("Disk düzeni: {0}; boyut {1}.", "Disc layout: {0}; size {1}."),
        ("Disk düzeni: veri izleri okunamadı; sürücünün bildirdiği boyut kullanılıyor.", "Disc layout: data tracks could not be read; using the size reported by the drive."),
        ("Disk düzeni: {0} iz (", "Disc layout: {0} track(s) ("),
        ("veri", "data"),
        ("ses", "audio"),
        ("), kapanış @{0:N0}. Okunacak: ", "), lead-out @{0:N0}. To read: "),
        ("; izler/oturumlar arasındaki {0:N0} sektörlük okunamaz boşluk sıfırla doldurulacak", "; the {0:N0}-sector unreadable gap between tracks/sessions will be zero-filled"),
        ("; {0} ses izi ISO'ya alınmaz", "; {0} audio track(s) not included in the ISO"),
        ("; çok oturumlu disk, ISO son oturumun dosyalarını gösterecek", "; multisession disc, the ISO will show the files of the last session"),
        ("dosya sisteminden {0:N0} sektör (sürücü {1:N0} bildiriyor)", "{0:N0} sectors from the file system (drive reports {1:N0})"),
        ("sürücüden {0:N0} sektör", "{0:N0} sectors from the drive"),

        // Worker
        ("{0} algılandı. Görüntü, ISO sektörlerine sığmayan özel sektörlerde (Mode 2 Form 2) durduğu için sektör sektör kopyalanamaz; ISO diskteki dosyalardan oluşturuluyor.",
            "{0} detected. The video is stored in special sectors (Mode 2 Form 2) that don't fit into ISO sectors, so it can't be copied sector by sector; building the ISO from the files on the disc."),
        ("Dosya tabanlı yöntem: ISO diskteki dosyalardan oluşturuluyor.", "File-based method: building the ISO from the files on the disc."),
        ("Dosya tabanlı yöntemde diskle sektör karşılaştırması yapılamaz; yalnız SHA-256 hesaplanacak.", "The file-based method can't compare sectors against the disc; only the SHA-256 will be computed."),
        ("Dosya sistemi {0:N0}. sektörde bitiyor; ardındaki {1:N0} sektör en sonda denenecek, okunamazsa hata sayılmayacak.",
            "The file system ends at sector {0:N0}; the {1:N0} sectors after it will be tried at the end and won't count as errors if unreadable."),
        ("Yardımcı işlem başlatılamadı.", "Could not start the helper process."),
        ("Okuma işlemi beklenmedik şekilde sonlandı.", "The read process ended unexpectedly."),

        // FileIsoBuilder
        ("Diskteki dosyalar listeleniyor…", "Listing files on the disc…"),
        ("{0:N0} dosya ({1:0.#} MB) geçici klasöre kopyalanıyor; ardından ISO oluşturulacak.", "Copying {0:N0} files ({1:0.#} MB) to a temporary folder; the ISO will be built afterwards."),
        ("\"{0}\" dosyası okunamadı ({1}). Disk çizik veya hasarlı olabilir.", "Could not read file \"{0}\" ({1}). The disc may be scratched or damaged."),
        ("\"{0}\": {1:N0} KB okunamadı, sıfırla dolduruldu (dosya bozuk olabilir).", "\"{0}\": {1:N0} KB unreadable, zero-filled (the file may be corrupted)."),
        ("Windows'un ISO bileşeni (IMAPI2FS) bulunamadı.", "The Windows ISO component (IMAPI2FS) was not found."),
        ("ISO oluşturuluyor…", "Building ISO…"),
        ("ISO oluşturulamadı: {0}", "Could not build the ISO: {0}"),

        // DiscReader
        ("Disk boyutu okunamadı. Sürücüde okunabilir bir disk olduğundan emin olun.", "Could not read the disc size. Make sure there is a readable disc in the drive."),
        ("okuma zaman aşımına uğradı", "read timed out"),
        ("okunamadı", "read error"),
        ("zaman aşımı", "timeout"),
        ("Hasarlı bölge ({0} konumunda {1}); {2} atlandı, en sonda tekrar denenecek", "Damaged area ({1} at {0}); {2} skipped, will be retried at the end"),
        ("Sağlam bölümler bitti. {0} hasarlı bölge ({1}) tekrar deneniyor", "Healthy areas done. Retrying {0} damaged area(s) ({1})"),
        ("Çok oturumlu disk: son oturumun dosya sistemi tanımı ISO'nun başına yazıldı; ISO açıldığında son eklenen dosyalar da görünür.",
            "Multisession disc: the last session's file system descriptors were written to the start of the ISO, so the most recently added files are visible when the ISO is opened."),
        ("Dosya sisteminden sonra {0:N0} sektör daha okunabildi ve ISO'ya eklendi.", "{0:N0} more sectors after the file system could be read and were added to the ISO."),
        ("Disk sonundaki {0:N0} sektör okunamadı. Bunlar veri içermeyen kuyruk sektörleri (yazılabilir disklerde normaldir); hata sayılmadı.",
            "{0:N0} sectors at the end of the disc could not be read. These are tail sectors without data (normal on recordable discs); not counted as errors."),
        ("sektör {0:N0}, deneme {1}/{2}", "sector {0:N0}, attempt {1}/{2}"),
        ("Sektör {0:N0} okunamadı ({1}). Disk çizik veya hasarlı olabilir.", "Sector {0:N0} could not be read ({1}). The disc may be scratched or damaged."),
        ("Sektör {0:N0} atlandı ({1}), sıfırla dolduruldu", "Sector {0:N0} skipped ({1}), zero-filled"),
        ("Doğrulama başladı: disk tekrar okunup ISO ile karşılaştırılıyor", "Verification started: re-reading the disc and comparing it with the ISO"),
        ("Doğrulama: sektör {0:N0} diskle uyuşmuyor", "Verify: sector {0:N0} does not match the disc"),
        ("Sürücü hızı düşürüldü (hasarlı bölge daha yavaş okunuyor)", "Drive speed lowered (reading the damaged area more slowly)"),
        ("Sürücü hızı normale döndü", "Drive speed back to normal"),
        ("Sürücü hız ayarını desteklemiyor; normal hızda devam ediliyor", "The drive doesn't support speed control; continuing at normal speed"),
        ("{0}: sürücüsü açılamadı", "Could not open drive {0}:"),
    ];
}
