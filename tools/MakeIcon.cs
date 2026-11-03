// Writes assets\app.ico and assets\logo.png. Picture source "ai" (default) scales the AI master PNGs;
// "drawn" draws the original coin-stack logo (16–256 px icon, 512 px logo.png), kept so we can switch back.
// Build & run:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

static class MakeIcon
{
    static readonly Color Red = Color.FromArgb(150, 30, 35);       // app accent (dark red)

    static Color Shade(Color c, double f)
    {
        Func<int, int> s = v => Math.Max(0, Math.Min(255, (int)(f >= 1 ? v + (255 - v) * (f - 1) : v * f)));
        return Color.FromArgb(c.A, s(c.R), s(c.G), s(c.B));
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void Prep(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
    }

    static Bitmap Draw(int size)
    {
        float S = size / 256f;                     // design on a 256 grid
        // centre the group (head + cube + coins) in the tile from its measured pixel bounds:
        // draw it alone on a bigger canvas, find the visible pixels, shift by the difference
        float dx, dy;
        using (var probe = new Bitmap(size * 2, size * 2, PixelFormat.Format32bppArgb))
        {
            using (var pg = Graphics.FromImage(probe))
            {
                Prep(pg);
                pg.TranslateTransform(size / 2f, size / 2f);
                DrawGroup(pg, size);
            }
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            var data = probe.LockBits(new Rectangle(0, 0, probe.Width, probe.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[probe.Width * probe.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            probe.UnlockBits(data);
            for (int y = 0; y < probe.Height; y++)
                for (int x = 0; x < probe.Width; x++)
                    if (((px[y * probe.Width + x] >> 24) & 0xFF) > 24)
                    {
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
            dx = size - (x0 + x1 + 1) / 2f;       // canvas centre (size) minus group centre
            dy = size - (y0 + y1 + 1) / 2f;
            if (size <= 32) { dx = (float)Math.Round(dx); dy = (float)Math.Round(dy); }   // whole pixels stay crisp
        }

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            Prep(g);
            // --- background tile ---
            var tile = new RectangleF(8 * S, 8 * S, 240 * S, 240 * S);
            using (var path = RoundRect(tile, 44 * S))
            {
                using (var bg = new LinearGradientBrush(tile, Color.FromArgb(52, 52, 58), Color.FromArgb(24, 24, 27), 90f))
                    g.FillPath(bg, path);
                using (var pen = new Pen(Color.FromArgb(90, 90, 96), Math.Max(1f, 3 * S)))
                    g.DrawPath(pen, path);
            }
            g.TranslateTransform(dx, dy);
            DrawGroup(g, size);
        }
        return bmp;
    }

    /// Cube, print head and coin stack, in 256-grid design coordinates scaled to size.
    static void DrawGroup(Graphics g, int size)
    {
        {
            float S = size / 256f;
            bool small = size <= 32;
            // --- isometric cube (the print) ---
            float cx = (small ? 128 : 116) * S, top = (small ? 92 : 96) * S, w = (small ? 88 : 74) * S, h = w * 0.58f, depth = (small ? 60 : 54) * S;
            var T = new PointF(cx, top);                       // back corner of top face
            var L = new PointF(cx - w, top + h);
            var R = new PointF(cx + w, top + h);
            var F = new PointF(cx, top + 2 * h);               // front corner of top face
            var Lb = new PointF(L.X, L.Y + depth);
            var Rb = new PointF(R.X, R.Y + depth);
            var Fb = new PointF(F.X, F.Y + depth);

            var topFace = new[] { T, R, F, L };
            var leftFace = new[] { L, F, Fb, Lb };
            var rightFace = new[] { F, R, Rb, Fb };
            using (var b = new SolidBrush(Color.FromArgb(196, 58, 64))) g.FillPolygon(b, topFace);
            using (var b = new SolidBrush(Red)) g.FillPolygon(b, leftFace);
            using (var b = new SolidBrush(Color.FromArgb(104, 20, 24))) g.FillPolygon(b, rightFace);

            // print layers on the sides
            int layers = small ? 3 : 7;
            using (var lp = new Pen(Color.FromArgb(small ? 90 : 70, 0, 0, 0), Math.Max(1f, 2.2f * S)))
            {
                for (int i = 1; i <= layers; i++)
                {
                    float t = depth * i / (layers + 1);
                    g.DrawLine(lp, L.X, L.Y + t, F.X, F.Y + t);
                    g.DrawLine(lp, F.X, F.Y + t, R.X, R.Y + t);
                }
            }
            // crisp edges
            using (var ep = new Pen(Color.FromArgb(225, 110, 112), Math.Max(1f, 2.5f * S)) { LineJoin = LineJoin.Round })
            {
                g.DrawLine(ep, L, F);
                g.DrawLine(ep, F, R);
                g.DrawLine(ep, F, Fb);
            }

            // --- print head sitting on the cube: fan housing, heater block, short brass nozzle ---
            // built upwards from the nozzle tip, which sits on the middle of the back-right edge of the top face
            float k = small ? 1.25f : 1f;                      // chunkier at tiny sizes
            float tipY = top + h * 0.5f - 1 * S;
            float coneH = 11 * S * k, hexH = 9 * S * k, blockH = 17 * S * k, bodyH = 50 * S * k;
            float hexY = tipY - coneH, blockY = hexY - hexH, bodyY = blockY - blockH, bodyTop = bodyY - bodyH;
            float bodyW = 76 * S * k, blockW = 44 * S * k, hexW = 24 * S * k, tipW = 7 * S * k;

            // freshly laid filament along the edge (right corner -> nozzle), drawn first so the nozzle sits on top of it
            if (!small)
                using (var trail = new Pen(Color.FromArgb(255, 200, 110), 7 * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(trail, R.X - 4 * S, R.Y - 2 * S, cx + w * 0.5f, tipY);
            cx += w * 0.5f;                                     // the head is drawn around the nozzle

            // fan housing / print head body
            var body = new RectangleF(cx - bodyW / 2, bodyTop, bodyW, bodyH);
            using (var bp = RoundRect(body, 9 * S * k))
            {
                using (var bb = new LinearGradientBrush(body, Color.FromArgb(210, 212, 218), Color.FromArgb(120, 122, 130), 0f))
                    g.FillPath(bb, bp);
                using (var edge = new Pen(Color.FromArgb(70, 70, 76), Math.Max(1f, 2 * S))) g.DrawPath(edge, bp);
            }
            // cooling fan on the front
            float fr = bodyH * 0.36f, fcx = cx, fcy = bodyTop + bodyH / 2;
            using (var fb = new SolidBrush(Color.FromArgb(38, 38, 42))) g.FillEllipse(fb, fcx - fr, fcy - fr, 2 * fr, 2 * fr);
            if (!small)
            {
                using (var blade = new Pen(Color.FromArgb(150, 150, 158), 3 * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 4; i++)
                    {
                        double a = i * Math.PI / 2 + 0.5;
                        g.DrawArc(blade, fcx - fr * 0.7f, fcy - fr * 0.7f, fr * 1.4f, fr * 1.4f, (float)(a * 180 / Math.PI), 55);
                    }
                using (var hub = new SolidBrush(Color.FromArgb(170, 170, 178)))
                    g.FillEllipse(hub, fcx - fr * 0.28f, fcy - fr * 0.28f, fr * 0.56f, fr * 0.56f);
            }

            // aluminium heater block
            var block = new RectangleF(cx - blockW / 2, blockY - blockH, blockW, blockH);
            block.Y = bodyY;
            using (var hb = new LinearGradientBrush(block, Color.FromArgb(200, 204, 212), Color.FromArgb(140, 144, 152), 90f))
                g.FillRectangle(hb, block);
            using (var edge = new Pen(Color.FromArgb(80, 80, 86), Math.Max(1f, 1.5f * S))) g.DrawRectangle(edge, block.X, block.Y, block.Width, block.Height);

            // brass nozzle: hex nut + short cone
            var brass = Color.FromArgb(214, 160, 70);
            using (var nb = new SolidBrush(brass))
            {
                g.FillRectangle(nb, cx - hexW / 2, hexY - hexH, hexW, hexH);
                g.FillPolygon(nb, new[] {
                    new PointF(cx - hexW * 0.42f, hexY), new PointF(cx + hexW * 0.42f, hexY),
                    new PointF(cx + tipW / 2, tipY), new PointF(cx - tipW / 2, tipY) });
            }
            using (var shade = new SolidBrush(Color.FromArgb(150, 105, 30)))
                g.FillRectangle(shade, cx + hexW * 0.18f, hexY - hexH, hexW * 0.32f, hexH);   // hex facet

            // --- price: a stack of plain gold coins (no currency sign), at every size ---
            if (small) CoinStack(g, S, size, 50 * S, 192 * S, 204 * S, size <= 16 ? 2 : 3);
            else CoinStack(g, S, size, 42 * S, 194 * S, 194 * S, 3);
        }
    }

    /// Gold coins lying in a slightly uneven stack; r = coin radius, (ccx, ccy) = centre of the stack.
    static void CoinStack(Graphics g, float S, int size, float r, float ccx, float ccy, int n)
    {
        float rw = r * 1.1f, eh = rw * 0.55f, thick = Math.Max(size <= 32 ? 2f : 1f, r * 0.32f);
        float baseY = ccy + r * 0.55f;                          // centre of the bottom coin's lower face
        var dark = Color.FromArgb(150, 100, 20);
        using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillEllipse(sh, ccx - rw + 3 * S, baseY - eh / 2 + 4 * S, 2 * rw, eh);
        for (int i = 0; i < n; i++)
        {
            float off = (i == 1 ? -0.12f : i == 2 ? 0.06f : 0f) * rw;
            float yb = baseY - i * thick, yt = yb - thick, x = ccx + off;
            using (var side = new SolidBrush(Color.FromArgb(190, 135, 35)))
            {
                g.FillEllipse(side, x - rw, yb - eh / 2, 2 * rw, eh);
                g.FillRectangle(side, x - rw, yt, 2 * rw, thick);
            }
            using (var rim = new Pen(dark, Math.Max(1f, 2.5f * S)))
            {
                g.DrawArc(rim, x - rw, yb - eh / 2, 2 * rw, eh, 0, 180);
                g.DrawLine(rim, x - rw, yt, x - rw, yb);
                g.DrawLine(rim, x + rw, yt, x + rw, yb);
            }
            if (size >= 48)                                     // reeded edge
                using (var reed = new Pen(Color.FromArgb(120, 110, 70, 10), 1.5f * S))
                    for (int j = 1; j < 12; j++)
                    {
                        double a = Math.PI * j / 12;
                        float px = x - rw * (float)Math.Cos(a), py = (float)Math.Sin(a) * eh / 2;
                        g.DrawLine(reed, px, yt + py, px, yb + py);
                    }
            var face = new RectangleF(x - rw, yt - eh / 2, 2 * rw, eh);
            using (var fb = new LinearGradientBrush(face, Color.FromArgb(255, 226, 130), Color.FromArgb(215, 160, 50), 20f))
                g.FillEllipse(fb, face);
            using (var rim = new Pen(dark, Math.Max(1f, 2.5f * S))) g.DrawEllipse(rim, face);
            if (i == n - 1 && size >= 48)
                using (var inner = new Pen(Color.FromArgb(140, 255, 245, 200), 2 * S))
                    g.DrawEllipse(inner, face.X + 9 * S, face.Y + 5 * S, face.Width - 18 * S, face.Height - 10 * S);
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    /// Classic ICO bitmap entry (32-bit BGRA + empty AND mask) – readable by every Windows API.
    static byte[] Dib(Bitmap b)
    {
        int n = b.Width;
        int maskStride = ((n + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(n); w.Write(n * 2);          // BITMAPINFOHEADER, height doubled (XOR + AND)
            w.Write((short)1); w.Write((short)32); w.Write(0);
            w.Write(n * n * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = n - 1; y >= 0; y--)                    // bottom-up rows
                for (int x = 0; x < n; x++)
                {
                    var c = b.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            w.Write(new byte[maskStride * n]);                  // AND mask unused with alpha
            return ms.ToArray();
        }
    }

    // ---- Picture source "ai": icon sizes scaled from assets\logo-master.png (64 px and up)
    // and assets\logo-master-small.png (zoomed crop, 48 px and down) ----

    static Bitmap Scale1(Image img, int n)
    {
        var b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        using (var ia = new ImageAttributes())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(img, new Rectangle(0, 0, n, n), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
        }
        return b;
    }

    /// Halves in steps while more than 4x too big (smoother small sizes), then scales to n.
    static Bitmap ScaleDown(Bitmap master, int n)
    {
        Bitmap cur = master;
        while (cur.Width >= 4 * n)
        {
            var half = Scale1(cur, (int)Math.Round(cur.Width / 2.0));
            if (cur != master) cur.Dispose();
            cur = half;
        }
        var result = Scale1(cur, n);
        if (cur != master) cur.Dispose();
        return result;
    }

    /// Light sharpening of opaque pixels (used for 32 px and below).
    static Bitmap Sharpen(Bitmap b, double a)
    {
        int n = b.Width;
        var o = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color c = b.GetPixel(x, y);
                if (c.A < 250 || x == 0 || y == 0 || x == n - 1 || y == n - 1) { o.SetPixel(x, y, c); continue; }
                Color l = b.GetPixel(x - 1, y), r = b.GetPixel(x + 1, y), u = b.GetPixel(x, y - 1), d = b.GetPixel(x, y + 1);
                Func<int, int, int, int, int, int> f = (v, v1, v2, v3, v4) =>
                    Math.Max(0, Math.Min(255, (int)Math.Round((1 + 4 * a) * v - a * (v1 + v2 + v3 + v4))));
                o.SetPixel(x, y, Color.FromArgb(c.A, f(c.R, l.R, r.R, u.R, d.R), f(c.G, l.G, r.G, u.G, d.G), f(c.B, l.B, r.B, u.B, d.B)));
            }
        return o;
    }

    /// 1 px light rim just inside the rounded square (2 px at 256), so the dark tile shows on dark backgrounds.
    static void DrawOutline(Bitmap b, Color colour)
    {
        int n = b.Width;
        float w = Math.Max(1f, n / 128f);
        float h = w / 2 + 0.02f * n / 32;
        using (var g = Graphics.FromImage(b))
        using (var pen = new Pen(Color.FromArgb(230, colour), w))   // 90 % opacity
        using (var path = RoundRect(new RectangleF(h, h, n - 2 * h, n - 2 * h), 0.222f * n - h))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawPath(pen, path);
        }
    }

    static Bitmap FromMaster(Bitmap full, Bitmap small, int size, Color? outline)
    {
        var b = ScaleDown(size <= 48 ? small : full, size);
        if (size <= 32) { var s = Sharpen(b, 0.12); b.Dispose(); b = s; }
        if (outline.HasValue) DrawOutline(b, outline.Value);
        return b;
    }

    /// "none" (or empty) = no outline, otherwise "R,G,B".
    static Color? ParseOutline(string s)
    {
        if (string.IsNullOrEmpty(s) || s.ToLowerInvariant() == "none") return null;
        string[] p = s.Split(',');
        if (p.Length != 3) throw new ArgumentException("Outline must be 'none' or 'R,G,B', not '" + s + "'");
        return Color.FromArgb(int.Parse(p[0].Trim()), int.Parse(p[1].Trim()), int.Parse(p[2].Trim()));
    }

    /// Writes an ICO: classic bitmap entries below 64 px, PNG from 64 px up (standard since Windows Vista; much smaller).
    static void WriteIco(string path, int[] sizes, Func<int, Bitmap> make)
    {
        var images = new List<byte[]>();
        foreach (int s in sizes) using (var b = make(s)) images.Add(s >= 64 ? Png(b) : Dib(b));
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    /// Usage: MakeIcon <outDir> [ai|drawn] [none|R,G,B]
    /// ai (default): icon 16-128 px + logo.png 256 px from the master PNGs in outDir, with an optional outline colour.
    /// drawn: the original drawn logo, exactly as in v1.2.2 (icon 16-256 px, logo.png 512 px; outline ignored).
    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "assets";
        string source = args.Length > 1 ? args[1].ToLowerInvariant() : "ai";
        Color? outline = ParseOutline(args.Length > 2 ? args[2] : "none");
        Directory.CreateDirectory(outDir);
        if (source == "ai")
        {
            using (var full = new Bitmap(Path.Combine(outDir, "logo-master.png")))
            using (var small = new Bitmap(Path.Combine(outDir, "logo-master-small.png")))
            {
                WriteIco(Path.Combine(outDir, "app.ico"), new[] { 16, 20, 24, 32, 40, 48, 64, 128 },
                         s => FromMaster(full, small, s, outline));
                using (var logo = FromMaster(full, small, 256, outline))
                    logo.Save(Path.Combine(outDir, "logo.png"), ImageFormat.Png);
            }
            Console.WriteLine("Wrote " + Path.Combine(outDir, "app.ico") + " and logo.png from the AI master" + (outline.HasValue ? " (outline " + args[2] + ")" : ""));
            return;
        }
        if (source != "drawn") throw new ArgumentException("Picture source must be 'ai' or 'drawn', not '" + source + "'");
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes) using (var b = Draw(s)) images.Add(s >= 64 ? Png(b) : Dib(b));

        // ICO container: bitmap entries for small sizes, PNG from 64 px up (standard since Windows Vista; much smaller)
        using (var fs = new FileStream(Path.Combine(outDir, "app.ico"), FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
        using (var big = Draw(512)) big.Save(Path.Combine(outDir, "logo.png"), ImageFormat.Png);
        Console.WriteLine("Wrote " + Path.Combine(outDir, "app.ico") + " and logo.png");
    }
}
