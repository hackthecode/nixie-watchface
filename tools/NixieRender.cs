// Photoreal-ish renderer for the Nixie watch face bitmaps.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

public static class NixieRender
{
    public static string DigitFont = "Bahnschrift Light Condensed";
    public static string LabelFont = "Bahnschrift SemiBold Condensed";
    const int S = 466;

    // Scene geometry (shared with the WFF XML generator)
    public static readonly float[] BigX = { 25, 123, 239, 337 };
    public const float BigY = 86, BigK = 1.24f;
    // old tubes never match: relative brightness of each one (also used for the digit alpha in the XML)
    public static readonly float[] Bright = { 1.00f, 0.92f, 0.97f, 0.88f };
    public const int BarW = 120, BarH = 14, BarX = 233 - BarW / 2, BarY = 382;
    public static readonly float[] ColonY = { 196, 254 };
    const float PlinthTop = BigY + 212 * BigK, PlinthFront = PlinthTop + 16;

    // ------------------------------------------------------------ float masks
    static float[] Read(Bitmap bmp, int ch)
    {
        int w = bmp.Width, h = bmp.Height;
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4]; Marshal.Copy(d.Scan0, buf, 0, buf.Length); bmp.UnlockBits(d);
        var m = new float[w * h]; for (int i = 0; i < w * h; i++) m[i] = buf[i * 4 + ch] / 255f; return m;
    }
    static float[] MaskOf(int w, int h, Action<Graphics> draw)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Black);
            draw(g); g.Flush(); return Read(bmp, 2);
        }
    }
    static float At(float[] s, int w, int h, int x, int y) { return (x < 0 || y < 0 || x >= w || y >= h) ? 0f : s[y * w + x]; }
    public static float[] Blur(float[] src, int w, int h, int r)
    {
        var a = (float[])src.Clone(); if (r < 1) return a;
        var b = new float[w * h]; float inv = 1f / (2 * r + 1);
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < h; y++) { float acc = 0; for (int x = -r; x <= r; x++) acc += At(a, w, h, x, y);
                for (int x = 0; x < w; x++) { b[y * w + x] = acc * inv; acc += At(a, w, h, x + r + 1, y) - At(a, w, h, x - r, y); } }
            for (int x = 0; x < w; x++) { float acc = 0; for (int y = -r; y <= r; y++) acc += At(b, w, h, x, y);
                for (int y = 0; y < h; y++) { a[y * w + x] = acc * inv; acc += At(b, w, h, x, y + r + 1) - At(b, w, h, x, y - r); } }
        }
        return a;
    }
    public static float[] Erode(float[] s, int w, int h, int r)
    {
        var t = new float[w * h]; var o = new float[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float m = 1; for (int k = -r; k <= r; k++) m = Math.Min(m, At(s, w, h, x + k, y)); t[y * w + x] = m; }
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float m = 1; for (int k = -r; k <= r; k++) m = Math.Min(m, At(t, w, h, x, y + k)); o[y * w + x] = m; }
        return o;
    }

    // ------------------------------------------------------------ glow -> transparent PNG
    // layers: radius, r, g, b, gain ; output is "light" encoded as colour + alpha for normal blending on dark glass
    static Bitmap GlowBitmap(int w, int h, float[] wire, float scale, float edgeFade, float[] extra = null)
    {
        float[][] L = {
            new float[]{0, 1.00f,0.86f,0.58f, 1.00f},
            new float[]{1.5f, 1.00f,0.46f,0.08f, 1.10f},
            new float[]{4, 1.00f,0.35f,0.03f, 0.78f},
            new float[]{11, 1.00f,0.28f,0.02f, 0.72f},
            new float[]{26, 0.95f,0.22f,0.01f, 0.44f},
            new float[]{7, 0.30f,0.16f,0.80f, 0.10f},   // faint violet haze of the discharge
        };
        var R = new float[w * h]; var G = new float[w * h]; var B = new float[w * h];
        foreach (var l in L)
        {
            int rad = (int)Math.Round(l[0] * scale); var m = rad < 1 ? wire : Blur(wire, w, h, rad);
            for (int i = 0; i < w * h; i++) { float v = m[i] * l[4]; R[i] += v * l[1]; G[i] += v * l[2]; B[i] += v * l[3]; }
        }
        if (extra != null) for (int i = 0; i < w * h; i++) { R[i] += extra[i]; G[i] += extra[i] * 0.42f; B[i] += extra[i] * 0.08f; }
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            float f = 1;
            if (edgeFade > 0) { float ex = Math.Min(x, w - 1 - x) / edgeFade, ey = Math.Min(y, h - 1 - y) / edgeFade; f = Smooth(Math.Min(1, Math.Min(ex, ey))); }
            float r = Math.Min(1, R[i]) * f, g = Math.Min(1, G[i]) * f, b = Math.Min(1, B[i]) * f;
            float a = Math.Max(r, Math.Max(g, b));
            if (a > 0.002f) { buf[i * 4 + 2] = (byte)(255 * r / a); buf[i * 4 + 1] = (byte)(255 * g / a); buf[i * 4 + 0] = (byte)(255 * b / a); buf[i * 4 + 3] = (byte)(255 * a); }
        }
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(buf, 0, d.Scan0, buf.Length); bmp.UnlockBits(d); return bmp;
    }
    static float Smooth(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

    // ------------------------------------------------------------ digit geometry
    static RectangleF refBounds = RectangleF.Empty;
    public static GraphicsPath Digit(char c, float cx, float cy, float height, float sx)
    {
        using (var ff = new FontFamily(DigitFont))
        {
            if (refBounds.IsEmpty) { var r = new GraphicsPath(); r.AddString("0", ff, 0, 200f, new PointF(0, 0), StringFormat.GenericTypographic); refBounds = r.GetBounds(); }
            var p = new GraphicsPath(); p.AddString(c.ToString(), ff, 0, 200f, new PointF(0, 0), StringFormat.GenericTypographic);
            var b = p.GetBounds(); float s = height / refBounds.Height;
            var m = new Matrix();
            m.Translate(cx, cy, MatrixOrder.Append);
            m.Scale(s * sx, s, MatrixOrder.Prepend);
            m.Translate(-(b.X + b.Width / 2), -(refBounds.Y + refBounds.Height / 2), MatrixOrder.Prepend);
            p.Transform(m); return p;
        }
    }

    // Cathodes sit one behind another: index 0 is the rearmost, 9 the frontmost. Front ones are larger and shifted.
    public const string Order = "1608295374";
    static GraphicsPath Cathode(char c, float ox, float oy, float k, float jx = 0, float jy = 0)
    {
        float depth = Order.IndexOf(c) / 9f;
        return Digit(c, ox + (42 + (depth - 0.5f) * 3.0f + jx) * k, oy + (112 - (depth - 0.5f) * 1.6f + jy) * k, (104 + 9 * depth) * k, 1f);
    }

    // ------------------------------------------------------------ lit digit sprite (box-sized)
    public static Bitmap LitDigit(char c, float k)
    {
        int w = (int)Math.Ceiling(84 * k), h = (int)Math.Ceiling(196 * k);
        var mask = MaskOf(w, h, g => g.FillPath(Brushes.White, Cathode(c, 0, 0, k)));
        var wire = k > 0.7f ? Erode(mask, w, h, 3) : Erode(mask, w, h, 1);
        wire = Blur(wire, w, h, 1);
        for (int i = 0; i < wire.Length; i++) wire[i] = Math.Min(1, wire[i] * 2.2f);
        // unlit cathodes in front of this one cross the glow as thin dark wires
        int me = Order.IndexOf(c);
        var front = MaskOf(w, h, g => { using (var pen = new Pen(Color.White, 0.8f * k)) { pen.LineJoin = LineJoin.Round; for (int j = me + 1; j < Order.Length; j++) g.DrawPath(pen, Cathode(Order[j], 0, 0, k)); } });
        for (int i = 0; i < wire.Length; i++) wire[i] *= 1 - 0.30f * front[i];
        // reflections of the lit cathode in the curved glass walls: squeezed, mirrored copies near each side
        var refl = new float[w * h];
        float[] wallX = { 13f * k, 71f * k }; float half = 4.5f * k, squeeze = 6.5f;
        for (int y = 0; y < h; y++) foreach (var xr in wallX)
            for (int x = (int)(xr - half); x <= (int)(xr + half); x++)
            {
                if (x < 0 || x >= w) continue;
                float t = (x - xr) / half; int sx = (int)Math.Round(42 * k - (x - xr) * squeeze);
                refl[y * w + x] += At(wire, w, h, sx, y) * (1 - t * t);
            }
        refl = Blur(refl, w, h, 2);
        for (int i = 0; i < refl.Length; i++) refl[i] *= 0.55f;
        return GlowBitmap(w, h, wire, k, 10 * k, refl);
    }

    // always-on sprite: only the thin glowing wire, no halo -> very few lit OLED pixels
    public static Bitmap AmbientDigit(char c, float k)
    {
        int w = (int)Math.Ceiling(84 * k), h = (int)Math.Ceiling(196 * k);
        var mask = MaskOf(w, h, g => g.FillPath(Brushes.White, Cathode(c, 0, 0, k)));
        var inner = Erode(mask, w, h, 4);
        var wire = new float[w * h];
        for (int i = 0; i < wire.Length; i++) wire[i] = Math.Max(0, mask[i] - inner[i]); // outline only
        wire = Blur(wire, w, h, 1);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            float a = Math.Min(1, wire[i] * 1.8f);
            if (a < 0.03f) continue;
            buf[i * 4 + 2] = 255; buf[i * 4 + 1] = 120; buf[i * 4 + 0] = 30; buf[i * 4 + 3] = (byte)(255 * a);
        }
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(buf, 0, d.Scan0, buf.Length); bmp.UnlockBits(d); return bmp;
    }

    public static Bitmap BulbLit()
    {
        int w = 34, h = 40;
        var m = MaskOf(w, h, g => g.FillEllipse(Brushes.White, 13.5f, 15f, 7f, 10f));
        return GlowBitmap(w, h, m, 0.6f, 6);
    }

    public static Bitmap BarLit()
    {
        var m = MaskOf(BarW, BarH, g => { using (var p = new Pen(Color.White, 2.6f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, 3, BarH / 2f, BarW - 3, BarH / 2f); } });
        return GlowBitmap(BarW, BarH, m, 0.35f, 0);
    }

    // ------------------------------------------------------------ noise
    static float Hash(int x, int y) { unchecked { int n = x * 374761393 + y * 668265263; n = (n ^ (n >> 13)) * 1274126177; return ((n ^ (n >> 16)) & 0xffff) / 65535f; } }
    static float Noise(float x, float y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }
    static float Fbm(float x, float y) { float s = 0, amp = 0.5f; for (int o = 0; o < 5; o++) { s += Noise(x, y) * amp; x *= 2.03f; y *= 2.03f; amp *= 0.5f; } return s; }

    // ------------------------------------------------------------ pixel helpers for opaque bg
    static void PutPixels(Bitmap bmp, Func<int, int, Color?> f)
    {
        int w = bmp.Width, h = bmp.Height;
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4]; Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { var c = f(x, y); if (c.HasValue) { int i = (y * w + x) * 4; var cc = c.Value;
            float a = cc.A / 255f; buf[i + 2] = (byte)(buf[i + 2] * (1 - a) + cc.R * a); buf[i + 1] = (byte)(buf[i + 1] * (1 - a) + cc.G * a); buf[i] = (byte)(buf[i] * (1 - a) + cc.B * a); buf[i + 3] = 255; } }
        Marshal.Copy(buf, 0, d.Scan0, buf.Length); bmp.UnlockBits(d);
    }
    static void AddLight(Bitmap bmp, float cx, float cy, float rx, float ry, float r, float g, float b, float strength)
    {
        PutPixelsAdd(bmp, (x, y) => { float dx = (x - cx) / rx, dy = (y - cy) / ry; float d = dx * dx + dy * dy; return d > 1 ? 0 : (float)Math.Pow(1 - d, 2) * strength; }, r, g, b);
    }
    static void PutPixelsAdd(Bitmap bmp, Func<int, int, float> f, float r, float g, float b)
    {
        int w = bmp.Width, h = bmp.Height;
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4]; Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float v = f(x, y); if (v <= 0) continue; int i = (y * w + x) * 4;
            buf[i + 2] = (byte)Math.Min(255, buf[i + 2] + r * v); buf[i + 1] = (byte)Math.Min(255, buf[i + 1] + g * v); buf[i] = (byte)Math.Min(255, buf[i] + b * v); }
        Marshal.Copy(buf, 0, d.Scan0, buf.Length); bmp.UnlockBits(d);
    }

    // ------------------------------------------------------------ tube shapes
    static GraphicsPath Glass(float ox, float oy, float k)
    {
        float l = ox + 4 * k, wd = 76 * k, top = oy + 4 * k, bot = oy + 188 * k, rr = 10 * k;
        var p = new GraphicsPath();
        p.AddArc(l, top, wd, wd, 180, 180);
        p.AddLine(l + wd, top + wd / 2, l + wd, bot - rr);
        p.AddArc(l + wd - 2 * rr, bot - 2 * rr, 2 * rr, 2 * rr, 0, 90);
        p.AddArc(l, bot - 2 * rr, 2 * rr, 2 * rr, 90, 90);
        p.CloseFigure(); return p;
    }
    static void Honeycomb(Graphics g, Pen pen, RectangleF r, float s)
    {
        float hw = s * 0.866f;
        for (int row = 0; ; row++)
        {
            float cy = r.Top + row * s * 1.5f; if (cy - s > r.Bottom) break;
            for (int col = -1; ; col++)
            {
                float cx = r.Left + col * 2 * hw + (row % 2 == 1 ? hw : 0); if (cx - hw > r.Right) break;
                var pts = new PointF[6]; for (int i = 0; i < 6; i++) { double a = Math.PI / 3 * i + Math.PI / 6; pts[i] = new PointF(cx + (float)Math.Cos(a) * s, cy + (float)Math.Sin(a) * s); }
                g.DrawPolygon(pen, pts);
            }
        }
    }

    static void TubeBack(Graphics g, float ox, float oy, float k, bool socket, int seed = 0)
    {
        var glass = Glass(ox, oy, k);
        var rnd = new Random(100 + seed * 17);
        float jx = (float)(rnd.NextDouble() - 0.5) * 1.4f, tone = 0.88f + (float)rnd.NextDouble() * 0.24f;
        if (socket)
        {
            var sr = new RectangleF(ox + 8 * k, oy + 184 * k, 68 * k, 34 * k);
            using (var lg = new LinearGradientBrush(sr, Color.FromArgb(255, 34, 33, 36), Color.FromArgb(255, 6, 6, 7), 0f))
            { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 10, 10, 12), Color.FromArgb(255, 48, 47, 52), Color.FromArgb(255, 22, 22, 25), Color.FromArgb(255, 8, 8, 9), Color.FromArgb(255, 4, 4, 5) }; cb.Positions = new[] { 0f, 0.25f, 0.5f, 0.8f, 1f }; lg.InterpolationColors = cb; g.FillRectangle(lg, sr); }
            var ring = new RectangleF(ox + 6 * k, oy + 182 * k, 72 * k, 6 * k);
            using (var lg = new LinearGradientBrush(ring, Color.White, Color.Black, 0f))
            { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 70, 70, 74), Color.FromArgb(255, 225, 225, 230), Color.FromArgb(255, 120, 120, 126), Color.FromArgb(255, 200, 200, 206), Color.FromArgb(255, 60, 60, 64) }; cb.Positions = new[] { 0f, 0.22f, 0.5f, 0.75f, 1f }; lg.InterpolationColors = cb; g.FillRectangle(lg, ring); }
        }
        // glass interior
        var gb = glass.GetBounds();
        using (var lg = new LinearGradientBrush(new RectangleF(gb.X - 1, gb.Y, gb.Width + 2, gb.Height), Color.Black, Color.Black, 0f))
        { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 8, 7, 7), Color.FromArgb(255, (int)(30 * tone), (int)(24 * tone), (int)(21 * tone)), Color.FromArgb(255, (int)(24 * tone), (int)(19 * tone), (int)(17 * tone)), Color.FromArgb(255, (int)(30 * tone), (int)(24 * tone), (int)(21 * tone)), Color.FromArgb(255, 7, 6, 6) }; cb.Positions = new[] { 0f, 0.3f, 0.5f, 0.7f, 1f }; lg.InterpolationColors = cb; g.FillPath(lg, glass); }
        var st = g.Save(); g.SetClip(glass);
        // back anode mesh
        using (var pen = new Pen(Color.FromArgb(46, 120, 105, 92), 0.55f)) Honeycomb(g, pen, new RectangleF(ox + 10 * k, oy + 44 * k, 64 * k, 136 * k), 2.3f * k);
        // getter: silvery-dark mirror flashed onto the inside of the dome
        using (var gp = new GraphicsPath())
        {
            var gr = new RectangleF(ox + 15 * k, oy + 5 * k, 54 * k, 30 * k); gp.AddEllipse(gr);
            using (var pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(150, 66, 68, 78); pg.SurroundColors = new[] { Color.FromArgb(0, 30, 30, 36) }; pg.CenterPoint = new PointF(gr.X + gr.Width * 0.42f, gr.Y + gr.Height * 0.5f); g.FillPath(pg, gp); }
            using (var pen = new Pen(Color.FromArgb(40, 190, 195, 215), 0.9f * k)) g.DrawArc(pen, gr.X + 6 * k, gr.Y + 5 * k, gr.Width - 12 * k, gr.Height - 6 * k, 200, 70);
        }
        // support rods + mica spacers
        using (var pen = new Pen(Color.FromArgb(150, 150, 140, 128), 1.4f * k)) { g.DrawLine(pen, ox + 11 * k, oy + 40 * k, ox + 11 * k, oy + 182 * k); g.DrawLine(pen, ox + 73 * k, oy + 40 * k, ox + 73 * k, oy + 182 * k); }
        using (var br = new SolidBrush(Color.FromArgb(60, 230, 225, 210))) { g.FillEllipse(br, ox + 8 * k, oy + 172 * k, 68 * k, 7 * k); g.FillEllipse(br, ox + 9 * k, oy + 40 * k, 66 * k, 6 * k); }
        // pins
        using (var pen = new Pen(Color.FromArgb(160, 170, 160, 150), 1f * k)) for (int i = 0; i < 9; i++) { float x = ox + (16 + i * 6.5f) * k; g.DrawLine(pen, x, oy + 178 * k, x, oy + 190 * k); }
        // leads fanning from the cathode stack down to the pins
        using (var pen = new Pen(Color.FromArgb(95, 165, 150, 135), 0.7f * k)) for (int i = 0; i < 9; i++) { float px = ox + (16 + i * 6.5f) * k; g.DrawBezier(pen, ox + (30 + i * 3f + jx) * k, oy + 166 * k, ox + (30 + i * 3f) * k, oy + 172 * k, px, oy + 172 * k, px, oy + 179 * k); }
        // unlit cathode stack (same geometry as the lit sprites)
        for (int i = 0; i < Order.Length; i++)
        {
            float depth = i / 9f;
            using (var pen = new Pen(Color.FromArgb((int)(55 + 85 * depth), 150, 120, 96), Math.Max(0.7f, 1.15f * k)))
            { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, Cathode(Order[i], ox, oy, k)); }
        }
        g.Restore(st);
    }

    static void TubeFront(Graphics g, float ox, float oy, float k, int seed = 0)
    {
        var glass = Glass(ox, oy, k);
        var st = g.Save(); g.SetClip(glass);
        var rnd = new Random(500 + seed * 31);
        // soft slanted reflection of the room
        using (var gp = new GraphicsPath())
        {
            gp.AddPolygon(new[] { new PointF(ox + 22 * k, oy + 10 * k), new PointF(ox + 40 * k, oy + 10 * k), new PointF(ox + 26 * k, oy + 186 * k), new PointF(ox + 12 * k, oy + 186 * k) });
            using (var br = new SolidBrush(Color.FromArgb(9, 235, 240, 255))) g.FillPath(br, gp);
        }
        // dust specks and hairline scratches
        for (int i = 0; i < 26; i++) { float x = ox + (8 + (float)rnd.NextDouble() * 68) * k, y = oy + (10 + (float)rnd.NextDouble() * 172) * k, r = 0.35f + (float)rnd.NextDouble() * 0.7f;
            using (var br = new SolidBrush(Color.FromArgb(rnd.Next(22, 70), 235, 235, 240))) g.FillEllipse(br, x, y, r * 2, r * 2); }
        for (int i = 0; i < 4; i++) { float x = ox + (10 + (float)rnd.NextDouble() * 60) * k, y = oy + (20 + (float)rnd.NextDouble() * 150) * k, len = (6 + (float)rnd.NextDouble() * 18) * k; double a = rnd.NextDouble() * Math.PI;
            using (var pen = new Pen(Color.FromArgb(rnd.Next(14, 34), 240, 240, 250), 0.6f)) g.DrawBezier(pen, x, y, x + (float)Math.Cos(a) * len * 0.3f + 1, y + (float)Math.Sin(a) * len * 0.3f, x + (float)Math.Cos(a) * len * 0.7f - 1, y + (float)Math.Sin(a) * len * 0.7f, x + (float)Math.Cos(a) * len, y + (float)Math.Sin(a) * len); }
        // front anode mesh
        using (var pen = new Pen(Color.FromArgb(20, 20, 17, 15), 0.5f)) Honeycomb(g, pen, new RectangleF(ox + 10 * k, oy + 44 * k, 64 * k, 136 * k), 2.3f * k);
        // vertical specular highlights
        var hl = new RectangleF(ox + 10 * k, oy + 20 * k, 9 * k, 160 * k);
        using (var lg = new LinearGradientBrush(hl, Color.Transparent, Color.Transparent, 0f))
        { var cb = new ColorBlend(3); cb.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(95, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }; cb.Positions = new[] { 0f, 0.45f, 1f }; lg.InterpolationColors = cb; g.FillRectangle(lg, hl); }
        var hl2 = new RectangleF(ox + 66 * k, oy + 30 * k, 5 * k, 140 * k);
        using (var lg = new LinearGradientBrush(hl2, Color.Transparent, Color.Transparent, 0f))
        { var cb = new ColorBlend(3); cb.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(45, 255, 240, 220), Color.FromArgb(0, 255, 255, 255) }; cb.Positions = new[] { 0f, 0.5f, 1f }; lg.InterpolationColors = cb; g.FillRectangle(lg, hl2); }
        // dome highlight
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 2.2f * k)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawArc(pen, ox + 12 * k, oy + 12 * k, 60 * k, 60 * k, 200, 50); }
        g.Restore(st);
        // glass edge
        using (var pen = new Pen(Color.FromArgb(70, 220, 225, 235), 1.1f * k)) g.DrawPath(pen, glass);
        // exhaust tip
        using (var br = new SolidBrush(Color.FromArgb(110, 210, 215, 225))) g.FillEllipse(br, ox + 38 * k, oy + 0.5f * k, 8 * k, 7 * k);
        using (var br = new SolidBrush(Color.FromArgb(180, 255, 255, 255))) g.FillEllipse(br, ox + 39.5f * k, oy + 1.5f * k, 2.5f * k, 2.5f * k);
    }

    static void Brass(Graphics g, RectangleF r, float radius)
    {
        var p = RoundRect(r, radius);
        using (var lg = new LinearGradientBrush(r, Color.Black, Color.Black, 90f))
        { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 226, 190, 112), Color.FromArgb(255, 176, 134, 62), Color.FromArgb(255, 205, 166, 90), Color.FromArgb(255, 150, 108, 46), Color.FromArgb(255, 120, 84, 34) }; cb.Positions = new[] { 0f, 0.3f, 0.55f, 0.85f, 1f }; lg.InterpolationColors = cb; g.FillPath(lg, p); }
        var st = g.Save(); g.SetClip(p);
        var rnd = new Random(3);
        for (int i = 0; i < (int)(r.Height * 3); i++) { float y = r.Top + (float)rnd.NextDouble() * r.Height; int a = rnd.Next(8, 30); bool light = rnd.Next(2) == 0;
            using (var pen = new Pen(light ? Color.FromArgb(a, 255, 240, 200) : Color.FromArgb(a, 60, 40, 10), 0.6f)) g.DrawLine(pen, r.Left, y, r.Right, y + (float)(rnd.NextDouble() - 0.5)); }
        g.Restore(st);
        using (var pen = new Pen(Color.FromArgb(160, 255, 236, 180), 1.2f)) g.DrawArc(pen, r.Left + 1, r.Top + 1, r.Width - 2, r.Height * 0.5f, 185, 170);
        using (var pen = new Pen(Color.FromArgb(200, 70, 46, 14), 1.4f)) g.DrawPath(pen, p);
    }
    static void Screw(Graphics g, float cx, float cy, float r, float ang)
    {
        using (var lg = new LinearGradientBrush(new RectangleF(cx - r, cy - r, 2 * r, 2 * r), Color.FromArgb(255, 240, 210, 140), Color.FromArgb(255, 110, 76, 30), 45f)) g.FillEllipse(lg, cx - r, cy - r, 2 * r, 2 * r);
        using (var pen = new Pen(Color.FromArgb(220, 60, 40, 12), 1.2f)) { double a = ang * Math.PI / 180; g.DrawLine(pen, cx - (float)Math.Cos(a) * r * 0.8f, cy - (float)Math.Sin(a) * r * 0.8f, cx + (float)Math.Cos(a) * r * 0.8f, cy + (float)Math.Sin(a) * r * 0.8f); }
    }
    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    static void Engrave(Graphics g, string text, RectangleF r, float size)
    {
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var f = new Font(LabelFont, size, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(Color.FromArgb(110, 255, 240, 200))) g.DrawString(text, f, b, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), sf);
            using (var b = new SolidBrush(Color.FromArgb(235, 58, 38, 12))) g.DrawString(text, f, b, r, sf);
        }
    }

    // ------------------------------------------------------------ full scenes
    public static Bitmap Background()
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        const float plinthTop = PlinthTop, plinthFront = PlinthFront;
        PutPixels(bmp, (x, y) =>
        {
            float dx = (x - 233) / 233f, dy = (y - 200) / 233f; float vig = Math.Max(0, 1 - (dx * dx + dy * dy) * 0.9f);
            if (y < plinthTop)
            {
                float n = Fbm(x * 0.04f, y * 0.04f) * 0.5f + Fbm(x * 0.6f, y * 0.6f) * 0.15f;
                float v = (1 + 6 * vig * vig) * (0.8f + n);
                return Color.FromArgb(255, Clamp(v * 1.15f), Clamp(v * 0.9f), Clamp(v * 0.8f));
            }
            // brushed dark gunmetal: streaks run horizontally
            float brush = Fbm(x * 0.015f, y * 1.4f) * 0.7f + Fbm(x * 0.004f, y * 4.0f) * 0.3f;
            float grain = Hash(x, y) * 0.08f;
            float shade = y < plinthFront ? 1.55f - (y - plinthTop) / (plinthFront - plinthTop) * 0.25f : Math.Max(0, 0.75f - (y - plinthFront) / 70f * 0.75f);
            shade *= 0.35f + 0.65f * vig;
            float m = (16 + 22 * brush) * (1 + grain) * shade;
            return Color.FromArgb(255, Clamp(m * 0.98f), Clamp(m * 1.0f), Clamp(m * 1.07f));
        });
        // warm wall glow behind the tubes + reflections on the polished top edge
        AddLight(bmp, 233, 205, 230, 150, 255, 100, 25, 0.13f);
        for (int i = 0; i < 4; i++)
        {
            float tx = BigX[i] + 42 * BigK, b = Bright[i];
            AddLight(bmp, tx, BigY + 120 * BigK, 78, 150, 255, 96, 22, 0.10f * b);          // halo on the wall behind each tube
            AddLight(bmp, tx, plinthTop + 8, 56, 10, 255, 118, 38, 0.72f * b);                // polished top edge
            AddLight(bmp, tx, plinthFront + 4, 46, 46, 255, 104, 30, 0.20f * b);              // spill down the brushed front
        }
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // plinth bevels
            using (var pen = new Pen(Color.FromArgb(150, 210, 215, 225), 1.2f)) g.DrawLine(pen, 0, plinthTop, S, plinthTop);
            using (var pen = new Pen(Color.FromArgb(200, 0, 0, 0), 2f)) g.DrawLine(pen, 0, plinthFront, S, plinthFront);
            using (var pen = new Pen(Color.FromArgb(110, 190, 195, 205), 1f)) g.DrawLine(pen, 0, plinthFront + 1.5f, S, plinthFront + 1.5f);
            // hex socket bolts on the front plate
            foreach (var bxy in new[] { new PointF(118, 396), new PointF(348, 396) }) Bolt(g, bxy.X, bxy.Y, 5);
            // big tubes
            for (int i = 0; i < 4; i++) TubeBack(g, BigX[i], BigY, BigK, true, i);
            // colon bulbs (INS-1 style)
            foreach (var cy in ColonY)
            {
                using (var br = new SolidBrush(Color.FromArgb(255, 22, 17, 15))) g.FillEllipse(br, 227, cy - 11, 12, 20);
                using (var pen = new Pen(Color.FromArgb(150, 150, 140, 130), 0.8f)) { g.DrawLine(pen, 231, cy + 9, 231, cy + 20); g.DrawLine(pen, 235, cy + 9, 235, cy + 20); }
                using (var br = new SolidBrush(Color.FromArgb(120, 120, 95, 75))) g.FillEllipse(br, 230.5f, cy - 3, 5, 5);
            }
            // IN-13 style bar graph tube for the battery, centred under the clock
            {
                int bx = BarX;
                var gr = new RectangleF(bx - 4, BarY - 3, BarW + 8, BarH + 6);
                using (var br = new SolidBrush(Color.FromArgb(255, 10, 9, 9))) g.FillPath(br, RoundRect(gr, 10));
                using (var pen = new Pen(Color.FromArgb(70, 150, 120, 95), 0.8f)) g.DrawLine(pen, bx + 2, BarY + BarH / 2f, bx + BarW - 2, BarY + BarH / 2f);
                using (var pen = new Pen(Color.FromArgb(200, 120, 120, 126), 2f)) { g.DrawLine(pen, bx - 6, BarY + BarH / 2f, bx - 4, BarY + BarH / 2f); g.DrawLine(pen, bx + BarW + 4, BarY + BarH / 2f, bx + BarW + 6, BarY + BarH / 2f); }
            }
        }
        // glow from the discharge on the socket rings and the lower glass
        for (int i = 0; i < 4; i++) AddLight(bmp, BigX[i] + 42 * BigK, BigY + 184 * BigK, 40 * BigK, 13, 255, 110, 34, 0.26f * Bright[i]);
        return bmp;
    }

    // warm-up cover frames: black shapes over the glass interiors, each tube flickering on its own, ending fully clear
    public const int WarmX = 20, WarmY = 80, WarmW = 426, WarmH = 256, WarmN = 14;
    static readonly float[][] Warm = {
        new float[]{1, 1, .30f, 1, .60f, .20f, .50f, .10f, 0, .15f, 0, 0, 0, 0},
        new float[]{1, 1, 1, .40f, 1, .70f, .20f, .60f, .15f, .30f, .05f, 0, 0, 0},
        new float[]{1, .50f, 1, 1, .30f, .80f, .30f, .10f, .40f, 0, .10f, 0, 0, 0},
        new float[]{1, 1, 1, 1, .50f, 1, .40f, .70f, .20f, .40f, .10f, .20f, 0, 0},
    };
    public static Bitmap WarmFrame(int n)
    {
        var bmp = new Bitmap(WarmW, WarmH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            for (int i = 0; i < 4; i++)
            {
                int a = (int)(238 * Warm[i][n]); if (a <= 0) continue;
                using (var br = new SolidBrush(Color.FromArgb(a, 4, 3, 3))) g.FillPath(br, Glass(BigX[i] - WarmX, BigY - WarmY, BigK));
            }
        }
        return bmp;
    }

    public static Bitmap Overlay()
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
            for (int i = 0; i < 4; i++) TubeFront(g, BigX[i], BigY, BigK, i);
            foreach (var cy in ColonY)
            {
                using (var pen = new Pen(Color.FromArgb(80, 230, 230, 240), 0.9f)) g.DrawEllipse(pen, 227, cy - 11, 12, 20);
                using (var br = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillEllipse(br, 229, cy - 8, 2.5f, 6);
            }
            {
                int bx = BarX;
                var gr = new RectangleF(bx - 4, BarY - 3, BarW + 8, BarH + 6);
                using (var pen = new Pen(Color.FromArgb(80, 220, 225, 235), 1f)) g.DrawPath(pen, RoundRect(gr, 10));
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f)) g.DrawLine(pen, bx + 4, BarY - 0.5f, bx + BarW - 4, BarY - 0.5f);
            }
        }
        return bmp;
    }

    static void EngraveMetal(Graphics g, string text, RectangleF r, float size)
    {
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var f = new Font(LabelFont, size, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(Color.FromArgb(230, 0, 0, 0))) g.DrawString(text, f, b, new RectangleF(r.X, r.Y - 1, r.Width, r.Height), sf);
            using (var b = new SolidBrush(Color.FromArgb(200, 168, 174, 186))) g.DrawString(text, f, b, r, sf);
        }
    }
    static void Bolt(Graphics g, float cx, float cy, float r)
    {
        using (var lg = new LinearGradientBrush(new RectangleF(cx - r, cy - r, 2 * r, 2 * r), Color.FromArgb(255, 150, 152, 160), Color.FromArgb(255, 30, 31, 34), 60f)) g.FillEllipse(lg, cx - r, cy - r, 2 * r, 2 * r);
        var hex = new PointF[6]; for (int i = 0; i < 6; i++) { double a = Math.PI / 3 * i; hex[i] = new PointF(cx + (float)Math.Cos(a) * r * 0.5f, cy + (float)Math.Sin(a) * r * 0.5f); }
        using (var br = new SolidBrush(Color.FromArgb(255, 12, 12, 14))) g.FillPolygon(br, hex);
    }

    static int Clamp(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }

    public static void RenderAll(string outDir)
    {
        Background().Save(System.IO.Path.Combine(outDir, "nx_bg.png"), ImageFormat.Png);
        Overlay().Save(System.IO.Path.Combine(outDir, "nx_front.png"), ImageFormat.Png);
        for (char c = '0'; c <= '9'; c++)
        {
            LitDigit(c, BigK).Save(System.IO.Path.Combine(outDir, "nx_d" + c + ".png"), ImageFormat.Png);
            AmbientDigit(c, BigK).Save(System.IO.Path.Combine(outDir, "nx_a" + c + ".png"), ImageFormat.Png);
        }
        for (int n = 0; n < WarmN; n++) WarmFrame(n).Save(System.IO.Path.Combine(outDir, "nx_w" + n.ToString("00") + ".png"), ImageFormat.Png);
        BulbLit().Save(System.IO.Path.Combine(outDir, "nx_bulb.png"), ImageFormat.Png);
        BarLit().Save(System.IO.Path.Combine(outDir, "nx_bar.png"), ImageFormat.Png);
    }
}
