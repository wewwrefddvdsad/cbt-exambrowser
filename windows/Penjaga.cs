using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CBTUjian
{
    /// <summary>
    /// Penguncian seperti exambrowser, HANYA selama ujian (sama seperti aplikasi Android):
    ///  - tombol Windows, Alt+Tab, Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc, Alt+Spasi, Alt+F4, Print Screen diblokir
    ///    (kait papan ketik dipasang saat ujian dimulai dan dilepas setelah jawaban terkirim);
    ///  - jendela tidak bisa ditangkap/direkam layar;
    ///  - Pengelola Tugas (Task Manager) dinonaktifkan untuk akun Windows ini, lalu dipulihkan;
    ///  - Ctrl+C+B = tombol darurat: jawaban langsung dikirim lalu aplikasi ditutup.
    /// Ctrl+Alt+Del tidak bisa diblokir oleh aplikasi apa pun (fitur keamanan Windows).
    /// </summary>
    internal sealed class Penjaga
    {
        private IntPtr kait = IntPtr.Zero;
        private Win.LowLevelKeyboardProc proc;   // disimpan agar tidak dibuang pengumpul sampah
        private Func<bool> sedangUjian = () => false;
        private DateTime terakhirDarurat = DateTime.MinValue;

        /// <summary>Dipanggil saat Ctrl+C+B ditekan selama ujian (kirim jawaban darurat).</summary>
        internal Action Darurat;

        internal void Pasang(Func<bool> ujian)
        {
            sedangUjian = ujian;
            if (kait != IntPtr.Zero) return;
            proc = Kait;
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule m = p.MainModule)
                kait = Win.SetWindowsHookEx(Win.WH_KEYBOARD_LL, proc, Win.GetModuleHandle(m.ModuleName), 0);
        }

        internal void Lepas()
        {
            if (kait != IntPtr.Zero) Win.UnhookWindowsHookEx(kait);
            kait = IntPtr.Zero;
        }

        private IntPtr Kait(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (Win.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win.KBDLLHOOKSTRUCT));
                int pesan = wParam.ToInt32();
                if ((pesan == Win.WM_KEYDOWN || pesan == Win.WM_SYSKEYDOWN) && TombolDarurat(k)) return (IntPtr)1;
                if (Blokir(k)) return (IntPtr)1;
            }
            return Win.CallNextHookEx(kait, nCode, wParam, lParam);
        }

        /// <summary>Ctrl+C+B (C dan B ditekan bersamaan sambil menahan Ctrl, urutan bebas) selama ujian.</summary>
        private bool TombolDarurat(Win.KBDLLHOOKSTRUCT k)
        {
            if (Darurat == null || !sedangUjian()) return false;
            int lain;
            if (k.vkCode == Win.VK_C) lain = Win.VK_B;
            else if (k.vkCode == Win.VK_B) lain = Win.VK_C;
            else return false;
            if ((Win.GetAsyncKeyState(Win.VK_CONTROL) & 0x8000) == 0 || (Win.GetAsyncKeyState(lain) & 0x8000) == 0) return false;
            if ((DateTime.Now - terakhirDarurat).TotalSeconds >= 2)
            {
                terakhirDarurat = DateTime.Now;
                try { Darurat(); } catch (Exception) { }
            }
            return true;
        }

        private bool Blokir(Win.KBDLLHOOKSTRUCT k)
        {
            Keys v = (Keys)(int)k.vkCode;
            bool alt = (k.flags & Win.LLKHF_ALTDOWN) != 0;
            bool ctrl = (Win.GetAsyncKeyState(Win.VK_CONTROL) & 0x8000) != 0;
            switch (v)
            {
                case Keys.LWin:
                case Keys.RWin:
                case Keys.Apps:
                case Keys.PrintScreen:
                case Keys.BrowserBack:
                case Keys.BrowserForward:
                case Keys.BrowserHome:
                case Keys.BrowserSearch:
                case Keys.BrowserRefresh:
                case Keys.LaunchMail:
                case Keys.LaunchApplication1:
                case Keys.LaunchApplication2:
                case Keys.SelectMedia:
                    return true;
                case Keys.Tab:
                case Keys.Space:
                    return alt;                       // Alt+Tab, Alt+Spasi
                case Keys.Escape:
                    return alt || ctrl;               // Alt+Esc, Ctrl+Esc (menu Start), Ctrl+Shift+Esc (Pengelola Tugas)
                case Keys.F4:
                    return alt && sedangUjian();      // Alt+F4 hanya diblokir saat ujian
                default:
                    return false;
            }
        }

        /// <summary>Isi jendela tidak ikut tertangkap aplikasi tangkap/rekam layar (Windows 10 2004+; selain itu tampil hitam).</summary>
        internal static void CegahTangkapLayar(IntPtr h)
        {
            if (!Win.SetWindowDisplayAffinity(h, Win.WDA_EXCLUDEFROMCAPTURE)) Win.SetWindowDisplayAffinity(h, Win.WDA_MONITOR);
        }

        /// <summary>Setelah ujian: jendela kembali bisa ditangkap layar seperti biasa.</summary>
        internal static void IzinkanTangkapLayar(IntPtr h)
        {
            Win.SetWindowDisplayAffinity(h, Win.WDA_NONE);
        }

        /// <summary>Bawa jendela ujian kembali ke paling depan.</summary>
        internal static void BawaKeDepan(IntPtr h)
        {
            IntPtr depan = Win.GetForegroundWindow();
            uint pid;
            uint tDepan = Win.GetWindowThreadProcessId(depan, out pid);
            uint tSaya = Win.GetCurrentThreadId();
            bool ikat = tDepan != 0 && tDepan != tSaya && Win.AttachThreadInput(tSaya, tDepan, true);
            Win.ShowWindow(h, Win.SW_SHOW);
            Win.SetWindowPos(h, Win.HWND_TOPMOST, 0, 0, 0, 0, Win.SWP_NOMOVE | Win.SWP_NOSIZE | Win.SWP_SHOWWINDOW);
            Win.BringWindowToTop(h);
            Win.SetForegroundWindow(h);
            if (ikat) Win.AttachThreadInput(tSaya, tDepan, false);
        }

        /// <summary>Jendela milik aplikasi ini sendiri (kotak dialog, atau daftar pilihan/menu dari mesin WebView2)?</summary>
        internal static bool MilikSendiri(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            uint pid;
            Win.GetWindowThreadProcessId(h, out pid);
            using (Process saya = Process.GetCurrentProcess()) if (pid == (uint)saya.Id) return true;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return string.Equals(p.ProcessName, "msedgewebview2", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------ Pengelola Tugas selama ujian
        private const string KunciKebijakan = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string KunciAplikasi = @"Software\CBTUjian";

        internal static void MatikanPengelolaTugas()
        {
            try
            {
                using (RegistryKey a = Registry.CurrentUser.CreateSubKey(KunciAplikasi))
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KunciKebijakan))
                {
                    if (a == null || k == null || a.GetValue("TaskMgrDiubah") != null) return;
                    object lama = k.GetValue("DisableTaskMgr");
                    if (lama != null && Convert.ToInt32(lama) != 0) return;   // sudah dimatikan kebijakan sekolah
                    k.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                    a.SetValue("TaskMgrDiubah", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { /* tidak diizinkan: penguncian lain tetap berjalan */ }
        }

        internal static void PulihkanPengelolaTugas()
        {
            try
            {
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(KunciAplikasi, true))
                {
                    if (a == null || a.GetValue("TaskMgrDiubah") == null) return;
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KunciKebijakan, true))
                        if (k != null) k.DeleteValue("DisableTaskMgr", false);
                    a.DeleteValue("TaskMgrDiubah", false);
                }
            }
            catch (Exception) { }
        }
    }

    internal static class Win
    {
        internal const int WH_KEYBOARD_LL = 13;
        internal const uint LLKHF_ALTDOWN = 0x20;
        internal const int VK_CONTROL = 0x11, VK_B = 0x42, VK_C = 0x43;
        internal const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        internal const int SW_SHOW = 5;
        internal const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_SHOWWINDOW = 0x0040;
        internal const uint WDA_NONE = 0x00, WDA_MONITOR = 0x01, WDA_EXCLUDEFROMCAPTURE = 0x11;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    }
}
