using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGenerator
{
    private static readonly Color Purple = Color.FromArgb(112, 48, 160);
    private static readonly Color Violet = Color.FromArgb(147, 83, 205);
    private static readonly Color Teal = Color.FromArgb(20, 156, 150);
    private static readonly Color Blue = Color.FromArgb(38, 132, 220);
    private static readonly Color Ink = Color.FromArgb(54, 57, 66);
    private static readonly Color Soft = Color.FromArgb(233, 225, 244);

    public static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        Directory.CreateDirectory(args[0]);
        Save(args[0], "Summarize", DrawSummarize);
        Save(args[0], "Generate", DrawGenerate);
        Save(args[0], "Template", DrawTemplate);
        Save(args[0], "Rewrite", DrawRewrite);
        Save(args[0], "QA", DrawQA);
        Save(args[0], "Translate", DrawTranslate);
        Save(args[0], "Tag", DrawTag);
        Save(args[0], "ExtractTodos", DrawTodos);
        Save(args[0], "Settings", DrawSettings);
        Save(args[0], "Help", DrawHelp);
        return 0;
    }

    private static void Save(string dir, string name, Action<Graphics> draw)
    {
        using (var large = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(large))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(4f, 4f);
            draw(g);
            using (var icon = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics small = Graphics.FromImage(icon))
            {
                small.InterpolationMode = InterpolationMode.HighQualityBicubic;
                small.CompositingQuality = CompositingQuality.HighQuality;
                small.DrawImage(large, 0, 0, 32, 32);
                icon.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            }
        }
    }

    private static Pen Pen(Color c, float width = 1.7f) { return new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }
    private static Brush Brush(Color c) { return new SolidBrush(c); }

    private static void DrawDocument(Graphics g)
    {
        using (var fill = Brush(Color.White)) g.FillPath(fill, RoundedRect(5, 3, 20, 26, 3));
        using (var p = Pen(Purple, 1.8f)) g.DrawPath(p, RoundedRect(5, 3, 20, 26, 3));
        using (var accent = Brush(Soft)) g.FillRectangle(accent, 6.8f, 5, 3.2f, 22);
    }

    private static void DrawSummarize(Graphics g)
    {
        DrawDocument(g);
        using (var p = Pen(Ink, 1.45f)) { g.DrawLine(p, 11, 10, 21, 10); g.DrawLine(p, 11, 15, 21, 15); g.DrawLine(p, 11, 20, 18, 20); }
        DrawSpark(g, 24, 23, 4.2f, Teal);
    }

    private static void DrawGenerate(Graphics g)
    {
        using (var p = Pen(Purple, 3.2f)) g.DrawLine(p, 8, 25, 21.5f, 11.5f);
        using (var p = Pen(Violet, 1.4f)) g.DrawLine(p, 10.5f, 22.5f, 13.5f, 25.5f);
        DrawSpark(g, 23.5f, 8.5f, 6, Teal);
        DrawSpark(g, 9, 9, 3.3f, Blue);
        DrawSpark(g, 25, 22, 2.8f, Violet);
    }

    private static void DrawTemplate(Graphics g)
    {
        using (var fill = Brush(Color.White)) g.FillPath(fill, RoundedRect(4, 4, 24, 24, 3));
        using (var p = Pen(Purple, 1.8f)) g.DrawPath(p, RoundedRect(4, 4, 24, 24, 3));
        using (var b = Brush(Purple)) g.FillRectangle(b, 4.8f, 4.8f, 22.4f, 5.2f);
        using (var b = Brush(Soft)) g.FillPath(b, RoundedRect(8, 13, 7, 10, 1.5f));
        using (var p = Pen(Teal, 1.5f)) { g.DrawLine(p, 18, 14, 24, 14); g.DrawLine(p, 18, 18, 24, 18); g.DrawLine(p, 18, 22, 22, 22); }
    }

    private static void DrawRewrite(Graphics g)
    {
        using (var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var b = Brush(Purple)) g.DrawString("A", font, b, 5, 6);
        using (var p = Pen(Teal, 2.1f)) g.DrawArc(p, 11, 9, 16, 15, -60, 230);
        using (var b = Brush(Teal)) g.FillPolygon(b, new[] { new PointF(10, 20), new PointF(15, 19), new PointF(13, 24) });
    }

    private static void DrawQA(Graphics g)
    {
        using (var b = Brush(Soft)) g.FillPath(b, RoundedRect(4, 6, 19, 14, 5));
        using (var p = Pen(Purple, 1.8f)) { g.DrawPath(p, RoundedRect(4, 6, 19, 14, 5)); g.DrawLine(p, 9, 19, 7, 24); g.DrawLine(p, 7, 24, 13, 20); }
        using (var b = Brush(Teal)) g.FillPath(b, RoundedRect(15, 17, 13, 9, 4));
        using (var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var b = Brush(Purple)) g.DrawString("?", font, b, 10, 7);
    }

    private static void DrawTranslate(Graphics g)
    {
        using (var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var b = Brush(Purple)) g.DrawString("A", font, b, 3, 4);
        using (var font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var b = Brush(Teal)) g.DrawString("文", font, b, 15, 14);
        using (var p = Pen(Blue, 1.6f)) { g.DrawLine(p, 7, 24, 20, 11); g.DrawLine(p, 17, 11, 20, 11); g.DrawLine(p, 20, 11, 20, 14); }
    }

    private static void DrawTag(Graphics g)
    {
        var pts = new[] { new PointF(4, 14), new PointF(14, 4), new PointF(27, 5), new PointF(28, 18), new PointF(18, 28) };
        using (var b = Brush(Soft)) g.FillPolygon(b, pts);
        using (var p = Pen(Purple, 1.8f)) g.DrawPolygon(p, pts);
        using (var b = Brush(Teal)) g.FillEllipse(b, 17.5f, 9, 4, 4);
    }

    private static void DrawTodos(Graphics g)
    {
        using (var fill = Brush(Color.White)) g.FillPath(fill, RoundedRect(5, 3, 22, 26, 3));
        using (var p = Pen(Purple, 1.8f)) g.DrawPath(p, RoundedRect(5, 3, 22, 26, 3));
        using (var p = Pen(Teal, 2)) { g.DrawLine(p, 9, 10, 11, 12); g.DrawLine(p, 11, 12, 15, 7); g.DrawLine(p, 9, 20, 11, 22); g.DrawLine(p, 11, 22, 15, 17); }
        using (var p = Pen(Ink, 1.4f)) { g.DrawLine(p, 17, 10, 23, 10); g.DrawLine(p, 17, 20, 23, 20); }
    }

    private static void DrawSettings(Graphics g)
    {
        using (var p = Pen(Ink, 1.8f)) { g.DrawLine(p, 6, 9, 26, 9); g.DrawLine(p, 6, 16, 26, 16); g.DrawLine(p, 6, 23, 26, 23); }
        using (var b = Brush(Purple)) g.FillEllipse(b, 10, 5.5f, 7, 7);
        using (var b = Brush(Teal)) g.FillEllipse(b, 19, 12.5f, 7, 7);
        using (var b = Brush(Blue)) g.FillEllipse(b, 8, 19.5f, 7, 7);
    }

    private static void DrawHelp(Graphics g)
    {
        using (var b = Brush(Soft)) g.FillEllipse(b, 4, 4, 24, 24);
        using (var p = Pen(Purple, 1.8f)) g.DrawEllipse(p, 4, 4, 24, 24);
        using (var font = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var b = Brush(Purple)) g.DrawString("?", font, b, 10, 5);
    }

    private static void DrawSpark(Graphics g, float x, float y, float r, Color color)
    {
        var pts = new[] { new PointF(x, y-r), new PointF(x+r*.28f, y-r*.28f), new PointF(x+r, y), new PointF(x+r*.28f, y+r*.28f), new PointF(x, y+r), new PointF(x-r*.28f, y+r*.28f), new PointF(x-r, y), new PointF(x-r*.28f, y-r*.28f) };
        using (var b = Brush(color)) g.FillPolygon(b, pts);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r*2, r*2, 180, 90); p.AddArc(x+w-r*2, y, r*2, r*2, 270, 90);
        p.AddArc(x+w-r*2, y+h-r*2, r*2, r*2, 0, 90); p.AddArc(x, y+h-r*2, r*2, r*2, 90, 90);
        p.CloseFigure(); return p;
    }
}
