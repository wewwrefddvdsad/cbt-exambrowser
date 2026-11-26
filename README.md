# CBT Ujian — aplikasi ujian Android (.apk) & Windows (.exe)

Aplikasi seperti *exambrowser* untuk halaman siswa `https://alamat-sekolah/ujian-cbt/`.
Guru dan admin tetap memakai `https://alamat-sekolah/guru/` di browser biasa.

| | Android (.apk) | Windows (.exe) |
|---|---|---|
| Versi sistem | Android 5.0 sampai Android 16 (minSdk 21, targetSdk 35) | Windows 10 dan 11 (64-bit, 32-bit, ARM) |
| Sebelum ujian | aplikasi biasa: bisa pindah aplikasi, atur Wi-Fi, ditutup | jendela biasa: bisa diperkecil, pindah aplikasi, atur Wi-Fi, ditutup |
| Mulai terkunci | saat siswa menekan **Mulai mengerjakan** | saat siswa menekan **Mulai mengerjakan** (jendela berubah menjadi layar penuh) |
| Selama ujian | layar disematkan (Beranda & Terbaru tidak berfungsi), layar penuh, tidak bisa tangkap/rekam layar, jendela mengambang aplikasi lain disembunyikan, papan klip dikosongkan | tombol Windows, Alt+Tab, Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc, Alt+F4, Print Screen diblokir; selalu di depan; layar kedua ditutup hitam; tidak bisa tangkap layar; Pengelola Tugas dimatikan |
| Siswa mencoba keluar | lapisan **"Ujian sedang berlangsung"** mengambang menutupi layar dan aplikasi kembali sendiri; dicatat sebagai keluar halaman | jendela langsung dibawa kembali; dicatat sebagai keluar halaman |
| Keluar sebelum selesai | **tidak bisa** (tombol Kembali hanya memberi pemberitahuan) | **tidak bisa** (Alt+F4 & tombol tutup diabaikan), kecuali tombol darurat **Ctrl+C+B** |
| Darurat (komputer lambat/macet) | — | **Ctrl+C+B**: jawaban langsung dikirim ke server lalu aplikasi tertutup. Bila 30 detik belum terkirim, aplikasi tetap tertutup; jawaban tersimpan di komputer dan langsung dikirim saat CBT Ujian dibuka lagi (ujian tidak dilanjutkan) |
| Simpan sandi / isi-otomatis | **mati** (tidak ada tawaran "Simpan sandi ke Google") | **mati** (tidak ada tawaran simpan sandi Edge) |
| Setelah jawaban terkirim | kunci terbuka, bisa ditutup | kunci terbuka, kembali menjadi jendela biasa, bisa ditutup |

**Tidak ada token keluar.** Kunci terbuka sendiri setelah jawaban terkirim, atau setelah pengawas **mereset** ujian
siswa itu di Monitoring (perangkat perlu internet saat itu). Bila perangkat macet tanpa internet, mulai ulang
HP/komputer: jawaban tetap tersimpan di perangkat dan ujian dilanjutkan saat aplikasi dibuka lagi.

Agar siswa tidak bisa mengerjakan dari browser biasa, centang **"Wajib memakai aplikasi CBT Ujian"** di
formulir jadwal ujian.

Selain itu, bila **`APP_KUNCI`** diatur (lihat `ExamBrowser-kunci\APP_KUNCI-BACA.md`), halaman ujian
`…/ujian-cbt/` **tidak bisa dibuka di browser mana pun** — hanya lewat aplikasi ini. Browser lain menerima 404.

---

## Membangun .apk dan .exe (gratis, lewat GitHub — tanpa memasang apa pun di komputer)

