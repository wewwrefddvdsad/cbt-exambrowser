using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CBTUjian
{
    /// <summary>
    /// Cara kerja sama dengan aplikasi Android (.apk):
    ///  - Membuka alamat sekolah apa adanya (boleh link blog yang memuat halaman ujian di dalam iframe).
    ///  - Sebelum ujian: jendela biasa (bisa diperkecil, pindah ke aplikasi lain, atur Wi-Fi, ditutup).
    ///  - Siswa menekan "Mulai mengerjakan" (halaman mengirim pesan "kunci"): layar penuh & selalu di depan,
    ///    tombol Windows/Alt+Tab/Alt+F4 dll. diblokir, layar lain ditutup hitam, tidak bisa tangkap layar,
    ///    Pengelola Tugas dinonaktifkan, dan setiap kali jendela ditinggalkan dicatat ke halaman.
    ///  - Jawaban terkirim atau ujian direset pengawas ("buka"): kembali menjadi jendela biasa.
    ///  - Darurat (mis. komputer lambat/macet): Ctrl+C+B → jawaban langsung dikirim ke server, lalu aplikasi
    ///    ditutup. Bila dalam 30 detik jawaban belum terkirim, aplikasi tetap ditutup; jawaban tersimpan di
    ///    komputer dan langsung dikirim begitu CBT Ujian dibuka lagi.
    /// Isi-otomatis & tawaran simpan sandi dimatikan.
    /// </summary>
    internal sealed class FormUjian : Form
    {
        private const int BatasDaruratDetik = 30;

        private readonly WebView2 web = new WebView2();
        private readonly System.Windows.Forms.Timer jaga = new System.Windows.Forms.Timer { Interval = 400 };
        private readonly Penjaga penjaga = new Penjaga();
        private readonly List<Form> layarLain = new List<Form>();
        private readonly List<CoreWebView2Frame> bingkai = new List<CoreWebView2Frame>();   // iframe (mis. halaman ujian di dalam blog)
        private readonly HashSet<string> hostIzin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string alamat;
        private bool terkunci;
        private bool bolehTutup;
        private bool layarPenuh;        // sedang layar penuh mode ujian
        private bool halamanGagal;      // yang sedang dimuat adalah halaman "tidak bisa dibuka" milik aplikasi
        private int ulangOtomatis;      // percobaan ulang otomatis saat jaringan terganggu sesaat
        private DateTime terakhirLapor = DateTime.MinValue;
        private DateTime daruratSampai = DateTime.MinValue;   // Ctrl+C+B ditekan: menunggu jawaban terkirim

        internal bool Terkunci { get { return terkunci; } }

        public FormUjian()
        {
            Text = "CBT Ujian";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            // jendela biasa sampai ujian dimulai (seperti .apk)
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(800, 600);
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(0xF2, 0xF5, 0xF8);
            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(0xF2, 0xF5, 0xF8);
            Controls.Add(web);
            jaga.Tick += (s, e) => Jaga();
            penjaga.Darurat = () => BeginInvoke(new Action(Darurat));
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            jaga.Start();
            alamat = Pengaturan.AlamatUjian();
            if (alamat == null)
            {
                alamat = Pengaturan.Tanya(this);
                if (alamat == null) { Tutup(); return; }
            }
            hostIzin.Add(new Uri(alamat).Host);
            try { await SiapkanWeb(); }
            catch (Exception ex) { Gagal("Aplikasi tidak bisa membuka halaman ujian.\n\n" + ex.Message); }
        }

        private async Task SiapkanWeb()
        {
            CoreWebView2Environment env;
            try
            {
                string pemuat = Program.SiapkanPemuat();
                if (pemuat != null) CoreWebView2Environment.SetLoaderDllFolderPath(pemuat);
                env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Pengaturan.Folder, "WebView2"), null);
                await web.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                Gagal("Komponen \"Microsoft Edge WebView2 Runtime\" belum terpasang di komputer ini.\n\nPasang dari https://go.microsoft.com/fwlink/p/?LinkId=2124703 lalu buka CBT Ujian lagi.");
                return;
            }

            CoreWebView2 c = web.CoreWebView2;
            CoreWebView2Settings st = c.Settings;
            st.AreDevToolsEnabled = false;
            st.AreDefaultContextMenusEnabled = false;
            st.IsStatusBarEnabled = false;
            st.IsZoomControlEnabled = false;
            st.AreBrowserAcceleratorKeysEnabled = false;   // tanpa F5, Ctrl+P, Ctrl+F, F12, dll.
            st.IsPasswordAutosaveEnabled = false;   // tanpa tawaran "simpan sandi"
            st.IsGeneralAutofillEnabled = false;    // tanpa isi-otomatis formulir
            st.IsSwipeNavigationEnabled = false;
            st.IsBuiltInErrorPageEnabled = false;
            string kunci = Pengaturan.AppKunci();   // opsional: kunci link di server
            st.UserAgent = st.UserAgent + " CBTUjian/" + Program.Versi + " (Windows)" + (kunci.Length == 0 ? "" : " k/" + kunci);

            try
            {
                // hapus isian & sandi yang mungkin tersimpan oleh versi lama (jawaban ujian di perangkat TIDAK dihapus)
                await c.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.GeneralAutofill | CoreWebView2BrowsingDataKinds.PasswordAutosave);
            }
            catch (Exception) { /* WebView2 Runtime lama */ }

            c.NewWindowRequested += (s, a) => a.Handled = true;                 // tidak ada jendela/tab baru
            c.NavigationStarting += (s, a) => { if (!BolehDibuka(a.Uri, a.IsUserInitiated)) a.Cancel = true; };
            c.NavigationCompleted += async (s, a) =>
            {
                if (a.IsSuccess)
                {
                    if (!halamanGagal) { ulangOtomatis = 0; IngatHost(c.Source); }
                    halamanGagal = false;
                    return;
                }
                if (a.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                // Server menjawab (mis. 404): halamannya ditampilkan apa adanya, seperti di HP.
                // Halaman milik aplikasi hanya untuk gangguan jaringan.
                if (AdaJawabanServer(a)) return;
                if (ulangOtomatis < 2)
                {
                    ulangOtomatis++;
                    await Task.Delay(1500 * ulangOtomatis);
                    try { web.CoreWebView2.Navigate(alamat); } catch (Exception) { }
                    return;
                }
                TampilkanGagal(a.WebErrorStatus.ToString());
            };
            // halaman ujian di dalam iframe (mis. link blog sekolah) berkirim pesan lewat bingkainya sendiri
            c.FrameCreated += (s, a) =>
            {
                CoreWebView2Frame f = a.Frame;
                bingkai.Add(f);
                f.WebMessageReceived += PesanHalaman;
                f.Destroyed += (s2, a2) => bingkai.Remove(f);
            };
            c.DownloadStarting += (s, a) => a.Cancel = true;
            c.PermissionRequested += (s, a) => a.State = CoreWebView2PermissionState.Deny;
            c.ProcessFailed += (s, a) => { try { c.Reload(); } catch (Exception) { } };
            c.WebMessageReceived += PesanHalaman;
            c.Navigate(alamat);
        }

        /// <summary>
        /// Halaman utama hanya boleh berpindah ke alamat sekolah. Tautan yang diklik siswa ke situs lain diabaikan;
        /// pengalihan otomatis dari halaman sekolah sendiri (mis. blog yang meneruskan ke server ujian) diizinkan.
        /// </summary>
        private bool BolehDibuka(string uri, bool olehPengguna)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
            Uri u;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps) return false;
            return hostIzin.Contains(u.Host) || !olehPengguna;
        }

        private void IngatHost(string sumber)
        {
            Uri u;
            if (Uri.TryCreate(sumber, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps) hostIzin.Add(u.Host);
        }

        /// <summary>true bila server sempat menjawab (ada kode HTTP), jadi bukan masalah jaringan.</summary>
        private static bool AdaJawabanServer(CoreWebView2NavigationCompletedEventArgs a)
        {
            try
            {
                System.Reflection.PropertyInfo p = a.GetType().GetProperty("HttpStatusCode");
                if (p == null) return false;
                object v = p.GetValue(a, null);
                return v is int && (int)v > 0;
            }
            catch (Exception) { return false; }
        }

        private void TampilkanGagal(string status)
        {
            string saran = status.StartsWith("Certificate", StringComparison.Ordinal)
                ? "Periksa jam dan tanggal komputer ini, lalu tekan Coba lagi."
                : "Periksa koneksi internet, lalu tekan Coba lagi.";
            string tombolTutup = terkunci ? "" :
                "<button style='margin-left:8px;font-size:17px;padding:12px 26px;border-radius:10px;border:1px solid #D9E1E8;background:#fff' onclick='chrome.webview.postMessage(JSON.stringify({a:\"tutup\"}))'>Tutup aplikasi</button>";
            string html = "<!doctype html><html><head><meta charset='utf-8'></head><body style='font-family:Segoe UI,sans-serif;display:grid;place-items:center;height:100vh;margin:0;color:#1C2833;background:#F2F5F8'>"
                + "<div style='text-align:center;max-width:640px;padding:0 24px'><h2>Tidak ada koneksi</h2><p style='font-size:16px;line-height:1.5'>" + saran + "</p>"
                + "<button style='font-size:17px;padding:12px 26px;border-radius:10px;border:0;background:#2A5C8A;color:#fff' onclick='chrome.webview.postMessage(JSON.stringify({a:\"ulang\"}))'>Coba lagi</button>"
                + tombolTutup + "</div></body></html>";
            halamanGagal = true;
            try { web.CoreWebView2.NavigateToString(html); } catch (Exception) { halamanGagal = false; }
        }

        /// <summary>Pesan dari halaman ujian (halaman utama atau iframe): {"a":"kunci"|"buka"|"tutup"|"siap"|"ulang"}.</summary>
        private void PesanHalaman(object sender, CoreWebView2WebMessageReceivedEventArgs a)
        {
            string m;
            try { m = a.TryGetWebMessageAsString(); } catch (ArgumentException) { return; }
            Match cocok = Regex.Match(m ?? "", "\"a\"\\s*:\\s*\"(\\w+)\"");
            if (!cocok.Success) return;
            bool dariWeb = a.Source != null && a.Source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            switch (cocok.Groups[1].Value)
            {
                case "kunci":
                    if (dariWeb) MasukModeUjian();
                    break;
                case "buka":
                    if (!dariWeb) break;
                    if (daruratSampai != DateTime.MinValue)
                        BeginInvoke(new Action(() => SelesaiDarurat(true)));   // jangan buka kotak pesan di dalam event WebView2
                    else KeluarModeUjian();
                    break;
                case "tutup":
                    // selama ujian aplikasi tidak bisa ditutup (terbuka lagi setelah jawaban terkirim)
                    if (!terkunci) Tutup();
                    break;
                case "ulang":
                    ulangOtomatis = 0;
                    try { web.CoreWebView2.Navigate(alamat); } catch (Exception) { }
                    break;
            }
        }

        /// <summary>Kirim pesan {"t":…} ke halaman utama dan semua iframe (halaman ujian bisa di salah satunya).</summary>
        private void KirimKeHalaman(string t)
        {
            string pesan = "{\"t\":\"" + t + "\"}";
            try { if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsString(pesan); } catch (Exception) { }
            foreach (CoreWebView2Frame f in bingkai.ToArray())
            {
                try { f.PostWebMessageAsString(pesan); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------ mode ujian
        private void MasukModeUjian()
        {
            if (terkunci) return;
            terkunci = true;
            LayarPenuh(true);
            penjaga.Pasang(() => terkunci);
            Penjaga.CegahTangkapLayar(Handle);
            TutupLayarLain();
            Penjaga.MatikanPengelolaTugas();
            try { Clipboard.Clear(); } catch (Exception) { }   // jawaban tidak bisa ditempel dari catatan
            Penjaga.BawaKeDepan(Handle);
        }

        private void KeluarModeUjian()
        {
            bool tadi = terkunci;
            terkunci = false;
            Penjaga.PulihkanPengelolaTugas();
            if (!tadi) return;
            penjaga.Lepas();
            Penjaga.IzinkanTangkapLayar(Handle);
            BukaLayarLain();
            if (!bolehTutup) LayarPenuh(false);
        }

        /// <summary>Layar penuh tanpa bingkai & selalu di depan (ujian) ↔ jendela biasa yang dimaksimalkan.</summary>
        private void LayarPenuh(bool penuh)
        {
            if (penuh == layarPenuh) return;
            layarPenuh = penuh;
            if (penuh)
            {
                Rectangle layar = Screen.FromHandle(Handle).Bounds;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = layar;
                TopMost = true;
            }
            else
            {
                TopMost = false;
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Maximized;
            }
        }

        // ------------------------------------------------------------ darurat: Ctrl+C+B
        /// <summary>Ctrl+C+B selama ujian: halaman langsung mengirim jawaban ke server, lalu aplikasi ditutup.</summary>
        private void Darurat()
        {
            if (!terkunci || bolehTutup || daruratSampai != DateTime.MinValue) return;
            daruratSampai = DateTime.Now.AddSeconds(BatasDaruratDetik);
            KirimKeHalaman("kirim");
        }

        /// <summary>terkirim = server sudah menerima jawaban; false = batas waktu habis (jawaban tetap di komputer).</summary>
        private void SelesaiDarurat(bool terkirim)
        {
            daruratSampai = DateTime.MinValue;
            bolehTutup = true;
            jaga.Stop();
            KeluarModeUjian();
            TopMost = false;
            string pesan = terkirim
                ? "Jawaban sudah terkirim ke server. Aplikasi ditutup."
                : "Jawaban belum bisa dikirim (koneksi terputus atau komputer lambat).\n\nJawaban tetap tersimpan di komputer ini dan langsung dikirim begitu CBT Ujian dibuka lagi.";
            MessageBox.Show(this, pesan, "CBT Ujian", MessageBoxButtons.OK, terkirim ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            Close();
        }

        private void Tutup()
        {
            bolehTutup = true;
            KeluarModeUjian();
            Close();
        }

        private void Gagal(string pesan)
        {
            jaga.Stop();
            penjaga.Lepas();
            TopMost = false;
            MessageBox.Show(this, pesan, "CBT Ujian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Tutup();
        }

        /// <summary>Jendela ujian selalu di depan. Saat ujian, jendela yang ditinggalkan dicatat ke halaman.</summary>
        private void Jaga()
        {
            if (bolehTutup || !terkunci) return;   // sebelum & sesudah ujian: jendela biasa, tidak dijaga
            if (daruratSampai != DateTime.MinValue && DateTime.Now > daruratSampai) { SelesaiDarurat(false); return; }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromHandle(Handle).Bounds;
            }
            IntPtr depan = Win.GetForegroundWindow();
            if (depan != Handle && !Penjaga.MilikSendiri(depan))
            {
                Penjaga.BawaKeDepan(Handle);
                if ((DateTime.Now - terakhirLapor).TotalSeconds > 3)
                {
                    terakhirLapor = DateTime.Now;
                    KirimKeHalaman("keluar");
                }
            }
            if (!TopMost) TopMost = true;
            foreach (Form f in layarLain) if (!f.TopMost) f.TopMost = true;
        }

        /// <summary>Layar kedua/proyektor ditutup hitam selama ujian.</summary>
        private void TutupLayarLain()
        {
            if (layarLain.Count > 0) return;
            string layarUjian = Screen.FromHandle(Handle).DeviceName;
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.DeviceName == layarUjian) continue;
                var f = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = s.Bounds,
                    BackColor = Color.Black,
                    TopMost = true,
                    ShowInTaskbar = false
                };
                f.Show(this);
                layarLain.Add(f);
            }
        }

        private void BukaLayarLain()
        {
            foreach (Form f in layarLain) { try { f.Close(); } catch (Exception) { } }
            layarLain.Clear();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool sistem = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;
            if (!bolehTutup && terkunci && !sistem)
            {
                e.Cancel = true;
                KirimKeHalaman("tutup");   // halaman memberi tahu: tutup setelah jawaban terkirim (darurat: Ctrl+C+B)
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            jaga.Stop();
            penjaga.Lepas();
            Penjaga.PulihkanPengelolaTugas();
            BukaLayarLain();
            base.OnFormClosed(e);
        }
    }
}
