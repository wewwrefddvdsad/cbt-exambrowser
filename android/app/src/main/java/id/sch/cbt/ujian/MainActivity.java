package id.sch.cbt.ujian;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.ActivityManager;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.PixelFormat;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.provider.Settings;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.SslErrorHandler;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.webkit.WebViewDatabase;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * CBT Ujian — aplikasi ujian (seperti exambrowser) untuk halaman siswa …/ujian-cbt/.
 *
 * Sebelum ujian: aplikasi biasa (bisa ditutup).
 * Setelah siswa menekan "Mulai mengerjakan" (halaman memanggil CBTAndroid.kunci):
 *   - layar disematkan (screen pinning) → tombol Beranda & Terbaru tidak berfungsi;
 *   - layar penuh, layar tetap menyala, tangkap/rekam layar diblokir, jendela mengambang aplikasi lain disembunyikan;
 *   - tombol Kembali tidak menutup aplikasi (halaman menampilkan pemberitahuan);
 *   - bila siswa berhasil keluar (mis. melepas sematan), lapisan "Ujian sedang berlangsung" mengambang menutupi
 *     layar dan aplikasi kembali ke depan sendiri; kejadian ini dicatat sebagai keluar halaman.
 * Kunci dibuka setelah jawaban terkirim atau ujian direset pengawas (CBTAndroid.buka). Tidak ada token keluar.
 * Isi-otomatis (autofill) & tawaran "simpan sandi" Google dimatikan untuk seluruh aplikasi.
 */
public class MainActivity extends Activity {
    static final String VERSI = "1.2";
    private static final String PREF = "cbt";

    private final Handler h = new Handler(Looper.getMainLooper());
    private FrameLayout akar;
    private WebView web;
    private SharedPreferences pref;
    private String alamat;
    private boolean terkunci = false;       // mode ujian
    private boolean tampil = false;         // activity sedang di depan
    private boolean halamanGagal = false;
    private boolean gagalKini = false;      // pemuatan halaman yang sedang berjalan gagal
    private boolean halamanSiap = false;    // halaman ujian sudah termuat
    private int ulangOtomatis = 0;          // percobaan ulang otomatis setelah gagal (maks. 2)
    private boolean dialogTerbuka = false;
    private boolean pernahTersemat = false;
    private boolean izinDitanya = false;
    private long jedaPin = 0;               // tunggu sebelum meminta sematan lagi (dialog sistem sedang tampil)
    private long hilangFokus = 0;
    private View lapisan;                   // lapisan mengambang saat siswa keluar dari aplikasi

