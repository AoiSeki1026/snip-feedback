using System.Drawing;

namespace SnipFeedback.Core;

public enum SnipMode { Rect, Window }

// 赤枠（R-08）。矩形は切り取った画像の中の画素座標
public sealed record SnipBox(int Number, int X, int Y, int Width, int Height, string Comment);

// 矢印と線（R-17）。番号と一言は付けない。点は切り取った画像の中の画素座標（矢印は始点と終点の2点）
public enum SketchKind { Box, Arrow, Pen }
public sealed record SnipMark(SketchKind Kind, List<Point> Points);

// ためる一覧の1件（R-10・R-11）。OcrText は読み取った文字（R-18）、Url はブラウザのアドレス（R-19）
public sealed record SnipItem(string ImagePath, string Comment, List<SnipBox> Boxes, string AppName, string WindowTitle, DateTime TakenAt,
    string? OcrText = null, string? Url = null);
