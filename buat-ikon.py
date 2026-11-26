#!/usr/bin/env python3
"""
Membuat ikon aplikasi CBT Ujian dari logo sekolah.

Cukup ganti berkas  logo.png  di folder ini (boleh juga logo.jpg / logo.jpeg / logo.webp).
Saat aplikasi dibangun lewat GitHub (bangun.yml), skrip ini dijalankan otomatis dan membuat:
  - android/app/src/main/res/drawable-*dpi/ikon_sekolah.png   (ikon .apk, latar putih)
  - windows/ikon.ico                                          (ikon .exe, latar transparan)

Logo berlatar putih (tanpa transparansi) dibersihkan otomatis: putih di LUAR logo dibuat transparan,
putih di dalam logo tetap. Logo yang sudah transparan dipakai apa adanya.

Menjalankan sendiri di komputer (tidak wajib):
    pip install pillow numpy
    python buat-ikon.py
"""
import io
import os
import struct
import sys
from collections import deque

try:
    import numpy as np
    from PIL import Image, ImageFilter
except ImportError:
    sys.exit('Pustaka belum terpasang. Jalankan: pip install pillow numpy')

try:   # konsol Windows (cp1252) tidak mengenal huruf seperti → ; jangan sampai pembangunan gagal karenanya
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

AKAR = os.path.dirname(os.path.abspath(__file__))
CALON = ['logo.png', 'logo.jpg', 'logo.jpeg', 'logo.webp', 'Logo.png', 'Logo.jpg', 'LOGO.png', 'LOGO.jpg']
RES = os.path.join(AKAR, 'android', 'app', 'src', 'main', 'res')
ICO = os.path.join(AKAR, 'windows', 'ikon.ico')
DENSITAS = {'mdpi': 108, 'hdpi': 162, 'xhdpi': 216, 'xxhdpi': 324, 'xxxhdpi': 432}   # kanvas ikon adaptif 108dp
UKURAN_ICO = [16, 24, 32, 48, 64, 128, 256]


def cari_logo():
    for n in CALON:
        p = os.path.join(AKAR, n)
        if os.path.isfile(p):
            return p
    return None


def bersihkan_latar(im):
    """RGBA: putih yang tersambung ke tepi gambar → transparan, tepinya dihaluskan."""
    im = im.convert('RGBA')
    a = np.asarray(im).astype(np.int32)
    if a[:, :, 3].min() < 250:            # sudah punya transparansi → pakai apa adanya
        return im, False
    H, W = a.shape[:2]
    terang = a[:, :, :3].min(axis=2) >= 200
    latar = np.zeros((H, W), bool)
    q = deque()
    for x in range(W):
        for y in (0, H - 1):
            if terang[y, x] and not latar[y, x]:
                latar[y, x] = True; q.append((y, x))
    for y in range(H):
        for x in (0, W - 1):
            if terang[y, x] and not latar[y, x]:
                latar[y, x] = True; q.append((y, x))
    while q:
        y, x = q.popleft()
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < H and 0 <= nx < W and terang[ny, nx] and not latar[ny, nx]:
                latar[ny, nx] = True; q.append((ny, nx))
    if not latar.any():                   # tidak ada latar putih di tepi
        return im, False
    # pita tepi (3 px): campuran logo + putih → "warna ke alfa" agar tidak ada lingkaran putih kasar
    pita = np.asarray(Image.fromarray((latar * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))) > 0
    pita &= ~latar
    alfa = np.full((H, W), 255.0)
    alfa[latar] = 0
    rgb = a[:, :, :3].astype(float)
    k = rgb[pita]
    al = np.max((255 - k) / 255.0, axis=1).clip(1e-3, 1)
    alfa[pita] = al * 255
    rgb[pita] = ((k - 255 * (1 - al[:, None])) / al[:, None]).clip(0, 255)
    return Image.fromarray(np.dstack([rgb, alfa]).astype(np.uint8), 'RGBA'), True


