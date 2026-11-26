using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace CBTUjian
{
    internal static class Program
    {
        internal const string Versi = "1.4";

        [STAThread]
        private static void Main()
        {
            // pustaka WebView2 ada di dalam exe → harus dipasang sebelum kelas yang memakainya dimuat
            AppDomain.CurrentDomain.AssemblyResolve += MuatPustaka;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool baru;
            using (var satu = new Mutex(true, "CBTUjian-satu-jendela", out baru))
            {
                if (!baru) return;   // aplikasi sudah terbuka
                Jalankan();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Jalankan()
        {
            // bila aplikasi sebelumnya mati mendadak saat ujian, Pengelola Tugas dipulihkan
            Penjaga.PulihkanPengelolaTugas();
            Application.Run(new FormUjian());
            Penjaga.PulihkanPengelolaTugas();
        }

        private static Assembly MuatPustaka(object sender, ResolveEventArgs e)
        {
            string nama = new AssemblyName(e.Name).Name;
            if (nama != "Microsoft.Web.WebView2.Core" && nama != "Microsoft.Web.WebView2.WinForms") return null;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lib." + nama + ".dll"))
            {
                if (s == null) return null;
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
        }

        /// <summary>Tulis WebView2Loader.dll (sesuai arsitektur) ke folder data aplikasi. null = pakai yang ada di samping exe.</summary>
        internal static string SiapkanPemuat()
        {
            string p = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
            string ars = p == "ARM64" ? "arm64" : (Environment.Is64BitProcess ? "x64" : "x86");
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("native." + ars + ".WebView2Loader.dll"))
            {
                if (s == null) return null;
                string folder = Path.Combine(Pengaturan.Folder, "pemuat", ars);
                string f = Path.Combine(folder, "WebView2Loader.dll");
                try
                {
                    if (!File.Exists(f) || new FileInfo(f).Length != s.Length)
                    {
                        Directory.CreateDirectory(folder);
                        using (var o = File.Create(f)) s.CopyTo(o);
                    }
                    return folder;
                }
                catch (IOException) { return File.Exists(f) ? folder : null; }
                catch (UnauthorizedAccessException) { return File.Exists(f) ? folder : null; }
            }
        }
    }
}
