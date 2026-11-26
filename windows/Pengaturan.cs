using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CBTUjian
{
    /// <summary>Alamat yang dibuka aplikasi: link blog/website sekolah atau langsung …/ujian-cbt/ (dipakai apa adanya).</summary>
    internal static class Pengaturan
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CBT-Ujian");

        /// <summary>Asal alamat yang sedang dipakai (ditampilkan saat halaman gagal dibuka).</summary>
        internal static string Asal = "";

        /// <summary>
        /// Urutan: alamat.txt di samping exe → alamat bawaan saat dibangun → alamat yang pernah dimasukkan.
        /// Alamat yang pernah dimasukkan (folder data aplikasi) hanya dipakai exe yang dibangun TANPA alamat,
        /// supaya alamat lama dari versi sebelumnya tidak menimpa alamat exe yang baru.
        /// </summary>
        internal static string AlamatUjian()
        {
            string a = Baca(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alamat.txt"));
            if (a != null) { Asal = "alamat.txt di samping aplikasi"; return a; }
            string b = Bawaan();
            if (Sah(b)) { Asal = "bawaan aplikasi"; return b; }
            a = Baca(Path.Combine(Folder, "alamat.txt"));
            if (a != null) { Asal = "alamat yang pernah dimasukkan"; return a; }
            return null;
        }

        private static string Baca(string f)
        {
            try
            {
                if (!File.Exists(f)) return null;
                string a = Rapikan(File.ReadAllText(f));
                return Sah(a) ? a : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string Bawaan()
        {
            foreach (object o in Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                var m = (AssemblyMetadataAttribute)o;
                if (m.Key == "AlamatUjian") return Rapikan(m.Value);
            }
            return null;
        }

        /// <summary>Kunci rahasia untuk membuka halaman ujian (disatukan ke exe saat dibangun). "" = tanpa kunci.</summary>
        internal static string AppKunci()
        {
            foreach (object o in Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                var m = (AssemblyMetadataAttribute)o;
                if (m.Key == "AppKunci") return (m.Value ?? "").Trim();
            }
            return "";
        }

        internal static bool Sah(string a)
        {
            Uri u;
            return a != null && a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && a.Length > 12
                && a.IndexOf("://example/", StringComparison.OrdinalIgnoreCase) < 0
                && Uri.TryCreate(a, UriKind.Absolute, out u) && u.Host.IndexOf('.') > 0;
        }

        /// <summary>Hanya memastikan diawali https://. Alamat TIDAK ditambah /ujian-cbt/ (sama seperti aplikasi Android).</summary>
        internal static string Rapikan(string a)
        {
            a = (a ?? "").Trim();
            if (a.Length == 0) return a;
            if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) a = "https://" + a.Substring(7);
            if (!a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) a = "https://" + a;
            return a;
        }

        internal static void Simpan(string a)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "alamat.txt"), a);
        }

        /// <summary>Alamat belum ada (tidak diisi saat dibangun): tanyakan sekali.</summary>
        internal static string Tanya(IWin32Window pemilik)
        {
            using (var f = new Form())
            {
                f.Text = "Alamat ujian";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false; f.TopMost = true;
                f.ClientSize = new Size(520, 170);
                f.Font = new Font("Segoe UI", 10f);
                var ket = new Label { Text = "Masukkan alamat ujian dari sekolah (link blog sekolah, atau alamat yang diakhiri /ujian-cbt/):", Left = 16, Top = 16, Width = 488, Height = 40 };
                var isian = new TextBox { Left = 16, Top = 60, Width = 488, Text = "https://" };
                var salah = new Label { Left = 16, Top = 92, Width = 488, ForeColor = Color.Firebrick, Text = "" };
                var ok = new Button { Text = "Simpan", Left = 314, Top = 124, Width = 90, Height = 32 };
                var batal = new Button { Text = "Tutup", Left = 414, Top = 124, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
                string hasil = null;
                ok.Click += (s, e) =>
                {
                    string a = Rapikan(isian.Text);
                    if (!Sah(a)) { salah.Text = "Alamat harus diawali https:// dan berisi nama server sekolah."; return; }
                    hasil = a;
                    Asal = "alamat yang dimasukkan";
                    try { Simpan(a); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    f.DialogResult = DialogResult.OK;
                };
                f.Controls.AddRange(new Control[] { ket, isian, salah, ok, batal });
                f.AcceptButton = ok; f.CancelButton = batal;
                return f.ShowDialog(pemilik) == DialogResult.OK ? hasil : null;
            }
        }
    }
}