    // ------------------------------------------------------------------ siklus hidup
    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        pref = getSharedPreferences(PREF, MODE_PRIVATE);
        Window w = getWindow();
        w.addFlags(WindowManager.LayoutParams.FLAG_SECURE);                 // blokir tangkap & rekam layar
        if (Build.VERSION.SDK_INT >= 31) w.setHideOverlayWindows(true);     // sembunyikan jendela mengambang aplikasi lain
        // tanpa isi-otomatis / "Simpan sandi ke Google?" di seluruh aplikasi (Android 8+)
        if (Build.VERSION.SDK_INT >= 26) w.getDecorView().setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
        akar = new FrameLayout(this);
        akar.setBackgroundColor(Color.WHITE);
        if (Build.VERSION.SDK_INT >= 26) akar.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
        if (Build.VERSION.SDK_INT >= 30) {
            // Android 15+: tampilan sampai tepi layar → beri jarak untuk bilah sistem, poni/lubang kamera & keyboard,
            // supaya kepala halaman tidak tertutup atau berhimpitan dengan bilah status
            akar.setOnApplyWindowInsetsListener((v, ins) -> {
                Insets i = ins.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout() | WindowInsets.Type.ime());
                v.setPadding(i.left, i.top, i.right, i.bottom);
                return ins;
            });
        }
        setContentView(akar);
        buatWeb();
        alamat = pref.getString("alamat", getString(R.string.alamat_ujian));
        if (!alamatSah(alamat)) mintaAlamat(); else muat();
        mintaIzinMengambang(false);
    }

    @Override
    protected void onResume() {
        super.onResume();
        tampil = true;
        h.removeCallbacks(bawaKembali);
        hapusLapisan();
        if (terkunci) { sembunyikanBar(); pastikanTersemat(); }
    }

    @Override
    protected void onPause() {
        super.onPause();
        tampil = false;
        if (terkunci) h.postDelayed(cekKeluar, 350);
    }

    @Override
    protected void onDestroy() {
        h.removeCallbacksAndMessages(null);
        hapusLapisan();
        if (web != null) web.destroy();
        super.onDestroy();
    }

    @Override
    public void onWindowFocusChanged(boolean ada) {
        super.onWindowFocusChanged(ada);
        if (ada) { hilangFokus = 0; if (terkunci) sembunyikanBar(); }
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        // halaman menampilkan konfirmasi tutup; saat ujian berlangsung hanya pemberitahuan (aplikasi tetap terbuka)
        if (!terkunci && (halamanGagal || web == null || web.getUrl() == null)) { tanyaTutup(); return; }
        kirimKeHalaman("tutup");
    }

    // ------------------------------------------------------------------ WebView
    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @SuppressWarnings("deprecation")
    private void buatWeb() {
        if (web != null) { akar.removeView(web); web.destroy(); }
        web = new WebView(this);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);            // soal & jawaban disimpan di perangkat (localStorage)
        s.setDatabaseEnabled(true);
        s.setAllowFileAccess(false);
        s.setAllowContentAccess(false);
        s.setMediaPlaybackRequiresUserGesture(true);
        s.setSupportMultipleWindows(false);
        s.setJavaScriptCanOpenWindowsAutomatically(false);
        s.setBuiltInZoomControls(true);
        s.setDisplayZoomControls(false);
        s.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        s.setTextZoom(100);                       // ukuran huruf sistem tidak membesarkan halaman (tulisan tidak berdempet)
        s.setSaveFormData(false);                 // tidak menyimpan isian formulir
        if (Build.VERSION.SDK_INT >= 26) web.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
        try { WebViewDatabase.getInstance(this).clearFormData(); } catch (Exception e) { /* tidak ada */ }
        // kunci rahasia disisipkan ke User-Agent → server hanya melayani halaman ujian untuk aplikasi ini
        String kunci = getString(R.string.app_kunci);
        s.setUserAgentString(s.getUserAgentString() + " CBTUjian/" + VERSI + " (Android)" + (kunci.isEmpty() ? "" : " k/" + kunci));
        WebView.setWebContentsDebuggingEnabled(false);
        web.setLongClickable(false);
        web.setOnLongClickListener(v -> true);   // tanpa menu salin/tempel
        web.setHapticFeedbackEnabled(false);
        web.setBackgroundColor(0xFFF2F5F8);
        web.addJavascriptInterface(new Jembatan(), "CBTAndroid");
        web.setWebChromeClient(new WebChromeClient());
        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest r) { return !bolehDibuka(r.getUrl()); }

            @Override
            @SuppressWarnings("deprecation")
            public boolean shouldOverrideUrlLoading(WebView v, String url) { return !bolehDibuka(Uri.parse(url)); }

            @Override
            public void onPageStarted(WebView v, String url, android.graphics.Bitmap ikon) { if (url != null && url.startsWith("http")) gagalKini = false; }

            @Override
            public void onPageFinished(WebView v, String url) {
                if (url == null || !url.startsWith("http") || gagalKini) return;
                halamanGagal = false;
                halamanSiap = true;
                ulangOtomatis = 0;
            }

            @Override
            public void onReceivedError(WebView v, WebResourceRequest r, WebResourceError e) {
                if (r.isForMainFrame()) gagalMuat(String.valueOf(e.getDescription()));
            }

            @Override
            @SuppressWarnings("deprecation")
            public void onReceivedError(WebView v, int kode, String ket, String url) { if (Build.VERSION.SDK_INT < 23) gagalMuat(ket); }

            @Override
            public void onReceivedSslError(WebView v, SslErrorHandler hd, SslError e) {
                hd.cancel();                                    // sertifikat tidak sah: jangan dilanjutkan
                String hs = null;
                try { hs = Uri.parse(e.getUrl()).getHost(); } catch (Exception x) { /* alamat tidak terbaca */ }
                String ha = alamat == null ? null : Uri.parse(alamat).getHost();
                // halaman ujian sendiri tidak bisa dibuka: biasanya tanggal/jam HP salah
                if (!halamanSiap && hs != null && hs.equalsIgnoreCase(ha)) gagalMuat("sertifikat");
            }

            @Override
            public boolean onRenderProcessGone(WebView v, RenderProcessGoneDetail d) { buatWeb(); muat(); return true; }
        });
        akar.addView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void muat() {
        if (!alamatSah(alamat)) return;
        halamanSiap = false;
        web.getSettings().setCacheMode(adaInternet() ? WebSettings.LOAD_DEFAULT : WebSettings.LOAD_CACHE_ELSE_NETWORK);
        web.loadUrl(alamat);
    }

    /** Hanya halaman di server ujian yang sama yang boleh dibuka. */
    private boolean bolehDibuka(Uri u) {
        Uri a = Uri.parse(alamat);
        return u != null && "https".equals(u.getScheme()) && a.getHost() != null && a.getHost().equalsIgnoreCase(u.getHost());
    }

    /** Halaman gagal dimuat: tampilkan pesan, lalu coba lagi otomatis 2 kali (jaringan sekolah sering padat
     *  saat ratusan siswa membuka aplikasi bersamaan). */
    private void gagalMuat(String ket) {
        if (gagalKini && halamanGagal) return;   // kegagalan yang sama sudah ditangani (mis. sertifikat lalu galat jaringan)
        gagalKini = true;
        halamanSiap = false;
        tampilkanGagal(ket);
        if (ulangOtomatis < 2) {
            ulangOtomatis++;
            h.removeCallbacks(ulang);
            h.postDelayed(ulang, 2500L * ulangOtomatis);
        }
    }

    private final Runnable ulang = new Runnable() {
        @Override
        public void run() { muat(); }
    };

    private void tampilkanGagal(String ket) {
        halamanGagal = true;
        String k = ket == null ? "" : ket;
        String besar = k.toUpperCase();
        boolean jam = k.contains("sertifikat") || besar.contains("CERT") || besar.contains("SSL");
        String saran = jam ? "Periksa tanggal dan jam di HP ini (aktifkan tanggal &amp; jam otomatis), lalu coba lagi."
                : "Periksa koneksi internet, lalu coba lagi.";
        String html = "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'></head>"
                + "<body style='font-family:sans-serif;padding:32px 24px;text-align:center;color:#1C2833;background:#F2F5F8;line-height:1.5'>"
                + "<h2 style='font-size:20px'>Halaman ujian tidak bisa dibuka</h2><p style='color:#566574'>" + saran + "</p>"
                + (ulangOtomatis < 2 ? "<p style='color:#8A97A5;font-size:14px'>Mencoba lagi otomatis...</p>" : "")
                + "<button style='font-size:17px;padding:12px 28px;border-radius:10px;border:0;background:#2A5C8A;color:#fff' onclick='CBTAndroid.muatUlang()'>Coba lagi</button>"
                + "<p style='color:#8A97A5;font-size:12px;margin-top:28px'>" + aman(k) + " - CBT Ujian " + VERSI + "</p>"
                + "</body></html>";
        web.loadDataWithBaseURL(null, html, "text/html", "utf-8", null);
    }

    private static String aman(String t) { return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"); }

    private void kirimKeHalaman(String t) {
        if (web != null) web.evaluateJavascript("window.dispatchEvent(new CustomEvent('cbt-app',{detail:{t:'" + t + "'}}))", null);
    }

    /** Dipanggil dari halaman ujian (JavaScript). */
    private class Jembatan {
        /** Izin "Tampil di atas aplikasi lain" sudah ada? Bila belum, setelannya dibuka. */
        @JavascriptInterface
        public String siap() {
            if (Build.VERSION.SDK_INT < 23 || Settings.canDrawOverlays(MainActivity.this)) return "ya";
            h.post(() -> mintaIzinMengambang(true));
            return "tidak";
        }

        @JavascriptInterface
        public void kunci(String info) { h.post(MainActivity.this::masukModeUjian); }

        @JavascriptInterface
        public void buka() { h.post(MainActivity.this::keluarModeUjian); }

        @JavascriptInterface
        public void tutup() { h.post(() -> { if (!terkunci) finishAndRemoveTask(); }); }   // saat ujian: tidak bisa ditutup

        @JavascriptInterface
        public void muatUlang() { h.post(MainActivity.this::muat); }

        @JavascriptInterface
        public String info() { return "{\"platform\":\"android\",\"versi\":\"" + VERSI + "\",\"sdk\":" + Build.VERSION.SDK_INT + "}"; }
    }

    // ------------------------------------------------------------------ mode ujian
    private void masukModeUjian() {
        if (terkunci) { pastikanTersemat(); return; }
        terkunci = true;
        pernahTersemat = false;
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        kosongkanPapanKlip();
        sembunyikanBar();
        pastikanTersemat();
        h.removeCallbacks(jaga);
        h.postDelayed(jaga, 1000);
    }

    private void keluarModeUjian() {
        terkunci = false;
        h.removeCallbacks(jaga);
        h.removeCallbacks(bawaKembali);
        h.removeCallbacks(cekKeluar);
        hapusLapisan();
        getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        try { if (tersemat()) stopLockTask(); } catch (Exception e) { /* tidak sedang disematkan */ }
        tampilkanBar();
    }

    @SuppressWarnings("deprecation")
    private boolean tersemat() {
        ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
        if (am == null) return false;
        if (Build.VERSION.SDK_INT >= 23) return am.getLockTaskModeState() != ActivityManager.LOCK_TASK_MODE_NONE;
        return am.isInLockTaskMode();
    }

    /** Sematkan layar (screen pinning). Android meminta konfirmasi siswa; diminta lagi bila sematan dilepas. */
    private void pastikanTersemat() {
        if (!terkunci || !tampil || tersemat()) return;
        long t = System.currentTimeMillis();
        if (t < jedaPin) return;
        jedaPin = t + 6000;
        try { startLockTask(); } catch (Exception e) { /* perangkat menolak sematan: lapisan mengambang tetap menjaga */ }
    }

    /** Pemeriksaan tiap detik selama ujian. */
    private final Runnable jaga = new Runnable() {
        @Override
        public void run() {
            if (!terkunci) return;
            long t = System.currentTimeMillis();
            boolean pin = tersemat();
            if (pin) pernahTersemat = true;
            else if (pernahTersemat && tampil) { pernahTersemat = false; kirimKeHalaman("keluar"); jedaPin = 0; }   // sematan dilepas
            pastikanTersemat();
            // panel notifikasi / jendela lain menutupi ujian lebih dari 1,5 detik
            if (tampil && !hasWindowFocus() && !dialogTerbuka && t > jedaPin) {
                if (hilangFokus == 0) hilangFokus = t;
                else if (t - hilangFokus > 1500) { kirimKeHalaman("keluar"); hilangFokus = t + 8000; }
            } else if (hasWindowFocus()) hilangFokus = 0;
            if (Build.VERSION.SDK_INT >= 24 && isInMultiWindowMode()) kirimKeHalaman("keluar");
            h.postDelayed(this, 1000);
        }
    };

    /** Activity tidak di depan saat ujian: bila layar menyala (bukan tombol daya), siswa keluar dari aplikasi. */
    private final Runnable cekKeluar = new Runnable() {
        @Override
        public void run() {
            if (!terkunci || tampil) return;
            PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
            if (pm != null && !pm.isInteractive()) return;     // layar dimatikan: tidak dihitung
            kirimKeHalaman("keluar");
            tampilkanLapisan();
            h.removeCallbacks(bawaKembali);
            h.postDelayed(bawaKembali, 300);
        }
    };

    /** Bawa aplikasi kembali ke depan selama ujian (butuh izin "Tampil di atas aplikasi lain" di Android 10+). */
    private final Runnable bawaKembali = new Runnable() {
        @Override
        public void run() {
            if (!terkunci || tampil) return;
            kembaliKeUjian();
            h.postDelayed(this, 1500);
        }
    };

    private void kembaliKeUjian() {
        try {
            ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
            if (am != null) am.moveTaskToFront(getTaskId(), 0);
        } catch (Exception e) { /* izin ditolak sistem */ }
        try {
            Intent i = new Intent(this, MainActivity.class);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            startActivity(i);
        } catch (Exception e) { /* dicoba lagi */ }
    }

    // ------------------------------------------------------------------ lapisan mengambang
    @SuppressWarnings("deprecation")
    private void tampilkanLapisan() {
        if (lapisan != null) return;
        if (Build.VERSION.SDK_INT >= 23 && !Settings.canDrawOverlays(this)) return;
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        l.setGravity(Gravity.CENTER);
        l.setBackgroundColor(0xF51C2833);
        int p = dp(28);
        l.setPadding(p, p, p, p);
        TextView judul = new TextView(this);
        judul.setText("Ujian sedang berlangsung");
        judul.setTextColor(Color.WHITE);
        judul.setTextSize(24);
        judul.setGravity(Gravity.CENTER);
        TextView ket = new TextView(this);
        ket.setText("Aplikasi CBT Ujian terkunci sampai jawaban terkirim. Setiap kali keluar dari ujian akan dicatat.");
        ket.setTextColor(0xFFD3DDE6);
        ket.setTextSize(16);
        ket.setGravity(Gravity.CENTER);
        ket.setPadding(0, dp(12), 0, dp(24));
        Button tombol = new Button(this);
        tombol.setText("Kembali ke ujian");
        tombol.setOnClickListener(v -> kembaliKeUjian());
        l.addView(judul);
        l.addView(ket);
        l.addView(tombol);
        int tipe = Build.VERSION.SDK_INT >= 26 ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY : WindowManager.LayoutParams.TYPE_PHONE;
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT, tipe,
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN | WindowManager.LayoutParams.FLAG_FULLSCREEN | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
                PixelFormat.TRANSLUCENT);
        lp.gravity = Gravity.TOP | Gravity.START;
        try {
            WindowManager wm = (WindowManager) getApplicationContext().getSystemService(Context.WINDOW_SERVICE);
            if (wm != null) { wm.addView(l, lp); lapisan = l; }
        } catch (Exception e) { lapisan = null; }
    }

    private void hapusLapisan() {
        if (lapisan == null) return;
        try {
            WindowManager wm = (WindowManager) getApplicationContext().getSystemService(Context.WINDOW_SERVICE);
            if (wm != null) wm.removeView(lapisan);
        } catch (Exception e) { /* sudah hilang */ }
        lapisan = null;
    }

    // ------------------------------------------------------------------ tampilan sistem
    @SuppressWarnings("deprecation")
    private void sembunyikanBar() {
        if (!terkunci) return;
        if (Build.VERSION.SDK_INT >= 30) {
            WindowInsetsController c = getWindow().getInsetsController();
            if (c != null) {
                c.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        }
    }

    @SuppressWarnings("deprecation")
    private void tampilkanBar() {
        if (Build.VERSION.SDK_INT >= 30) {
            WindowInsetsController c = getWindow().getInsetsController();
            if (c != null) c.show(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
        } else {
            getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_VISIBLE);
        }
    }

    // ------------------------------------------------------------------ izin, alamat, lain-lain
    /** Izin "Tampil di atas aplikasi lain": untuk lapisan mengambang & membawa aplikasi kembali ke depan. */
    private void mintaIzinMengambang(boolean langsung) {
        if (Build.VERSION.SDK_INT < 23 || Settings.canDrawOverlays(this)) return;
        if (langsung) { bukaSetelanMengambang(); return; }
        if (izinDitanya) return;
        izinDitanya = true;
        dialogTerbuka = true;
        new AlertDialog.Builder(this, android.R.style.Theme_Material_Light_Dialog_Alert)
                .setTitle("Izinkan tampil di atas aplikasi lain")
                .setMessage("CBT Ujian memerlukan izin \"Tampil di atas aplikasi lain\" agar tetap terkunci selama ujian. Pada layar berikutnya, pilih CBT Ujian lalu aktifkan izinnya.")
                .setPositiveButton("Buka setelan", (d, i) -> bukaSetelanMengambang())
                .setNegativeButton("Nanti", null)
                .setOnDismissListener(d -> dialogTerbuka = false)
                .show();
    }

    private void bukaSetelanMengambang() {
        try {
            startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName())));
        } catch (Exception e) {
            try { startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName()))); } catch (Exception e2) { /* tidak ada */ }
        }
    }

    private boolean alamatSah(String a) {
        return a != null && a.startsWith("https://") && a.length() > 12 && !a.contains("://example/");
    }

    /** Alamat ujian belum dimasukkan saat aplikasi dibangun: tanyakan sekali, lalu disimpan. */
    private void mintaAlamat() {
        final EditText isian = new EditText(this);
        isian.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        isian.setHint("https://alamat-sekolah/ujian-cbt/");
        isian.setSingleLine(true);
        if (Build.VERSION.SDK_INT >= 26) isian.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);
        LinearLayout bungkus = new LinearLayout(this);
        int p = dp(20);
        bungkus.setPadding(p, dp(8), p, 0);
        bungkus.addView(isian, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        dialogTerbuka = true;
        final AlertDialog d = new AlertDialog.Builder(this, android.R.style.Theme_Material_Light_Dialog_Alert)
                .setTitle("Alamat ujian")
                .setMessage("Masukkan alamat halaman ujian siswa dari sekolah (diakhiri /ujian-cbt/).")
                .setView(bungkus)
                .setCancelable(false)
                .setPositiveButton("Simpan", null)
                .setNegativeButton("Tutup aplikasi", (x, i) -> finish())
                .create();
        d.setOnShowListener(x -> d.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String a = rapikanAlamat(isian.getText().toString());
            if (!alamatSah(a)) { isian.setError("Alamat harus diawali https://"); return; }
            alamat = a;
            pref.edit().putString("alamat", a).apply();
            dialogTerbuka = false;
            d.dismiss();
            muat();
        }));
        d.show();
    }

    private static String rapikanAlamat(String a) {
        a = a == null ? "" : a.trim();
        if (a.isEmpty()) return a;
        if (!a.startsWith("http://") && !a.startsWith("https://")) a = "https://" + a;
        if (a.startsWith("http://")) a = "https://" + a.substring(7);
        if (!a.endsWith("/")) a = a + "/";
        if (!a.contains("/ujian-cbt/")) a = a + "ujian-cbt/";
        return a;
    }

    private void tanyaTutup() {
        dialogTerbuka = true;
        new AlertDialog.Builder(this, android.R.style.Theme_Material_Light_Dialog_Alert)
                .setTitle("Tutup aplikasi?")
                .setPositiveButton("Tutup", (d, i) -> finishAndRemoveTask())
                .setNegativeButton("Batal", null)
                .setOnDismissListener(d -> dialogTerbuka = false)
                .show();
    }

    @SuppressWarnings("deprecation")
    private boolean adaInternet() {
        try {
            ConnectivityManager cm = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
            NetworkInfo ni = cm == null ? null : cm.getActiveNetworkInfo();
            return ni != null && ni.isConnected();
        } catch (Exception e) { return true; }
    }

    /** Papan klip dikosongkan saat ujian dimulai (jawaban tidak bisa ditempel dari catatan). */
    private void kosongkanPapanKlip() {
        try {
            ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            if (cm == null) return;
            if (Build.VERSION.SDK_INT >= 28) cm.clearPrimaryClip();
            else cm.setPrimaryClip(ClipData.newPlainText("", ""));
        } catch (Exception e) { /* tidak penting */ }
    }

    private int dp(int n) { return Math.round(n * getResources().getDisplayMetrics().density); }
}