1. Buat repo baru di GitHub, misalnya **`cbt-exambrowser`** (boleh **Private**).
2. Unggah isi folder `ExamBrowser` ke repo itu: folder **`android`**, folder **`windows`**, dan berkas **`logo.png`**,
   **`buat-ikon.py`**, **`bangun.yml`**, `README.md` (*Add file → Upload files*, seret semuanya sekaligus).
   **Penting:** unggahan lewat web GitHub **melewati folder berawalan titik** (`.github`), jadi resep pembangunnya
   harus dibuat sendiri: *Add file → Create new file* → ketik nama berkas **`.github/workflows/bangun.yml`** →
   salin-tempel **seluruh isi berkas `bangun.yml`** (yang di akar folder ini) → *Commit changes*.
   Tanda berhasil: tab **Actions** menampilkan **Bangun aplikasi CBT Ujian** (bukan halaman "Get started").
3. Isi **Secret** di repo → **Settings → Secrets and variables → Actions → New repository secret**:
   - `APP_KUNCI` → kunci link, **sama persis** dengan `APP_KUNCI` di serverC (Settings → Variables and Secrets
     aplikasi server). Teksnya ada di `ExamBrowser-kunci\APP_KUNCI.txt`. Tempel tanpa spasi/enter. **Wajib**.
   - `KEYSTORE_BASE64` → isi dengan seluruh teks berkas `ExamBrowser-kunci\KEYSTORE_BASE64.txt` *(disarankan)*
   - `KEYSTORE_PASSWORD` → isi dengan teks berkas `ExamBrowser-kunci\KEYSTORE_PASSWORD.txt` *(disarankan)*

   Tanpa KEYSTORE, APK tetap jadi, tetapi setiap pembaruan aplikasi harus menghapus aplikasi lama di HP dulu.
   Sebaiknya repo dijadikan **Private** (Settings → General → Danger Zone → Change visibility): hasil .apk/.exe
   memuat kunci link, dan hasil pembangunan di repo publik bisa diunduh siapa pun yang punya akun GitHub.
4. Buka tab **Actions** → **Bangun aplikasi CBT Ujian** → **Run workflow** → isi **alamat yang dibuka aplikasi**:
   sebaiknya langsung `https://…/ujian-cbt/` (contoh `https://css.afamir19.my.id/ujian-cbt/`) → **Run workflow**.
   Langkah pertama, **Periksa alamat & kunci link**, mencocokkan alamat dan `APP_KUNCI` dengan server. Bila merah,
   bacalah pesannya (alamat salah / `APP_KUNCI` kosong / `APP_KUNCI` berbeda dengan serverC), perbaiki, lalu jalankan lagi.
   Aplikasi yang pasti ditolak server tidak akan dibangun.
5. Tunggu ±5–10 menit sampai kedua pekerjaan berwarna hijau. Buka hasilnya, lalu di bagian **Artifacts** unduh:
   - **CBT-Ujian-android** → berisi `CBT-Ujian.apk`
   - **CBT-Ujian-windows** → berisi `CBT-Ujian.exe`

Alamat ujian disatukan ke dalam aplikasi. Bila nanti alamat server berubah, jalankan langkah 4 lagi.
Untuk Windows, alamat juga bisa diganti tanpa membangun ulang: taruh berkas `alamat.txt` berisi alamat baru di
samping `CBT-Ujian.exe`.

---

## Memasang di HP Android

1. Salin `CBT-Ujian.apk` ke HP, buka, izinkan **"Instal aplikasi tidak dikenal"** bila diminta.
   (Google Play Protect bisa menampilkan peringatan karena aplikasi tidak berasal dari Play Store — pilih *Tetap instal*.)
2. Buka **CBT Ujian**. Saat diminta, aktifkan **"Tampil di atas aplikasi lain"** untuk CBT Ujian.
3. Saat siswa menekan **Mulai mengerjakan**, Android menampilkan **"Sematkan aplikasi?"** (Pin app) → tekan
   **Mengerti / OK**. Sejak itu aplikasi terkunci.
4. HP **Xiaomi / Redmi / POCO**: di *Setelan → Aplikasi → CBT Ujian → Izin lainnya*, aktifkan juga
   **"Tampilkan jendela pop-up saat berjalan di latar belakang"** agar aplikasi bisa kembali sendiri ke depan.
