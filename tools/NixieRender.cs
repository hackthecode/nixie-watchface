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
    // gas mix: multipliers on the green and blue of the glow (1,1 = classic neon orange)
    public static float GasG = 1, GasB = 1;
    public static readonly int[] BarXs = { 72, 274 };   // battery tube, steps tube
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
            new float[]{0, 1.00f,0.80f,0.48f, 0.95f},
            new float[]{1.5f, 1.00f,0.44f,0.07f, 0.62f},
            new float[]{4, 1.00f,0.35f,0.03f, 0.66f},
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
            float r = Math.Min(1, R[i]) * f, g = Math.Min(1, G[i] * GasG) * f, b = Math.Min(1, B[i] * GasB) * f;
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

    // ------------------------------------------------------------ lit digit sprite
    // The sprite is larger than the tube box by Pad pixels on every side, so the halo can spill through the glass.
    public static int Pad(float k) { return (int)Math.Round(14 * k); }
    public static Bitmap LitDigit(char c, float k)
    {
        int m = Pad(k);
        int w = (int)Math.Ceiling(84 * k) + 2 * m, h = (int)Math.Ceiling(196 * k) + 2 * m;
        var mask = MaskOf(w, h, g => g.FillPath(Brushes.White, Cathode(c, m, m, k)));
        // The gas glows in a sheath around the metal: the lit shape is the whole stroke, with a thin darker core where the cathode itself sits.
        var sheath = Blur(Erode(mask, w, h, 3), w, h, 1);
        var core = Blur(Erode(mask, w, h, 5), w, h, 1);
        var wire = new float[w * h];
        for (int i = 0; i < wire.Length; i++) wire[i] = Math.Min(1, sheath[i] * 2.2f) * (1 - 0.45f * Math.Min(1, core[i] * 3f));
        // unlit cathodes in front of this one cross the glow as thin dark wires
        int me = Order.IndexOf(c);
        var front = MaskOf(w, h, g => { using (var pen = new Pen(Color.White, 0.8f * k)) { pen.LineJoin = LineJoin.Round; for (int j = me + 1; j < Order.Length; j++) g.DrawPath(pen, Cathode(Order[j], m, m, k)); } });
        for (int i = 0; i < wire.Length; i++) wire[i] *= 1 - 0.30f * front[i];
        var extra = new float[w * h];
        // reflections of the lit cathode in the curved glass walls: squeezed, mirrored copies near each side
        float[] wallX = { m + 13f * k, m + 71f * k }; float half = 4.5f * k, squeeze = 6.5f;
        for (int y = 0; y < h; y++) foreach (var xr in wallX)
            for (int x = (int)(xr - half); x <= (int)(xr + half); x++)
            {
                if (x < 0 || x >= w) continue;
                float t = (x - xr) / half; int sx = (int)Math.Round(m + 42 * k - (x - xr) * squeeze);
                extra[y * w + x] += At(wire, w, h, sx, y) * (1 - t * t);
            }
        extra = Blur(extra, w, h, 2);
        for (int i = 0; i < extra.Length; i++) extra[i] *= 0.55f;
        // the lit digit lights what is next to it: the other cathodes, the mica discs and the support rods
        var near = Blur(wire, w, h, (int)(8 * k));
        var metal = MaskOf(w, h, g =>
        {
            using (var pen = new Pen(Color.White, 1.0f * k)) { pen.LineJoin = LineJoin.Round; foreach (char o in Order) if (o != c) g.DrawPath(pen, Cathode(o, m, m, k)); }
            using (var br = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) { g.FillEllipse(br, m + 8 * k, m + 170 * k, 68 * k, 9 * k); g.FillEllipse(br, m + 9 * k, m + 39 * k, 66 * k, 8 * k); }
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.4f * k)) { g.DrawLine(pen, m + 11 * k, m + 40 * k, m + 11 * k, m + 182 * k); g.DrawLine(pen, m + 73 * k, m + 40 * k, m + 73 * k, m + 182 * k); }
        });
        for (int i = 0; i < extra.Length; i++) extra[i] += near[i] * metal[i] * 1.9f;
        return GlowBitmap(w, h, wire, k, 10 * k, extra);
    }

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
    // Honeycomb wrapped on a cylinder of radius rad around cx: the cells bunch up toward the glass walls.
    static void HoneycombCyl(Graphics g, Pen pen, float cx, float rad, float top, float bottom, float s)
    {
        float hw = s * 0.866f, arc = (float)(Math.PI * 0.5) * 0.95f * rad;
        for (int row = 0; ; row++)
        {
            float cy = top + row * s * 1.5f; if (cy - s > bottom) break;
            for (float u = -arc - 2 * hw + (row % 2 == 1 ? hw : 0); u < arc + 2 * hw; u += 2 * hw)
            {
                var pts = new PointF[6]; bool ok = true;
                for (int i = 0; i < 6 && ok; i++)
                {
                    double a = Math.PI / 3 * i + Math.PI / 6; float uu = u + (float)Math.Cos(a) * s;
                    if (Math.Abs(uu) > arc) ok = false; else pts[i] = new PointF(cx + rad * (float)Math.Sin(uu / rad), cy + (float)Math.Sin(a) * s);
                }
                if (ok) g.DrawPolygon(pen, pts);
            }
        }
    }
    static void ClipCage(Graphics g, GraphicsPath glass, float ox, float oy, float k)
    { g.SetClip(glass); g.SetClip(new RectangleF(ox + 8 * k, oy + 44 * k, 68 * k, 136 * k), CombineMode.Intersect); }
    static Color Tone(int a, int r, int gr, int b, float t) { return Color.FromArgb(a, Clamp(r * t), Clamp(gr * t), Clamp(b * t)); }

    // ---- opaque parts of a tube: shadow on the plinth, socket seen slightly from above, dark glass interior (goes into the background)
    static void TubeShell(Graphics g, float ox, float oy, float k, int seed)
    {
        var rnd = new Random(100 + seed * 17); rnd.NextDouble(); float tone = 0.88f + (float)rnd.NextDouble() * 0.24f;
        float cx = ox + 42 * k;
        // contact shadow on the plinth top
        using (var gp = new GraphicsPath())
        {
            var sr = new RectangleF(cx - 50 * k, oy + 207 * k, 100 * k, 17 * k); gp.AddEllipse(sr);
            using (var pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(215, 0, 0, 0); pg.SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) }; g.FillPath(pg, gp); }
        }
        // socket: a short cylinder, so its bottom edge is curved and its top is an ellipse
        var body = new RectangleF(ox + 8 * k, oy + 185 * k, 68 * k, 30 * k);
        using (var gp = new GraphicsPath())
        {
            gp.AddLine(body.Left, body.Top, body.Right, body.Top); gp.AddLine(body.Right, body.Top, body.Right, body.Bottom);
            gp.AddArc(body.Left, body.Bottom - 4 * k, body.Width, 8 * k, 0, 180); gp.CloseFigure();
            using (var lg = new LinearGradientBrush(new RectangleF(body.X - 1, body.Y, body.Width + 2, body.Height + 5 * k), Color.Black, Color.Black, 0f))
            { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 9, 9, 11), Color.FromArgb(255, 52, 51, 56), Color.FromArgb(255, 24, 24, 27), Color.FromArgb(255, 9, 9, 10), Color.FromArgb(255, 4, 4, 5) }; cb.Positions = new[] { 0f, 0.24f, 0.5f, 0.8f, 1f }; lg.InterpolationColors = cb; g.FillPath(lg, gp); }
        }
        // chrome collar: full ellipse here, its front half is redrawn over the glass later
        var ring = new RectangleF(ox + 5 * k, oy + 180.5f * k, 74 * k, 10 * k);
        using (var lg = new LinearGradientBrush(ring, Color.White, Color.Black, 0f))
        { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 60, 60, 64), Color.FromArgb(255, 225, 225, 230), Color.FromArgb(255, 112, 112, 118), Color.FromArgb(255, 196, 196, 202), Color.FromArgb(255, 52, 52, 56) }; cb.Positions = new[] { 0f, 0.22f, 0.5f, 0.75f, 1f }; lg.InterpolationColors = cb; g.FillEllipse(lg, ring); }
        using (var br = new SolidBrush(Color.FromArgb(255, 5, 5, 6))) g.FillEllipse(br, ox + 8 * k, oy + 182 * k, 68 * k, 6.5f * k);
        // glass interior: darkest at the walls, where the line of sight runs through the most glass
        var glass = Glass(ox, oy, k); var gb = glass.GetBounds();
        using (var lg = new LinearGradientBrush(new RectangleF(gb.X - 1, gb.Y, gb.Width + 2, gb.Height), Color.Black, Color.Black, 0f))
        { var cb = new ColorBlend(7); cb.Colors = new[] { Color.FromArgb(255, 3, 3, 3), Tone(255, 15, 12, 11, tone), Tone(255, 31, 25, 22, tone), Tone(255, 25, 20, 18, tone), Tone(255, 31, 25, 22, tone), Tone(255, 14, 11, 10, tone), Color.FromArgb(255, 3, 3, 3) }; cb.Positions = new[] { 0f, 0.1f, 0.3f, 0.5f, 0.7f, 0.9f, 1f }; lg.InterpolationColors = cb; g.FillPath(lg, glass); }
    }

    // ---- far half of the anode cage and the getter (own layer: it shifts against the digits when the wrist tilts)
    static void TubeMeshBack(Graphics g, float ox, float oy, float k)
    {
        var glass = Glass(ox, oy, k); var st = g.Save(); ClipCage(g, glass, ox, oy, k);
        using (var pen = new Pen(Color.FromArgb(50, 120, 105, 92), 0.55f)) HoneycombCyl(g, pen, ox + 42 * k, 32 * k, oy + 44 * k, oy + 180 * k, 2.3f * k);
        g.Restore(st); st = g.Save(); g.SetClip(glass);
        using (var gp = new GraphicsPath())
        {
            var gr = new RectangleF(ox + 15 * k, oy + 5 * k, 54 * k, 30 * k); gp.AddEllipse(gr);
            using (var pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(150, 66, 68, 78); pg.SurroundColors = new[] { Color.FromArgb(0, 30, 30, 36) }; pg.CenterPoint = new PointF(gr.X + gr.Width * 0.42f, gr.Y + gr.Height * 0.5f); g.FillPath(pg, gp); }
            using (var pen = new Pen(Color.FromArgb(40, 190, 195, 215), 0.9f * k)) g.DrawArc(pen, gr.X + 6 * k, gr.Y + 5 * k, gr.Width - 12 * k, gr.Height - 6 * k, 200, 70);
        }
        g.Restore(st);
    }

    // ---- what sits in the plane of the digits: rods, mica discs, pins, leads and the unlit cathode stack
    static void TubeInner(Graphics g, float ox, float oy, float k, int seed)
    {
        var rnd = new Random(100 + seed * 17); float jx = (float)(rnd.NextDouble() - 0.5) * 1.4f;
        var glass = Glass(ox, oy, k); var st = g.Save(); g.SetClip(glass);
        // support rods: round bars, lit from the left
        foreach (float rx in new[] { 11f, 73f })
        {
            using (var pen = new Pen(Color.FromArgb(150, 92, 86, 78), 1.9f * k)) g.DrawLine(pen, ox + rx * k, oy + 40 * k, ox + rx * k, oy + 182 * k);
            using (var pen = new Pen(Color.FromArgb(150, 200, 190, 176), 0.6f * k)) g.DrawLine(pen, ox + (rx - 0.45f) * k, oy + 40 * k, ox + (rx - 0.45f) * k, oy + 182 * k);
        }
        // mica spacers: discs seen from slightly above
        foreach (var d in new[] { new RectangleF(ox + 8 * k, oy + 170 * k, 68 * k, 9 * k), new RectangleF(ox + 9 * k, oy + 39 * k, 66 * k, 8 * k) })
        {
            using (var br = new SolidBrush(Color.FromArgb(58, 230, 225, 210))) g.FillEllipse(br, d);
            using (var pen = new Pen(Color.FromArgb(84, 245, 240, 226), 0.8f * k)) g.DrawArc(pen, d, 0, 180);
            using (var pen = new Pen(Color.FromArgb(36, 245, 240, 226), 0.6f * k)) g.DrawArc(pen, d, 180, 180);
        }
        using (var pen = new Pen(Color.FromArgb(160, 170, 160, 150), 1f * k)) for (int i = 0; i < 9; i++) { float x = ox + (16 + i * 6.5f) * k; g.DrawLine(pen, x, oy + 178 * k, x, oy + 190 * k); }
        using (var pen = new Pen(Color.FromArgb(95, 165, 150, 135), 0.7f * k)) for (int i = 0; i < 9; i++) { float px = ox + (16 + i * 6.5f) * k; g.DrawBezier(pen, ox + (30 + i * 3f + jx) * k, oy + 166 * k, ox + (30 + i * 3f) * k, oy + 172 * k, px, oy + 172 * k, px, oy + 179 * k); }
        // unlit cathodes: stamped metal, so each has a darker body and a thin lit edge on its upper left
        for (int i = 0; i < Order.Length; i++)
        {
            float depth = i / 9f;
            using (var pen = new Pen(Color.FromArgb((int)(60 + 90 * depth), 112, 90, 72), Math.Max(0.8f, 1.3f * k))) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, Cathode(Order[i], ox, oy, k)); }
            using (var pen = new Pen(Color.FromArgb((int)(34 + 86 * depth), 226, 204, 176), 0.5f * k)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, Cathode(Order[i], ox - 0.5f, oy - 0.5f, k)); }
        }
        g.Restore(st);
    }

    // ---- near half of the anode cage (own layer for the tilt parallax)
    static void TubeMeshFront(Graphics g, float ox, float oy, float k)
    {
        var st = g.Save(); ClipCage(g, Glass(ox, oy, k), ox, oy, k);
        using (var pen = new Pen(Color.FromArgb(24, 20, 17, 15), 0.5f)) HoneycombCyl(g, pen, ox + 42 * k, 32 * k, oy + 44 * k, oy + 180 * k, 2.3f * k);
        g.Restore(st);
    }

    // ---- the glass itself: thick dark walls, double rim, dust, scratches, front half of the collar (static)
    static void TubeGlass(Graphics g, float ox, float oy, float k, int seed)
    {
        var glass = Glass(ox, oy, k); var gb = glass.GetBounds();
        var st = g.Save(); g.SetClip(glass);
        var rnd = new Random(500 + seed * 31);
        // walls: where the cylinder turns away the glass is seen edge-on, so it goes dark and then catches a thin bright line
        float ww = 10 * k;
        using (var lg = new LinearGradientBrush(new RectangleF(gb.Left - 1, gb.Top, ww + 1, gb.Height), Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f)) g.FillRectangle(lg, gb.Left, gb.Top, ww, gb.Height);
        using (var lg = new LinearGradientBrush(new RectangleF(gb.Right - ww, gb.Top, ww + 1, gb.Height), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 0f)) g.FillRectangle(lg, gb.Right - ww, gb.Top, ww, gb.Height);
        using (var inner = (GraphicsPath)glass.Clone())
        {
            var mx = new Matrix(); mx.Translate(gb.X + gb.Width / 2, gb.Y + gb.Height / 2); mx.Scale((gb.Width - 5.2f * k) / gb.Width, (gb.Height - 5.2f * k) / gb.Height); mx.Translate(-(gb.X + gb.Width / 2), -(gb.Y + gb.Height / 2)); inner.Transform(mx);
            using (var pen = new Pen(Color.FromArgb(34, 225, 230, 245), 0.8f * k)) g.DrawPath(pen, inner);
        }
        for (int i = 0; i < 26; i++) { float x = ox + (8 + (float)rnd.NextDouble() * 68) * k, y = oy + (10 + (float)rnd.NextDouble() * 172) * k, r = 0.35f + (float)rnd.NextDouble() * 0.7f;
            using (var br = new SolidBrush(Color.FromArgb(rnd.Next(22, 70), 235, 235, 240))) g.FillEllipse(br, x, y, r * 2, r * 2); }
        for (int i = 0; i < 4; i++) { float x = ox + (10 + (float)rnd.NextDouble() * 60) * k, y = oy + (20 + (float)rnd.NextDouble() * 150) * k, len = (6 + (float)rnd.NextDouble() * 18) * k; double a = rnd.NextDouble() * Math.PI;
            using (var pen = new Pen(Color.FromArgb(rnd.Next(14, 34), 240, 240, 250), 0.6f)) g.DrawBezier(pen, x, y, x + (float)Math.Cos(a) * len * 0.3f + 1, y + (float)Math.Sin(a) * len * 0.3f, x + (float)Math.Cos(a) * len * 0.7f - 1, y + (float)Math.Sin(a) * len * 0.7f, x + (float)Math.Cos(a) * len, y + (float)Math.Sin(a) * len); }
        g.Restore(st);
        using (var pen = new Pen(Color.FromArgb(92, 220, 225, 235), 1.2f * k)) g.DrawPath(pen, glass);
        // front half of the chrome collar, over the bottom of the glass
        var ring = new RectangleF(ox + 5 * k, oy + 180.5f * k, 74 * k, 10 * k);
        using (var lg = new LinearGradientBrush(ring, Color.White, Color.Black, 0f))
        { var cb = new ColorBlend(5); cb.Colors = new[] { Color.FromArgb(255, 60, 60, 64), Color.FromArgb(255, 232, 232, 238), Color.FromArgb(255, 118, 118, 124), Color.FromArgb(255, 200, 200, 206), Color.FromArgb(255, 52, 52, 56) }; cb.Positions = new[] { 0f, 0.22f, 0.5f, 0.75f, 1f }; lg.InterpolationColors = cb;
          using (var pen = new Pen(lg, 3.4f * k)) g.DrawArc(pen, ring.X + 1.7f * k, ring.Y + 1.2f * k, ring.Width - 3.4f * k, ring.Height - 1.6f * k, 4, 172); }
        using (var pen = new Pen(Color.FromArgb(150, 0, 0, 0), 0.8f * k)) g.DrawArc(pen, ring.X, ring.Y + 1.5f * k, ring.Width, ring.Height, 6, 168);
        // exhaust tip
        using (var br = new SolidBrush(Color.FromArgb(110, 210, 215, 225))) g.FillEllipse(br, ox + 38 * k, oy + 0.5f * k, 8 * k, 7 * k);
        using (var br = new SolidBrush(Color.FromArgb(180, 255, 255, 255))) g.FillEllipse(br, ox + 39.5f * k, oy + 1.5f * k, 2.5f * k, 2.5f * k);
    }

    // ---- reflections on the glass (own layer: they slide across the tube when the wrist tilts)
    static void TubeHighlights(Graphics g, float ox, float oy, float k)
    {
        var st = g.Save(); g.SetClip(Glass(ox, oy, k));
        using (var gp = new GraphicsPath())
        {
            gp.AddPolygon(new[] { new PointF(ox + 24 * k, oy + 10 * k), new PointF(ox + 42 * k, oy + 10 * k), new PointF(ox + 28 * k, oy + 186 * k), new PointF(ox + 14 * k, oy + 186 * k) });
            using (var br = new SolidBrush(Color.FromArgb(9, 235, 240, 255))) g.FillPath(br, gp);
        }
        var hl = new RectangleF(ox + 13 * k, oy + 20 * k, 9 * k, 160 * k);
        using (var lg = new LinearGradientBrush(hl, Color.Transparent, Color.Transparent, 0f))
        { var cb = new ColorBlend(3); cb.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(105, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }; cb.Positions = new[] { 0f, 0.45f, 1f }; lg.InterpolationColors = cb; g.FillRectangle(lg, hl); }
        var hl2 = new RectangleF(ox + 64 * k, oy + 30 * k, 5 * k, 140 * k);
        using (var lg = new LinearGradientBrush(hl2, Color.Transparent, Color.Transparent, 0f))
        { var cb = new ColorBlend(3); cb.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(48, 255, 240, 220), Color.FromArgb(0, 255, 255, 255) }; cb.Positions = new[] { 0f, 0.5f, 1f }; lg.InterpolationColors = cb; g.FillRectangle(lg, hl2); }
        using (var pen = new Pen(Color.FromArgb(95, 255, 255, 255), 2.2f * k)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawArc(pen, ox + 14 * k, oy + 12 * k, 58 * k, 60 * k, 200, 50); }
        g.Restore(st);
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
    public static Bitmap Background(int style = 0)
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
            if (style > 0)
            {
                // wood: long wavy growth rings along the plate, fine pores across them
                float ring = Fbm(x * 0.006f, y * 0.09f); float wave = (float)Math.Sin(y * 0.55f + ring * 26f + Fbm(x * 0.02f, y * 0.02f) * 6f) * 0.5f + 0.5f;
                float pore = Fbm(x * 0.9f, y * 0.12f);
                float wv = (0.50f + 0.34f * wave + 0.30f * pore) * (1 + grain) * Math.Min(1.25f, shade * 1.25f + 0.10f);
                return style == 1 ? Color.FromArgb(255, Clamp(96 * wv), Clamp(58 * wv), Clamp(31 * wv)) : Color.FromArgb(255, Clamp(124 * wv), Clamp(44 * wv), Clamp(26 * wv));
            }
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
            AddLight(bmp, tx, plinthFront + 30, 26, 70, 255, 98, 26, 0.12f * b);              // long smeared reflection of the tube in the plate
        }
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // plinth bevels
            using (var pen = new Pen(Color.FromArgb(150, 210, 215, 225), 1.2f)) g.DrawLine(pen, 0, plinthTop, S, plinthTop);
            using (var pen = new Pen(Color.FromArgb(200, 0, 0, 0), 2f)) g.DrawLine(pen, 0, plinthFront, S, plinthFront);
            using (var pen = new Pen(Color.FromArgb(110, 190, 195, 205), 1f)) g.DrawLine(pen, 0, plinthFront + 1.5f, S, plinthFront + 1.5f);
            // hex socket bolts on the front plate
            if (style == 0) Bolt(g, 233, 390, 5);
            else { var plate = new RectangleF(203, 380, 60, 19); Brass(g, plate, 3); Engrave(g, "NIXIE", plate, 12); Screw(g, 208, 389.5f, 1.8f, 30); Screw(g, 258, 389.5f, 1.8f, 110); }
            EngraveMetal(g, "BATTERY", new RectangleF(BarXs[0], 366, BarW, 13), 10); EngraveMetal(g, "STEPS", new RectangleF(BarXs[1], 366, BarW, 13), 10);
            // big tubes
            for (int i = 0; i < 4; i++) TubeShell(g, BigX[i], BigY, BigK, i);
            // colon bulbs (INS-1 style)
            foreach (var cy in ColonY)
            {
                using (var br = new SolidBrush(Color.FromArgb(255, 22, 17, 15))) g.FillEllipse(br, 227, cy - 11, 12, 20);
                using (var pen = new Pen(Color.FromArgb(150, 150, 140, 130), 0.8f)) { g.DrawLine(pen, 231, cy + 9, 231, cy + 20); g.DrawLine(pen, 235, cy + 9, 235, cy + 20); }
                using (var br = new SolidBrush(Color.FromArgb(120, 120, 95, 75))) g.FillEllipse(br, 230.5f, cy - 3, 5, 5);
            }
            // IN-13 style bar graph tube for the battery, centred under the clock
            foreach (int bx in BarXs)
            {
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
    public const int BaseY = 340;
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
        var buf = new byte[WarmW * WarmH * 4];
        for (int i = 0; i < 4; i++)
        {
            float d = Warm[i][n]; if (d <= 0) continue; int tube = i;
            var gm = MaskOf(WarmW, WarmH, g => g.FillPath(Brushes.White, Glass(BigX[tube] - WarmX, BigY - WarmY, BigK)));
            for (int y = 0; y < WarmH; y++) for (int x = 0; x < WarmW; x++)
            {
                float m = gm[y * WarmW + x]; if (m <= 0) continue;
                float thr = 0.15f + 0.85f * Fbm(x * 0.045f + i * 9.1f, y * 0.03f + n * 0.21f);   // patches drift a little from frame to frame
                float a = Smooth((d * 1.45f - thr) / 0.22f) * m * 0.94f;
                int p = (y * WarmW + x) * 4; if (a * 255 <= buf[p + 3]) continue;
                buf[p + 2] = 13; buf[p + 1] = 4; buf[p] = 1; buf[p + 3] = (byte)(255 * a);
            }
        }
        var bd = bmp.LockBits(new Rectangle(0, 0, WarmW, WarmH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(buf, 0, bd.Scan0, buf.Length); bmp.UnlockBits(bd); return bmp;
    }

    // tube interiors without a lit digit, cropped to the glass: the backdrop for the date view
    public static Bitmap Cover()
    {
        var bmp = new Bitmap(WarmW, WarmH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
            g.TranslateTransform(-WarmX, -WarmY);
            for (int i = 0; i < 4; i++)
            {
                var st = g.Save(); g.SetClip(Glass(BigX[i], BigY, BigK));
                TubeShell(g, BigX[i], BigY, BigK, i); g.Restore(st);
                TubeMeshBack(g, BigX[i], BigY, BigK); TubeInner(g, BigX[i], BigY, BigK, i);
            }
        }
        return bmp;
    }
    // glass-shaped mask (any colour, the alpha is what counts)
    public static Bitmap GlassMask(int alpha)
    {
        var bmp = new Bitmap(WarmW, WarmH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using (var br = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) for (int i = 0; i < 4; i++) g.FillPath(br, Glass(BigX[i] - WarmX, BigY - WarmY, BigK));
        }
        return bmp;
    }
    static Bitmap Flat(int alpha) { var b = new Bitmap(8, 8, PixelFormat.Format32bppArgb); using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(alpha, 255, 255, 255)); return b; }

    static Bitmap Layer(Action<Graphics> draw)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent); draw(g); }
        return bmp;
    }
    public static Bitmap BackMesh() { return Layer(g => { for (int i = 0; i < 4; i++) TubeMeshBack(g, BigX[i], BigY, BigK); }); }
    public static Bitmap Inner() { return Layer(g => { for (int i = 0; i < 4; i++) TubeInner(g, BigX[i], BigY, BigK, i); }); }
    public static Bitmap FrontMesh() { return Layer(g => { for (int i = 0; i < 4; i++) TubeMeshFront(g, BigX[i], BigY, BigK); }); }
    public static Bitmap Highlights() { return Layer(g => { for (int i = 0; i < 4; i++) TubeHighlights(g, BigX[i], BigY, BigK); }); }

    public static Bitmap Overlay()
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
            for (int i = 0; i < 4; i++) TubeGlass(g, BigX[i], BigY, BigK, i);
            foreach (var cy in ColonY)
            {
                using (var pen = new Pen(Color.FromArgb(80, 230, 230, 240), 0.9f)) g.DrawEllipse(pen, 227, cy - 11, 12, 20);
                using (var br = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillEllipse(br, 229, cy - 8, 2.5f, 6);
            }
            foreach (int bx in BarXs)
            {
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
        Func<string, string> P = n => System.IO.Path.Combine(outDir, n + ".png");
        Background(0).Save(P("nx_bg"), ImageFormat.Png);
        // wooden bases replace only the strip from just above the plinth down
        for (int s = 1; s <= 2; s++) using (var full = Background(s)) using (var strip = full.Clone(new Rectangle(0, BaseY, S, S - BaseY), PixelFormat.Format32bppArgb)) strip.Save(P("nx_base" + s), ImageFormat.Png);
        Overlay().Save(P("nx_front"), ImageFormat.Png);
        BackMesh().Save(P("nx_back"), ImageFormat.Png);
        Inner().Save(P("nx_inner"), ImageFormat.Png);
        FrontMesh().Save(P("nx_mesh"), ImageFormat.Png);
        Highlights().Save(P("nx_hl"), ImageFormat.Png);
        Cover().Save(P("nx_cover"), ImageFormat.Png);
        GlassMask(255).Save(P("nx_mk"), ImageFormat.Png);
        Flat(255).Save(P("nx_on"), ImageFormat.Png); Flat(0).Save(P("nx_off"), ImageFormat.Png);
        // d = classic neon orange, r = redder, y = amber
        var gases = new[] { new object[] { "d", 1f, 1f }, new object[] { "r", 0.64f, 0.75f }, new object[] { "y", 1.30f, 1.35f } };
        foreach (var gs in gases)
        {
            GasG = (float)gs[1]; GasB = (float)gs[2];
            for (char c = '0'; c <= '9'; c++) LitDigit(c, BigK).Save(P("nx_" + gs[0] + c), ImageFormat.Png);
        }
        GasG = 1; GasB = 1;
        for (char c = '0'; c <= '9'; c++) AmbientDigit(c, BigK).Save(P("nx_a" + c), ImageFormat.Png);
        for (int n = 0; n < WarmN; n++) WarmFrame(n).Save(P("nx_w" + n.ToString("00")), ImageFormat.Png);
        BulbLit().Save(P("nx_bulb"), ImageFormat.Png);
        BarLit().Save(P("nx_bar"), ImageFormat.Png);
    }
}