def siapkan(path):
    im = Image.open(path)
    im.load()
    if max(im.size) > 1200:               # cukup untuk ikon 512 px, dan pembersihan latar lebih cepat
        k = 1200 / max(im.size)
        im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)
    im, dibersihkan = bersihkan_latar(im)
    kotak = im.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
    if not kotak:
        sys.exit('Logo kosong / seluruhnya transparan: ' + path)
    return im.crop(kotak), dibersihkan


def ikon_android(logo):
    W, H = logo.size
    A = np.asarray(logo.getchannel('A')) > 40
    ys, xs = np.nonzero(A)
    jauh = float(np.sqrt((xs - (W - 1) / 2) ** 2 + (ys - (H - 1) / 2) ** 2).max()) or 1.0
    for d, n in DENSITAS.items():
        s = (n * 33 / 108 * 0.97) / jauh     # seluruh logo di dalam lingkaran aman 66dp
        w, h = max(1, round(W * s)), max(1, round(H * s))
        c = Image.new('RGBA', (n, n), (0, 0, 0, 0))
        c.alpha_composite(logo.resize((w, h), Image.LANCZOS), ((n - w) // 2, (n - h) // 2))
        folder = os.path.join(RES, 'drawable-' + d)
        os.makedirs(folder, exist_ok=True)
        c.save(os.path.join(folder, 'ikon_sekolah.png'), optimize=True)
    print('  Android: drawable-mdpi … drawable-xxxhdpi/ikon_sekolah.png')


def _dib(img):
    """bingkai ICO bergaya BMP (paling cocok untuk ukuran kecil di semua versi Windows)"""
    n = img.size[0]
    px = np.asarray(img).astype(np.uint8)
    bgra = px[::-1][:, :, [2, 1, 0, 3]].tobytes()
    baris = ((n + 31) // 32) * 4
    topeng = bytearray()
    for row in px[::-1]:
        b = bytearray(baris)
        for x in range(n):
            if row[x][3] == 0:
                b[x // 8] |= 0x80 >> (x % 8)
        topeng += b
    kepala = struct.pack('<IiiHHIIiiII', 40, n, n * 2, 1, 32, 0, len(bgra) + len(topeng), 0, 0, 0, 0)
    return kepala + bgra + bytes(topeng)


def ikon_windows(logo):
    W, H = logo.size
    data = []
    for n in UKURAN_ICO:
        k = n / max(W, H)
        w, h = max(1, round(W * k)), max(1, round(H * k))
        c = Image.new('RGBA', (n, n), (0, 0, 0, 0))
        c.alpha_composite(logo.resize((w, h), Image.LANCZOS), ((n - w) // 2, (n - h) // 2))
        if n <= 48:
            data.append(_dib(c))
        else:
            b = io.BytesIO(); c.save(b, 'PNG', optimize=True); data.append(b.getvalue())
    out = bytearray(struct.pack('<HHH', 0, 1, len(UKURAN_ICO)))
    off = 6 + 16 * len(UKURAN_ICO)
    for n, d in zip(UKURAN_ICO, data):
        out += struct.pack('<BBBBHHII', n % 256, n % 256, 0, 0, 1, 32, len(d), off)
        off += len(d)
    for d in data:
        out += d
    os.makedirs(os.path.dirname(ICO), exist_ok=True)
    with open(ICO, 'wb') as f:
        f.write(out)
    print('  Windows: windows/ikon.ico (16–256 px)')


def main():
    p = cari_logo()
    if not p:
        print('Tidak ada logo.png di folder ExamBrowser → ikon yang sudah ada dipakai apa adanya.')
        return
    logo, dibersihkan = siapkan(p)
    print('Logo: %s (%d×%d px%s)' % (os.path.basename(p), logo.width, logo.height,
                                     ', latar putih dibuat transparan' if dibersihkan else ''))
    ikon_android(logo)
    ikon_windows(logo)
    print('Selesai.')


if __name__ == '__main__':
    main()
