using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SnipFeedback.Core;

// 止めた画面から切り出し、赤枠と番号を焼き込む（R-03・R-08、N-02 の画素一致）
public static class SnipImage
{
    public static readonly Color Red = Color.FromArgb(229, 57, 53);

    public static Bitmap Crop(Bitmap screen, Rectangle area)
    {
        var r = Rectangle.Intersect(area, new Rectangle(Point.Empty, screen.Size));
        return screen.Clone(r, screen.PixelFormat);
    }

    // 線の太さと番号の大きさは画像の大きさによらず読める太さにする（scale は表示スケール。150% なら 1.5）
    public static void DrawBoxes(Graphics g, IEnumerable<SnipBox> boxes, float scale)
    {
        var pen = new Pen(Red, Math.Max(2f, 3f * scale));
        using var font = new Font("Segoe UI", 11f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        foreach (var b in boxes)
        {
            g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
            var label = b.Number.ToString();
            var size = g.MeasureString(label, font);
            var lw = Math.Max(size.Width + 6 * scale, 18 * scale); var lh = size.Height + 2 * scale;
            var lx = b.X; var ly = b.Y - lh >= 0 ? b.Y - lh : b.Y;   // 枠の左上の外。上に余白が無ければ内側
            using var bg = new SolidBrush(Red);
            g.FillRectangle(bg, lx, ly, lw, lh);
            g.DrawString(label, font, Brushes.White, lx + (lw - size.Width) / 2, ly + (lh - size.Height) / 2);
        }
        pen.Dispose();
    }

    public static Bitmap Bake(Bitmap screen, Rectangle area, IEnumerable<SnipBox> boxes, float scale) =>
        Bake(screen, area, boxes, Array.Empty<SnipMark>(), scale);

    public static Bitmap Bake(Bitmap screen, Rectangle area, IEnumerable<SnipBox> boxes, IEnumerable<SnipMark> marks, float scale)
    {
        var bmp = Crop(screen, area);
        using (var g = Graphics.FromImage(bmp)) { DrawMarks(g, marks, scale); DrawBoxes(g, boxes, scale); }
        return bmp;
    }

    // 矢印（線と先端の三角）とペン（点をつないだ線）。R-17
    public static void DrawMarks(Graphics g, IEnumerable<SnipMark> marks, float scale)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Red, Math.Max(2f, 3f * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(Red);
        foreach (var m in marks)
        {
            if (m.Points.Count < 2) continue;
            if (m.Kind == SketchKind.Arrow)
            {
                PointF a = m.Points[0], b = m.Points[^1];
                var len = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (len < 1) continue;
                float ux = (b.X - a.X) / len, uy = (b.Y - a.Y) / len;
                var head = Math.Min(len, 14f * scale);
                var bx = b.X - ux * head; var by = b.Y - uy * head;
                g.DrawLine(pen, a.X, a.Y, bx, by);
                var w = head * 0.6f;
                g.FillPolygon(brush, new[] { b, new PointF(bx - uy * w, by + ux * w), new PointF(bx + uy * w, by - ux * w) });
            }
            else g.DrawLines(pen, m.Points.ToArray());
        }
    }
}