5. Bila **Sematkan layar** tidak muncul di HP tertentu, aktifkan di *Setelan → Keamanan → Sematkan aplikasi*
   (nama menu berbeda tiap merek). Tanpa sematan pun, lapisan mengambang dan pencatatan keluar tetap bekerja.

## Memasang di komputer Windows

1. Salin `CBT-Ujian.exe` ke komputer (misalnya ke Desktop) lalu jalankan. Tidak perlu instalasi.
2. Windows SmartScreen bisa menampilkan *"Windows protected your PC"* karena aplikasi belum bertanda tangan
   digital → klik **More info → Run anyway**.
3. Butuh **Microsoft Edge WebView2 Runtime** (sudah ada di Windows 11 dan Windows 10 yang diperbarui). Bila belum
   ada, aplikasi memberi tahu alamat unduhannya.
4. Ctrl+Alt+Del tidak bisa diblokir oleh aplikasi mana pun (fitur keamanan Windows). Pengelola Tugas dimatikan
   selama ujian untuk akun Windows tersebut dan dipulihkan otomatis setelah ujian / saat aplikasi dibuka lagi.

Sejak versi 1.3, `CBT-Ujian.exe` bekerja seperti aplikasi Android. Sebelum ujian dimulai, aplikasi tampil sebagai jendela
biasa, sehingga siswa atau operator masih bisa mengatur jaringan atau membuka aplikasi lain. Penguncian (layar penuh,
tombol diblokir, layar lain ditutup hitam, tangkap layar dicegah, Pengelola Tugas dimatikan) baru aktif saat siswa
menekan **Mulai mengerjakan**, lalu dilepas lagi setelah jawaban terkirim atau ujian direset pengawas. Bila aplikasi
dibuka ulang di tengah ujian, ujian dilanjutkan dan aplikasi langsung terkunci lagi.

Selama ujian, satu-satunya cara keluar adalah tombol darurat **Ctrl+C+B** (tahan Ctrl, lalu tekan C dan B bersamaan),
misalnya bila komputer lambat atau macet. Jawaban langsung dikirim ke server, muncul pemberitahuan, lalu aplikasi
tertutup. Tombol ini butuh server CBT versi 2.14.2 atau lebih baru.

### Alamat yang dibuka aplikasi

Aplikasi membuka alamat yang diisi saat membangun **apa adanya**, sama seperti aplikasi Android. Alamatnya boleh:
- link blog/website sekolah yang memuat halaman ujian di dalamnya (mis. `https://namasekolah.blogspot.com` berisi
  iframe ke `https://…/ujian-cbt/`). Halaman ujian bisa diganti dari blog tanpa membangun ulang aplikasi;
- atau langsung alamat ujian `https://…/ujian-cbt/`.

Tautan ke situs lain yang diklik siswa diabaikan. Pengalihan otomatis dari halaman sekolah sendiri tetap diizinkan.

Urutan alamat yang dipakai `CBT-Ujian.exe`:
1. berkas `alamat.txt` di samping exe;
2. alamat bawaan saat dibangun;
3. alamat yang pernah diketik (hanya untuk exe yang dibangun tanpa alamat).

Bila server tidak bisa dihubungi sama sekali, aplikasi mencoba lagi otomatis 2 kali. Setelah itu muncul layar
**"Tidak ada koneksi"** dengan tombol **Coba lagi**. Bila server menjawab (misalnya halaman tidak ditemukan),
halaman dari server ditampilkan apa adanya.

---|---|---|
| Nama server tidak ditemukan · `HostNameNotResolved` | Alamat salah, atau DNS jaringan komputer ini memblokir alamat itu | Cocokkan alamat di bawah dengan alamat yang dibuka HP. Bila sama, coba jaringan lain atau ganti DNS komputer ke 8.8.8.8 / 1.1.1.1 |
| Sertifikat keamanan server ditolak · `Certificate…` | Jam/tanggal komputer salah, atau antivirus memeriksa HTTPS | Betulkan jam & tanggal Windows, lalu **Coba lagi** |
| Tidak bisa tersambung · `CannotConnect`, `Timeout`, … | Internet putus, atau firewall/antivirus memblokir aplikasi | Coba buka alamat itu di browser komputer yang sama. Bila bisa, izinkan CBT Ujian di firewall/antivirus |
| Server tidak mengizinkan … (404) | Aplikasi dibangun untuk server lain, atau kunci link berbeda | Bangun ulang aplikasi dengan alamat dan kunci link server sekolah ini |
| Server mencapai batas harian (429) | Kuota harian server habis | Tunggu sampai server pulih pukul 08.00 WITA |

