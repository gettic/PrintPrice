// Draws the program logo and writes assets\app.ico (16–256 px) plus assets\logo.png (512 px).
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

    static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            float S = size / 256f;                 // design on a 256 grid
            bool small = size <= 32;

            // --- background tile ---
            var tile = new RectangleF(8 * S, 8 * S, 240 * S, 240 * S);
            using (var path = RoundRect(tile, 44 * S))
            {
                using (var bg = new LinearGradientBrush(tile, Color.FromArgb(52, 52, 58), Color.FromArgb(24, 24, 27), 90f))
                    g.FillPath(bg, path);
                using (var pen = new Pen(Color.FromArgb(90, 90, 96), Math.Max(1f, 3 * S)))
                    g.DrawPath(pen, path);
            }

            // centre the group (head + cube + coin) in the tile, offsets from its measured bounding box
            g.TranslateTransform((small ? 0f : -11.75f) * S, (small ? -15.25f : -16.25f) * S);
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
            // built upwards from the nozzle tip, which touches the centre of the top face
            float k = small ? 1.25f : 1f;                      // chunkier at tiny sizes
            float tipY = top + h - 1 * S;
            float coneH = 11 * S * k, hexH = 9 * S * k, blockH = 17 * S * k, bodyH = 50 * S * k;
            float hexY = tipY - coneH, blockY = hexY - hexH, bodyY = blockY - blockH, bodyTop = bodyY - bodyH;
            float bodyW = 76 * S * k, blockW = 44 * S * k, hexW = 24 * S * k, tipW = 7 * S * k;

            // freshly laid filament on the top face, drawn first so the nozzle sits on top of it
            if (!small)
                using (var trail = new Pen(Color.FromArgb(255, 200, 110), 7 * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(trail, cx - w * 0.5f, top + h + h * 0.5f - 2 * S, cx, tipY);

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

            // --- price coin "kr" (skipped at tiny sizes) ---
            if (size >= 48)
            {
                float r = 42 * S, ccx = 194 * S, ccy = 194 * S;
                var coin = new RectangleF(ccx - r, ccy - r, 2 * r, 2 * r);
                using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillEllipse(sh, coin.X + 3 * S, coin.Y + 4 * S, coin.Width, coin.Height);
                using (var cbg = new LinearGradientBrush(coin, Color.FromArgb(255, 222, 120), Color.FromArgb(205, 150, 40), 60f))
                    g.FillEllipse(cbg, coin);
                using (var rim = new Pen(Color.FromArgb(150, 100, 20), Math.Max(1f, 4 * S))) g.DrawEllipse(rim, coin);
                using (var inner = new Pen(Color.FromArgb(120, 255, 245, 200), Math.Max(1f, 2 * S)))
                    g.DrawEllipse(inner, coin.X + 9 * S, coin.Y + 9 * S, coin.Width - 18 * S, coin.Height - 18 * S);
                using (var font = new Font("Segoe UI", 36 * S, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(Color.FromArgb(110, 60, 0)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("kr", font, tb, new RectangleF(coin.X, coin.Y - 2 * S, coin.Width, coin.Height), sf);
            }
        }
        return bmp;
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

    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "assets";
        Directory.CreateDirectory(outDir);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes) using (var b = Draw(s)) images.Add(s >= 256 ? Png(b) : Dib(b));

        // ICO container: bitmap entries for small sizes, PNG for 256 px (standard since Windows Vista)
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
