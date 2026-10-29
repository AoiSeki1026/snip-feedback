using System.Drawing;

namespace SnipFeedback.Core;

// 編集画面で描いたもの（枠・矢印・線）を描いた順に持つ（R-08・R-17）。
// 右クリックで最後の1つを消す。枠の番号は残った枠の順に 1, 2, … と振り直す。座標は画面の画素座標
public sealed class Sketch
{
    public sealed class Item
    {
        public SketchKind Kind { get; init; }
        public Rectangle Box { get; init; }
        public List<Point> Points { get; init; } = new();
    }

    private readonly List<Item> _items = new();
    public IReadOnlyList<Item> Items => _items;

    public Item AddBox(Rectangle r) { var it = new Item { Kind = SketchKind.Box, Box = r }; _items.Add(it); return it; }
    public Item AddArrow(Point from, Point to) { var it = new Item { Kind = SketchKind.Arrow, Points = new() { from, to } }; _items.Add(it); return it; }
    public Item AddPen(IEnumerable<Point> points) { var it = new Item { Kind = SketchKind.Pen, Points = points.ToList() }; _items.Add(it); return it; }

    // 最後に描いたものを消して返す。何も無ければ null
    public Item? RemoveLast()
    {
        if (_items.Count == 0) return null;
        var it = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        return it;
    }

    public void Remove(Item it) => _items.Remove(it);

    // 枠の番号（1 始まり。描いた順）
    public int NumberOf(Item box) => _items.Where(i => i.Kind == SketchKind.Box).ToList().IndexOf(box) + 1;

    // 一言はここでは空。呼び出し側（編集画面）が欄の文字を入れる
    public List<SnipBox> Boxes => _items.Where(i => i.Kind == SketchKind.Box)
        .Select((b, k) => new SnipBox(k + 1, b.Box.X, b.Box.Y, b.Box.Width, b.Box.Height, "")).ToList();

    public List<SnipMark> Marks => _items.Where(i => i.Kind != SketchKind.Box)
        .Select(m => new SnipMark(m.Kind, m.Points.ToList())).ToList();
}