Urutan alamat yang dipakai `CBT-Ujian.exe`:
1. berkas `alamat.txt` di samping exe;
2. alamat bawaan saat dibangun;
3. alamat yang pernah diketik (hanya untuk exe yang dibangun tanpa alamat).

Sebelum versi 1.3, alamat yang pernah diketik di komputer itu selalu dipakai, walaupun exe yang baru sudah membawa alamat
lain. Akibatnya exe baru bisa membuka server lama yang sudah tidak ada. Bila komputer masih memakai `alamat.txt` lama di
samping exe, hapus berkas itu.

---

## Logo aplikasi

Ikon aplikasi diambil dari berkas **`logo.png`** di folder ini (boleh juga `logo.jpg`). Untuk mengganti logo:

1. Timpa `logo.png` dengan logo baru. Sebaiknya minimal 512 px, berlatar putih atau transparan.
2. Unggah `logo.png` itu ke repo `cbt-exambrowser` (*Add file → Upload files*, timpa yang lama) lalu *Commit*.
3. **Actions → Bangun aplikasi CBT Ujian → Run workflow**. Ikon .apk dan .exe dibuat ulang dari `logo.png` saat membangun.

Yang dikerjakan otomatis oleh `buat-ikon.py` saat membangun:
- Latar putih **di luar** logo dibuat transparan (putih di dalam logo tetap). Logo yang sudah transparan dipakai apa adanya.
- **Android:** logo di tengah latar putih, diperkecil secukupnya agar tidak terpotong oleh bentuk ikon HP
  mana pun (bulat, kotak membulat, tetesan) → `android/app/src/main/res/drawable-*dpi/ikon_sekolah.png`.
- **Windows:** `windows/ikon.ico` ukuran 16–256 px berlatar transparan (ikon `CBT-Ujian.exe`, jendela, bilah tugas).

Berkas ikon di folder `android` dan `windows` **tidak perlu diubah sendiri**, karena selalu ditimpa dari `logo.png` saat
membangun. Bila `logo.png` tidak ada, ikon yang sudah ada yang dipakai. Setelah memasang versi baru, ikon di layar HP
kadang baru berubah setelah HP dinyalakan ulang.

Ingin melihat hasil ikonnya di komputer sendiri sebelum membangun (tidak wajib): pasang Python, lalu
`pip install pillow numpy` dan `python buat-ikon.py` di folder ini.

## Isi folder

| Folder | Isi |
|---|---|
| `android/` | proyek Android (Java, tanpa pustaka tambahan) — bisa juga dibuka di Android Studio |
| `windows/` | proyek Windows (C#, WinForms + WebView2, .NET Framework 4.8) — bisa juga dibuka di Visual Studio |
| `bangun.yml` | resep GitHub Actions yang memeriksa kunci link lalu membangun .apk dan .exe (di repo disimpan sebagai `.github/workflows/bangun.yml`) |
| `logo.png` | logo sekolah → ikon .apk dan .exe (ganti kapan saja) |
| `buat-ikon.py` | pembuat ikon dari `logo.png`, dijalankan otomatis saat membangun |

Membangun di komputer sendiri (opsional):
- Android Studio: buka folder `android`, lalu *Build → Generate Signed App Bundle / APK*. Alamat diisi dengan
  menambahkan `alamatUjian=https://…/ujian-cbt/` di `android/gradle.properties`.
- Windows (Visual Studio 2022 atau .NET SDK 8): `dotnet build windows/CBTUjian.csproj -c Release -p:AlamatUjian=https://…/ujian-cbt/`
