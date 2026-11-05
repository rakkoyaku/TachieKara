using SkiaSharp;
using TachieKara.Models;

namespace TachieKara.Services;

/// <summary>表情グリッドの 1 マス。</summary>
public sealed record DisplayCell(string Path, string Label);

/// <summary>
/// 描画に使う画像の取り出し口。プレビューでは縮小版、書き出しでは原寸を返す。
/// scale は「返した画像のピクセル ÷ 元画像のピクセル」。
/// </summary>
public interface IDisplayImageSource
{
    SKBitmap? Get(string path, out double scale);
    /// <summary>Get で受け取った画像を使い終わったら呼ぶ（書き出しではここで解放する）。</summary>
    void Release(SKBitmap bitmap);
}

/// <summary>ディスプレイ画像を描くのに必要な素材。</summary>
public sealed class DisplayInput
{
    public required DisplaySpec Spec { get; init; }
    public required string SpritePath { get; init; }
    public required string GhostPath { get; init; }
    public required IReadOnlyList<DisplayCell> Cells { get; init; }
    /// <summary>ベース画像の大きさ（顔の枠はこの座標）。</summary>
    public required int BaseWidth { get; init; }
    public required int BaseHeight { get; init; }
    /// <summary>顔アイコンの枠（ベース画像のピクセル）。</summary>
    public required SKRect FaceCrop { get; init; }
}

/// <summary>ディスプレイ画像（立ち絵＋表情一覧）を描く。プレビューと書き出しで同じ処理を使う。</summary>
public static partial class DisplayRenderer
{
    /// <summary>出力される画像の大きさ（scale = 1 のとき）。</summary>
    public static (int W, int H) CanvasSize(DisplaySpec spec, int baseW, int baseH, int cellCount)
    {
        var w = Math.Clamp(spec.Width, 64, 16000);
        return (w, ComputeHeight(spec, w, baseW, baseH, cellCount));
    }

    static int ComputeHeight(DisplaySpec spec, double w, int baseW, int baseH, int cellCount)
    {
        if (TryRatio(spec.Aspect, out var r)) return Math.Max(16, (int)Math.Round(w / r));
        // 自動: 立ち絵がちょうど収まる高さ。立ち絵なしなら表情グリッドに合わせる
        var u = w / 1000.0;
        var m = spec.Margin * u;
        if (spec.ShowSprite && baseW > 0)
        {
            var areaW = w * spec.SpriteArea / 100.0 - m;
            var h = areaW * baseH / baseW + 2 * m;
            return (int)Math.Round(Math.Clamp(h, w * 0.3, w * 3));
        }
        var probe = LayoutGrid(spec, new SKRect((float)m, (float)m, (float)(w - m), (float)(w * 3)), cellCount, w, fitHeight: false);
        return (int)Math.Round(Math.Clamp(probe.TotalHeight + 2 * m, w * 0.2, w * 3));
    }

    public static SKBitmap Render(DisplayInput input, IDisplayImageSource src, double scale, CancellationToken ct = default)
    {
        var spec = input.Spec;
        var (fullW, fullH) = CanvasSize(spec, input.BaseWidth, input.BaseHeight, input.Cells.Count);
        int W = Math.Max(16, (int)Math.Round(fullW * scale));
        int H = Math.Max(16, (int)Math.Round(fullH * scale));
        double u = W / 1000.0;
        double m = spec.Margin * u;

        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        var full = new SKRect(0, 0, W, H);

        // 背景と、背景だけにかける効果（ざらつき・周辺減光・内枠）
        DrawBackground(canvas, spec, full, u, src);
        DrawOverlays(canvas, spec, full, u);
        ct.ThrowIfCancellationRequested();

        // 領域の分け方（spriteLeft = 立ち絵が左、表情の並びが右）
        bool spriteLeft = spec.SpriteSide != "right";
        float split = (float)(W * spec.SpriteArea / 100.0);
        SKRect spriteRegion, gridRegion;
        if (spec.ShowSprite)
        {
            spriteRegion = spriteLeft
                ? new SKRect((float)m, (float)m, split, (float)(H - m))
                : new SKRect(W - split, (float)m, (float)(W - m), (float)(H - m));
            gridRegion = spriteLeft
                ? new SKRect(split + (float)(m * 0.5), (float)m, (float)(W - m), (float)(H - m))
                : new SKRect((float)m, (float)m, W - split - (float)(m * 0.5), (float)(H - m));
        }
        else
        {
            spriteRegion = SKRect.Empty;
            gridRegion = new SKRect((float)m, (float)m, (float)(W - m), (float)(H - m));
        }
        var content = new SKRect((float)m, (float)m, (float)(W - m), (float)(H - m));

        // ---- タイトルの置き場所を決める ----
        var anchor = spec.TitleAnchor;
        bool anchorGrid = anchor is "grid" or "grid-below";
        bool cornerRight = anchor.EndsWith("right");
        bool cornerTop = anchor.StartsWith("top");
        string align = anchorGrid ? spec.TitleAlign : cornerRight ? "right" : "left";
        // 表情の並びの側にある角か（立ち絵の側なら場所を空けずに重ねる）
        bool onGridSide = !spec.ShowSprite || (spriteLeft ? cornerRight : !cornerRight);
        float tgap = TitleGap(spec, u);
        if (spec.ShowTitle && spec.TitleStyle == "watermark" && !spec.WatermarkFront)
            DrawWatermark(canvas, spec, content, spriteRegion, W, H);

        var block = BuildTitle(spec, u,
            anchorGrid ? gridRegion.Width : content.Width * (spec.ShowSprite && !onGridSide ? (float)(spec.SpriteArea / 100.0) : 1f),
            content.Height * 0.9f, align);
        SKPoint titlePos = default;
        float reserveTop = 0, reserveBottom = 0;
        if (block is not null)
        {
            if (block.Vertical)
            {
                // 縦書き: 画像の左右の端に沿わせる。並びの側の端なら、その分だけ並びを狭める
                bool rightEdge = anchorGrid ? spriteLeft || !spec.ShowSprite : cornerRight;
                float x = rightEdge ? content.Right - block.Width : content.Left;
                float y = anchorGrid ? content.MidY - block.Height / 2 : cornerTop ? content.Top : content.Bottom - block.Height;
                titlePos = new SKPoint(x, y);
                bool besideGrid = !spec.ShowSprite || (spriteLeft == rightEdge);
                if (besideGrid)
                {
                    if (rightEdge) gridRegion.Right = Math.Min(gridRegion.Right, x - tgap);
                    else gridRegion.Left = Math.Max(gridRegion.Left, x + block.Width + tgap);
                }
                else
                {
                    // 立ち絵の側の端なら、立ち絵を内側へ寄せて重ならないようにする
                    if (rightEdge) spriteRegion.Right = Math.Min(spriteRegion.Right, x - tgap);
                    else spriteRegion.Left = Math.Max(spriteRegion.Left, x + block.Width + tgap);
                }
            }
            else if (anchorGrid)
            {
                if (anchor == "grid") reserveTop = block.Height + tgap; else reserveBottom = block.Height + tgap;
            }
            else
            {
                float x = cornerRight ? content.Right - block.Width : content.Left;
                float y = cornerTop ? content.Top : content.Bottom - block.Height;
                titlePos = new SKPoint(x, y);
                // 並びの側なら並びを、立ち絵の側なら立ち絵を、タイトルの分だけ避けさせる
                ref var avoid = ref (onGridSide ? ref gridRegion : ref spriteRegion);
                if (cornerTop) avoid.Top = Math.Max(avoid.Top, y + block.Height + tgap);
                else avoid.Bottom = Math.Min(avoid.Bottom, y - tgap);
            }
        }

        // 奥の立ち絵 → 立ち絵
        if (spec.ShowSprite)
        {
            var fit = FitSprite(spec, spriteRegion, input, src);
            if (spec.Ghost && fit.Dst.Width > 0)
                DrawGhost(canvas, spec, fit, input, src, W, H);
            ct.ThrowIfCancellationRequested();
            if (fit.Dst.Width > 0)
                DrawSprite(canvas, spec, fit, input.SpritePath, src, u);
        }
        ct.ThrowIfCancellationRequested();

        // 表情の並び
        SKRect gridBounds = new(gridRegion.Left, gridRegion.Top + reserveTop, gridRegion.Right, gridRegion.Top + reserveTop);
        if (input.Cells.Count > 0 && gridRegion.Width > 8 && gridRegion.Height - reserveTop - reserveBottom > 8)
            gridBounds = DrawGrid(canvas, spec, gridRegion, input, src, W, u, ct, reserveTop, reserveBottom);

        // タイトル
        if (block is not null)
        {
            if (!block.Vertical && anchorGrid)
            {
                float x = gridBounds.Left + AlignX(align, gridBounds.Width, block.Width);
                x = Math.Clamp(x, content.Left, Math.Max(content.Left, content.Right - block.Width));
                float y = anchor == "grid" ? gridBounds.Top - tgap - block.Height : gridBounds.Bottom + tgap;
                titlePos = new SKPoint(x, y);
            }
            block.Draw(canvas, titlePos.X, titlePos.Y);
        }

        // 前面に出す透かし（立ち絵や表情の上に重なる）
        if (spec.ShowTitle && spec.TitleStyle == "watermark" && spec.WatermarkFront)
            DrawWatermark(canvas, spec, content, spriteRegion, W, H);

        // クレジット（いちばん上に、四隅のどこかへ小さく）
        if (spec.ShowCredit)
            DrawCredit(canvas, spec, W, H, u);

        canvas.Flush();
        return bmp;
    }

    // ================= 背景 =================

    static void DrawBackground(SKCanvas c, DisplaySpec s, SKRect r, double u, IDisplayImageSource src)
    {
        var c1 = Col(s.BgColor1, SKColors.White);
        var c2 = Col(s.BgColor2, SKColors.Gray);
        var c3 = Col(s.BgColor3, SKColors.White);
        float W = r.Width, H = r.Height;
        var center = new SKPoint(W / 2, H / 2);
        float diag = (float)Math.Sqrt(W * W + H * H);
        float alpha = (float)Math.Clamp(s.PatternOpacity / 100.0, 0, 1);
        c.Clear(c1);

        switch (s.BgKind)
        {
            case "gradient":
            {
                var (a, b) = GradientPoints(center, diag, s.BgAngle + 90);
                float mid = (float)Math.Clamp(0.5 + s.BgOffset / 100.0, 0.05, 0.95);
                using var shader = SKShader.CreateLinearGradient(a, b, [c1, Mix(c1, c2, 0.5f), c2], [0, mid, 1], SKShaderTileMode.Clamp);
                using var p = new SKPaint { Shader = shader };
                c.DrawRect(r, p);
                break;
            }
            case "radial":
            {
                var cp = new SKPoint(center.X + (float)(s.BgOffset / 100.0 * W), center.Y);
                float radius = (float)Math.Max(1, diag * s.BgSize / 100.0);
                using var shader = SKShader.CreateRadialGradient(cp, radius, [c2, c1], [0, 1], SKShaderTileMode.Clamp);
                using var p = new SKPaint { Shader = shader };
                c.DrawRect(r, p);
                break;
            }
            case "band":
            {
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                float bandH = (float)(H * s.BgSize / 100.0);
                float cy = center.Y + (float)(s.BgOffset / 100.0 * H);
                var band = new SKRect(center.X - diag, cy - bandH / 2, center.X + diag, cy + bandH / 2);
                using (var p = new SKPaint { Color = c2, IsAntialias = true }) c.DrawRect(band, p);
                if (s.BgEdgeLines)
                {
                    float lw = (float)(5 * u), gap = (float)(9 * u);
                    using var lp = new SKPaint { Color = c3, IsAntialias = true, StrokeWidth = lw, Style = SKPaintStyle.Stroke };
                    using var dp = new SKPaint { Color = c2.WithAlpha(200), IsAntialias = true, StrokeWidth = lw * 0.6f, Style = SKPaintStyle.Stroke };
                    c.DrawLine(band.Left, band.Top - gap, band.Right, band.Top - gap, lp);
                    c.DrawLine(band.Left, band.Bottom + gap, band.Right, band.Bottom + gap, lp);
                    c.DrawLine(band.Left, band.Top - gap * 2.2f, band.Right, band.Top - gap * 2.2f, dp);
                    c.DrawLine(band.Left, band.Bottom + gap * 2.2f, band.Right, band.Bottom + gap * 2.2f, dp);
                }
                c.Restore();
                break;
            }
            case "split":
            {
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                float x = center.X + (float)(s.BgOffset / 100.0 * W);
                using (var p = new SKPaint { Color = c2, IsAntialias = true })
                    c.DrawRect(new SKRect(x, center.Y - diag, center.X + diag, center.Y + diag), p);
                if (s.BgEdgeLines)
                {
                    using var lp = new SKPaint { Color = c3, IsAntialias = true, StrokeWidth = (float)(6 * u), Style = SKPaintStyle.Stroke };
                    c.DrawLine(x, center.Y - diag, x, center.Y + diag, lp);
                }
                c.Restore();
                break;
            }
            case "stripes":
            {
                float period = (float)Math.Max(4 * u, s.BgSize * u);
                c.Save();
                c.RotateDegrees((float)s.BgAngle + 45, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true };
                for (float x = center.X - diag; x < center.X + diag; x += period)
                    c.DrawRect(new SKRect(x, center.Y - diag, x + period / 2, center.Y + diag), p);
                c.Restore();
                break;
            }
            case "dots":
            {
                float sp = (float)Math.Max(6 * u, s.BgSize * u);
                float rad = sp * 0.17f;
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true };
                int row = 0;
                for (float y = center.Y - diag; y < center.Y + diag; y += sp * 0.5f, row++)
                    for (float x = center.X - diag + (row % 2) * sp / 2; x < center.X + diag; x += sp)
                        c.DrawCircle(x, y, rad, p);
                c.Restore();
                break;
            }
            case "grid":
            {
                float sp = (float)Math.Max(6 * u, s.BgSize * u * 0.5);
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var thin = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), StrokeWidth = (float)Math.Max(1, 1.2 * u), IsAntialias = true };
                using var thick = new SKPaint { Color = c2.WithAlpha((byte)(Math.Min(1, alpha * 1.8) * 255)), StrokeWidth = (float)Math.Max(1, 2.2 * u), IsAntialias = true };
                int i = 0;
                for (float x = center.X; x < center.X + diag; x += sp, i++)
                {
                    var p = i % 5 == 0 ? thick : thin;
                    c.DrawLine(x, center.Y - diag, x, center.Y + diag, p);
                    c.DrawLine(2 * center.X - x, center.Y - diag, 2 * center.X - x, center.Y + diag, p);
                }
                i = 0;
                for (float y = center.Y; y < center.Y + diag; y += sp, i++)
                {
                    var p = i % 5 == 0 ? thick : thin;
                    c.DrawLine(center.X - diag, y, center.X + diag, y, p);
                    c.DrawLine(center.X - diag, 2 * center.Y - y, center.X + diag, 2 * center.Y - y, p);
                }
                c.Restore();
                break;
            }
            case "checker":
            {
                float sp = (float)Math.Max(6 * u, s.BgSize * u);
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)) };
                int n = (int)(diag / sp) + 2;
                for (int iy = -n; iy < n; iy++)
                    for (int ix = -n; ix < n; ix++)
                        if (((ix + iy) & 1) == 0)
                            c.DrawRect(SKRect.Create(center.X + ix * sp, center.Y + iy * sp, sp, sp), p);
                c.Restore();
                break;
            }
            case "sunburst":
            {
                // 中心から広がる光の筋（明暗の扇形を交互に）
                var cp = new SKPoint(center.X + (float)(s.BgOffset / 100.0 * W), center.Y);
                int rays = Math.Clamp((int)Math.Round(s.BgSize / 3), 8, 60) * 2;
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true };
                using var path = new SKPath();
                for (int i = 0; i < rays; i += 2)
                {
                    double a0 = (s.BgAngle + 360.0 * i / rays) * Math.PI / 180, a1 = (s.BgAngle + 360.0 * (i + 1) / rays) * Math.PI / 180;
                    path.MoveTo(cp);
                    path.LineTo(cp.X + (float)Math.Cos(a0) * diag, cp.Y + (float)Math.Sin(a0) * diag);
                    path.LineTo(cp.X + (float)Math.Cos(a1) * diag, cp.Y + (float)Math.Sin(a1) * diag);
                    path.Close();
                }
                c.DrawPath(path, p);
                // 中心をやわらかく明るく
                using var glow = SKShader.CreateRadialGradient(cp, diag * 0.35f, [c1.WithAlpha(200), c1.WithAlpha(0)], [0, 1], SKShaderTileMode.Clamp);
                using var gp = new SKPaint { Shader = glow };
                c.DrawRect(r, gp);
                break;
            }
            case "halftone":
            {
                // 角度の方向に向かって点が大きくなる網点
                float sp = (float)Math.Max(6 * u, s.BgSize * u * 0.6);
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true };
                float span = diag;
                float start = center.X - span / 2 + (float)(s.BgOffset / 100.0 * span);
                int row = 0;
                for (float y = center.Y - diag / 2; y < center.Y + diag / 2; y += sp * 0.866f, row++)
                    for (float x = center.X - diag / 2 + (row % 2) * sp / 2; x < center.X + diag / 2; x += sp)
                    {
                        float t = Math.Clamp((x - start) / (span * 0.75f), 0, 1);
                        float rad = sp * 0.5f * t;
                        if (rad > 0.4f) c.DrawCircle(x, y, rad, p);
                    }
                c.Restore();
                break;
            }
            case "gingham":
            {
                // 縦横の半透明の帯を重ねたギンガムチェック
                float sp = (float)Math.Max(6 * u, s.BgSize * u);
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 0.6f * 255)), IsAntialias = true };
                for (float x = center.X - diag; x < center.X + diag; x += sp)
                    c.DrawRect(new SKRect(x, center.Y - diag, x + sp / 2, center.Y + diag), p);
                for (float y = center.Y - diag; y < center.Y + diag; y += sp)
                    c.DrawRect(new SKRect(center.X - diag, y, center.X + diag, y + sp / 2), p);
                c.Restore();
                break;
            }
            case "waves":
            {
                // 波線の帯
                float sp = (float)Math.Max(8 * u, s.BgSize * u);
                float amp = sp * 0.28f, wl = sp * 3.2f;
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = sp * 0.32f, StrokeCap = SKStrokeCap.Round };
                for (float y = center.Y - diag; y < center.Y + diag; y += sp)
                {
                    using var path = new SKPath();
                    path.MoveTo(center.X - diag, y);
                    for (float x = center.X - diag; x < center.X + diag; x += wl / 2)
                        path.QuadTo(x + wl / 4, y + ((int)((x - center.X + diag) / (wl / 2)) % 2 == 0 ? -amp : amp) * 2, x + wl / 2, y);
                    c.DrawPath(path, p);
                }
                c.Restore();
                break;
            }
            case "hex":
            {
                // ハニカム（六角形の線）
                float rad = (float)Math.Max(6 * u, s.BgSize * u * 0.6);
                float hw = rad * 1.732f, vh = rad * 1.5f;
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(1, 2.4 * u) };
                int row = 0;
                for (float y = center.Y - diag; y < center.Y + diag; y += vh, row++)
                    for (float x = center.X - diag + (row % 2) * hw / 2; x < center.X + diag; x += hw)
                    {
                        using var path = new SKPath();
                        for (int k = 0; k < 6; k++)
                        {
                            double a = (60 * k - 30) * Math.PI / 180;
                            var pt = new SKPoint(x + rad * (float)Math.Cos(a), y + rad * (float)Math.Sin(a));
                            if (k == 0) path.MoveTo(pt); else path.LineTo(pt);
                        }
                        path.Close();
                        c.DrawPath(path, p);
                    }
                c.Restore();
                break;
            }
            case "stars":
            {
                // ちりばめた星（四方に光るきらめき）と小さな点。毎回同じ配置になるよう乱数の種を固定
                var rnd = new Random(1234);
                float unit = (float)Math.Max(4 * u, s.BgSize * u * 0.5);
                int count = (int)(W * H / (unit * unit * 18));
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true };
                for (int i = 0; i < count; i++)
                {
                    float x = (float)rnd.NextDouble() * W, y = (float)rnd.NextDouble() * H;
                    float sz = unit * (0.3f + (float)Math.Pow(rnd.NextDouble(), 3) * 1.4f);
                    if (rnd.NextDouble() < 0.55) { c.DrawCircle(x, y, sz * 0.12f, p); continue; }
                    using var path = new SKPath();
                    path.MoveTo(x, y - sz);
                    path.QuadTo(x, y, x + sz * 0.7f, y);
                    path.QuadTo(x, y, x, y + sz);
                    path.QuadTo(x, y, x - sz * 0.7f, y);
                    path.QuadTo(x, y, x, y - sz);
                    path.Close();
                    c.DrawPath(path, p);
                }
                break;
            }
            case "circles":
            {
                // 同心円
                var cp = new SKPoint(center.X + (float)(s.BgOffset / 100.0 * W), center.Y);
                float sp = (float)Math.Max(6 * u, s.BgSize * u);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = sp * 0.3f };
                for (float rr = sp; rr < diag; rr += sp) c.DrawCircle(cp, rr, p);
                break;
            }
            case "chevron":
            {
                // ジグザグの帯
                float sp = (float)Math.Max(8 * u, s.BgSize * u);
                c.Save();
                c.RotateDegrees((float)s.BgAngle, center.X, center.Y);
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = sp * 0.3f, StrokeJoin = SKStrokeJoin.Miter };
                for (float y = center.Y - diag; y < center.Y + diag; y += sp)
                {
                    using var path = new SKPath();
                    bool up = true;
                    path.MoveTo(center.X - diag, y);
                    for (float x = center.X - diag; x < center.X + diag; x += sp)
                    {
                        path.LineTo(x + sp, y + (up ? -sp * 0.5f : 0));
                        up = !up;
                    }
                    c.DrawPath(path, p);
                }
                c.Restore();
                break;
            }
            case "speedlines":
            {
                // 集中線: 中心のまわりを空けて、外側から細い楔を差し込む
                var cp = new SKPoint(center.X + (float)(s.BgOffset / 100.0 * W), center.Y);
                var rnd = new Random(42);
                int lines = Math.Clamp((int)(s.BgSize * 2.4), 40, 400);
                float inner = Math.Min(W, H) * 0.32f;
                using var p = new SKPaint { Color = c2.WithAlpha((byte)(alpha * 255)), IsAntialias = true };
                for (int i = 0; i < lines; i++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2;
                    double wdt = (0.004 + rnd.NextDouble() * 0.012);
                    float start = inner * (0.8f + (float)rnd.NextDouble() * 0.9f);
                    var tip = new SKPoint(cp.X + (float)Math.Cos(a) * start, cp.Y + (float)Math.Sin(a) * start);
                    using var path = new SKPath();
                    path.MoveTo(tip);
                    path.LineTo(cp.X + (float)Math.Cos(a - wdt) * diag, cp.Y + (float)Math.Sin(a - wdt) * diag);
                    path.LineTo(cp.X + (float)Math.Cos(a + wdt) * diag, cp.Y + (float)Math.Sin(a + wdt) * diag);
                    path.Close();
                    c.DrawPath(path, p);
                }
                break;
            }
            case "image":
            {
                if (string.IsNullOrEmpty(s.BgImagePath)) break;
                var img = src.Get(s.BgImagePath, out _);
                if (img is null) break;
                try
                {
                    // 全体を覆うように拡大（はみ出しは切る）
                    float k = Math.Max(W / (float)img.Width, H / (float)img.Height);
                    float dw = img.Width * k, dh = img.Height * k;
                    var dst = SKRect.Create((W - dw) / 2, (H - dh) / 2, dw, dh);
                    using var image = SKImage.FromBitmap(img);
                    using var p = new SKPaint();
                    if (s.BgImageBlur > 0)
                        p.ImageFilter = SKImageFilter.CreateBlur((float)(s.BgImageBlur * u), (float)(s.BgImageBlur * u), SKShaderTileMode.Clamp);
                    c.DrawImage(image, dst, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
                }
                finally { src.Release(img); }
                if (s.BgImageDim > 0)
                {
                    using var dim = new SKPaint { Color = SKColors.Black.WithAlpha((byte)(Math.Clamp(s.BgImageDim, 0, 100) * 2.55)) };
                    c.DrawRect(r, dim);
                }
                break;
            }
        }
    }

    static void DrawOverlays(SKCanvas c, DisplaySpec s, SKRect r, double u)
    {
        if (s.Grain && s.GrainAmount > 0)
        {
            // 細かいざらつき（明暗のみ）
            using var noise = SKShader.CreatePerlinNoiseFractalNoise(0.85f, 0.85f, 2, 7);
            using var gray = SKColorFilter.CreateColorMatrix(
            [
                0.33f, 0.33f, 0.33f, 0, 0,
                0.33f, 0.33f, 0.33f, 0, 0,
                0.33f, 0.33f, 0.33f, 0, 0,
                0, 0, 0, 0, (float)Math.Clamp(s.GrainAmount / 100.0, 0, 1),
            ]);
            using var p = new SKPaint { Shader = noise, ColorFilter = gray, BlendMode = SKBlendMode.Overlay };
            c.DrawRect(r, p);
        }
        if (s.Vignette)
        {
            var center = new SKPoint(r.MidX, r.MidY);
            float rad = (float)Math.Sqrt(r.Width * r.Width + r.Height * r.Height) / 2;
            using var shader = SKShader.CreateRadialGradient(center, rad,
                [SKColors.Transparent, SKColors.Transparent, SKColors.Black.WithAlpha(90)], [0, 0.55f, 1], SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader };
            c.DrawRect(r, p);
        }
        if (s.InnerFrame && s.InnerFrameWidth > 0)
        {
            float inset = (float)(s.InnerFrameInset * u), lw = (float)(s.InnerFrameWidth * u);
            var fr = new SKRect(r.Left + inset, r.Top + inset, r.Right - inset, r.Bottom - inset);
            using var p = new SKPaint { Color = Col(s.InnerFrameColor, SKColors.White), Style = SKPaintStyle.Stroke, StrokeWidth = lw, IsAntialias = true };
            float rad = (float)(s.CornerRadius * u * 1.5);
            c.DrawRoundRect(fr, rad, rad, p);
        }
    }

    // ================= 立ち絵 =================

    /// <summary>立ち絵の置き方。Dst は出力上の矩形、Trim は元画像（ベース座標）で使う範囲。</summary>
    readonly record struct SpriteFit(SKRect Dst, SKRect Trim);

    sealed record BoundsBox(SKRectI Rect);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SKBitmap, BoundsBox> BoundsCache = new();

    /// <summary>画像の中で透明でない範囲（その画像のピクセル）。同じ画像は覚えておく。</summary>
    static SKRectI OpaqueBounds(SKBitmap bmp)
    {
        if (BoundsCache.TryGetValue(bmp, out var cached)) return cached.Rect;
        var r = ImageOps.AlphaBounds(bmp, 8);
        BoundsCache.AddOrUpdate(bmp, new BoundsBox(r));
        return r;
    }

    /// <summary>使う範囲（ベース座標）。トリミングしない設定なら画像全体。</summary>
    static SKRect UsedRect(DisplaySpec s, SKBitmap img, double k)
    {
        var whole = new SKRect(0, 0, (float)(img.Width / k), (float)(img.Height / k));
        if (!s.SpriteTrim) return whole;
        var b = OpaqueBounds(img);
        if (b.Width <= 0 || b.Height <= 0) return whole;
        // 縮小画像で測った端は 1px ずれることがあるので、少しだけ外側に広げる
        float pad = (float)(1 / k);
        var r = new SKRect((float)(b.Left / k) - pad, (float)(b.Top / k) - pad, (float)(b.Right / k) + pad, (float)(b.Bottom / k) + pad);
        r.Intersect(whole);
        return r;
    }

    static SpriteFit FitSprite(DisplaySpec s, SKRect region, DisplayInput input, IDisplayImageSource src)
    {
        var img = src.Get(input.SpritePath, out var k);
        if (img is null) return default;
        SKRect used;
        try { used = UsedRect(s, img, k); }
        finally { src.Release(img); }
        if (used.Width <= 0 || used.Height <= 0) return default;

        double fit = Math.Min(region.Width / used.Width, region.Height / used.Height) * s.SpriteScale / 100.0;
        float dw = (float)(used.Width * fit), dh = (float)(used.Height * fit);
        float x = region.MidX - dw / 2 + (float)(s.SpriteOffsetX / 100.0 * region.Width);
        float y = (s.SpriteAlign == "center" ? region.MidY - dh / 2 : region.Bottom - dh)
                  + (float)(s.SpriteOffsetY / 100.0 * region.Height);
        return new SpriteFit(SKRect.Create(x, y, dw, dh), used);
    }

    static void DrawSprite(SKCanvas c, DisplaySpec s, SpriteFit fit, string path, IDisplayImageSource src, double u)
    {
        var img = src.Get(path, out var k);
        if (img is null) return;
        var dst = fit.Dst;
        SKBitmap scaled;
        try
        {
            int w = Math.Max(1, (int)Math.Round(dst.Width)), h = Math.Max(1, (int)Math.Round(dst.Height));
            var t = fit.Trim;
            scaled = ImageOps.Resample(img, new SKRect((float)(t.Left * k), (float)(t.Top * k), (float)(t.Right * k), (float)(t.Bottom * k)), w, h, null);
        }
        finally { src.Release(img); }

        using (scaled)
        {
            float x = (float)Math.Round(dst.Left), y = (float)Math.Round(dst.Top);
            SKImageFilter? silhouette = null;
            if (s.SpriteOutline && s.SpriteOutlineWidth > 0)
                silhouette = OutlineFilter(s, u);
            try
            {
                if (s.SpriteShadow && s.SpriteShadowOpacity > 0)
                {
                    float d = (float)(s.SpriteShadowDistance * u), blur = (float)Math.Max(0.1, s.SpriteShadowBlur * u);
                    var col = Col(s.SpriteShadowColor, SKColors.Black).WithAlpha((byte)(Math.Clamp(s.SpriteShadowOpacity, 0, 100) * 2.55));
                    using var shadow = SKImageFilter.CreateDropShadowOnly(d, d, blur, blur, col, silhouette);
                    using var p = new SKPaint { ImageFilter = shadow };
                    c.DrawBitmap(scaled, x, y, p);
                }
                if (silhouette is not null)
                {
                    using var p = new SKPaint { ImageFilter = silhouette };
                    c.DrawBitmap(scaled, x, y, p);
                }
                c.DrawBitmap(scaled, x, y);
            }
            finally { silhouette?.Dispose(); }
        }
    }

    /// <summary>
    /// フチの形を作るフィルター。
    /// 1) 輪郭を「太さ＋なめらかさ」だけ太らせる → 2) なめらかさの分だけ細くし直す（毛先のすき間などの小さなくぼみが埋まる）
    /// → 3) ぼかしてから半分の濃さで切り直す（細かいトゲが丸まる）。切り直しの急さで、縁のアンチエイリアスの有無を変える。
    /// </summary>
    static SKImageFilter OutlineFilter(DisplaySpec s, double u)
    {
        float r = (float)(s.SpriteOutlineWidth * u);
        float smooth = (float)(Math.Max(0, s.SpriteOutlineSmooth) * u);
        bool crisp = s.SpriteOutlineEdge == "crisp";
        var color = Col(s.SpriteOutlineColor, SKColors.White);

        SKImageFilter shape = SKImageFilter.CreateDilate(r + smooth, r + smooth);
        if (smooth > 0.5f)
        {
            var closed = SKImageFilter.CreateErode(smooth, smooth, shape);
            shape.Dispose();
            shape = closed;
        }
        // ぼかし: なめらかにするときは大きめ、しないときも縁を整える程度にかける
        float sigma = Math.Max((float)Math.Max(0.6, u * 0.7), smooth * 0.55f);
        var blurred = SKImageFilter.CreateBlur(sigma, sigma, shape);
        shape.Dispose();
        // 半分の濃さを境に切り直す。アンチエイリアスは境の前後をなだらかに、くっきりは段差に
        // ぼかした縁の傾きは約 1/(2.5σ)。k = 2.1σ にすると、なだらかな部分が約 1.2px に収まる
        float k = crisp ? 255f : Math.Clamp(2.1f * sigma, 2f, 200f);
        using var threshold = SKColorFilter.CreateColorMatrix(
        [
            0, 0, 0, 0, 1,
            0, 0, 0, 0, 1,
            0, 0, 0, 0, 1,
            0, 0, 0, k, 0.5f - k * 0.5f,
        ]);
        var cut = SKImageFilter.CreateColorFilter(threshold, blurred);
        blurred.Dispose();
        using var tint = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn);
        var result = SKImageFilter.CreateColorFilter(tint, cut);
        cut.Dispose();
        return result;
    }

    static void DrawGhost(SKCanvas c, DisplaySpec s, SpriteFit fit, DisplayInput input, IDisplayImageSource src, int W, int H)
    {
        var path = string.IsNullOrEmpty(input.GhostPath) ? input.SpritePath : input.GhostPath;
        var img = src.Get(path, out var k);
        if (img is null) return;
        try
        {
            // 立ち絵（大きさ 100% のとき）を基準に拡大し、中心からずらして置く
            var used = UsedRect(s, img, k);
            double baseScale = fit.Dst.Height / fit.Trim.Height * 100.0 / Math.Max(1, s.SpriteScale);
            double gk = baseScale * s.GhostScale / 100.0;
            float dw = (float)(used.Width * gk), dh = (float)(used.Height * gk);
            float cx = fit.Dst.MidX + (float)(s.GhostOffsetX / 100.0 * W);
            float cy = fit.Dst.MidY + (float)(s.GhostOffsetY / 100.0 * H);
            var dst = SKRect.Create(cx - dw / 2, cy - dh / 2, dw, dh);
            // 見える部分だけ縮小して描く（大きく拡大すると重いので）
            var visible = SKRect.Intersect(dst, new SKRect(0, 0, W, H));
            if (visible.IsEmpty) return;
            float ux = (float)(used.Left * k), uy = (float)(used.Top * k), uw = (float)(used.Width * k), uh = (float)(used.Height * k);
            var srcRect = new SKRect(
                ux + (visible.Left - dst.Left) / dw * uw, uy + (visible.Top - dst.Top) / dh * uh,
                ux + (visible.Right - dst.Left) / dw * uw, uy + (visible.Bottom - dst.Top) / dh * uh);
            int vw = Math.Max(1, (int)Math.Round(visible.Width)), vh = Math.Max(1, (int)Math.Round(visible.Height));
            using var part = ImageOps.Resample(img, srcRect, vw, vh, null);
            using var p = new SKPaint { Color = SKColors.White.WithAlpha((byte)(Math.Clamp(s.GhostOpacity, 0, 100) * 2.55)) };
            c.DrawBitmap(part, visible.Left, visible.Top, p);
        }
        finally { src.Release(img); }
    }

    // ================= 表情グリッド =================

    readonly record struct GridLayout(int Columns, int Rows, float CellW, float CellH, float LabelH, float Gap, float TotalWidth, float TotalHeight);

    static float LabelHeight(DisplaySpec s, float cellW) =>
        s.LabelMode == "below" ? cellW * (float)(s.LabelSize / 100.0 * 1.3 + 0.06) : 0;

    static GridLayout LayoutGrid(DisplaySpec s, SKRect region, int n, double canvasW, bool fitHeight = true)
    {
        double u = canvasW / 1000.0;
        float gap = (float)(s.CellGap * u);
        double a = TryRatio(s.CellAspect, out var ar) ? ar : 1;
        double labelK = s.LabelMode == "below" ? s.LabelSize / 100.0 * 1.3 + 0.06 : 0;
        double maxCell = s.CellMax > 0 ? canvasW * s.CellMax / 100.0 : double.MaxValue;

        GridLayout Make(int cols)
        {
            int rows = (int)Math.Ceiling(n / (double)cols);
            double byW = (region.Width - gap * (cols - 1)) / cols;
            double byH = fitHeight ? (region.Height - gap * (rows - 1)) / (rows * (1 / a + labelK)) : double.MaxValue;
            float cw = (float)Math.Max(1, Math.Min(Math.Min(byW, byH), maxCell));
            float ch = (float)(cw / a);
            float lh = (float)(cw * labelK);
            return new GridLayout(cols, rows, cw, ch, lh, gap,
                cols * cw + (cols - 1) * gap, rows * (ch + lh) + (rows - 1) * gap);
        }

        if (s.Columns > 0) return Make(Math.Min(s.Columns, Math.Max(1, n)));
        GridLayout best = Make(1);
        double regionRatio = region.Width / Math.Max(1, region.Height);
        for (int cols = 2; cols <= Math.Max(1, n); cols++)
        {
            var g = Make(cols);
            if (g.CellW > best.CellW + 0.5f) best = g;
            else if (Math.Abs(g.CellW - best.CellW) <= 0.5f)
            {
                // 同じ大きさなら、領域の形に近い並びを選ぶ
                double d1 = Math.Abs(Math.Log(best.TotalWidth / Math.Max(1, best.TotalHeight) / regionRatio));
                double d2 = Math.Abs(Math.Log(g.TotalWidth / Math.Max(1, g.TotalHeight) / regionRatio));
                if (d2 < d1) best = g;
            }
        }
        return best;
    }

    /// <summary>表情の並びを描き、描いた範囲を返す。reserveTop/Bottom はタイトル用に上下へ空ける高さ。</summary>
    static SKRect DrawGrid(SKCanvas c, DisplaySpec s, SKRect region, DisplayInput input, IDisplayImageSource src, int W, double u,
        CancellationToken ct, float reserveTop, float reserveBottom)
    {
        var cells = input.Cells;
        var inner = new SKRect(region.Left, region.Top + reserveTop, region.Right, region.Bottom - reserveBottom);
        var g = LayoutGrid(s, inner, cells.Count, W);
        float ox = region.MidX - g.TotalWidth / 2;
        // タイトルと並びをひとかたまりとして揃える
        float blockH = reserveTop + g.TotalHeight + reserveBottom;
        float blockTop = s.GridAlign switch
        {
            "top" => region.Top,
            "bottom" => region.Bottom - blockH,
            _ => region.MidY - blockH / 2,
        };
        float oy = blockTop + reserveTop;
        float radius = (float)(s.CornerRadius * u);

        if (s.GridCard)
        {
            float pad = Math.Max(g.Gap * 1.4f, (float)(16 * u));
            var card = new SKRect(ox - pad, oy - pad, ox + g.TotalWidth + pad, oy + g.TotalHeight + pad);
            using var p = new SKPaint
            {
                Color = Col(s.GridCardColor, SKColors.White).WithAlpha((byte)(Math.Clamp(s.GridCardOpacity, 0, 100) * 2.55)),
                IsAntialias = true,
            };
            c.DrawRoundRect(card, radius * 1.6f + pad * 0.3f, radius * 1.6f + pad * 0.3f, p);
        }

        // 顔の枠をセルの縦横比に広げた切り抜き範囲（ベース画像の座標）
        double a = TryRatio(s.CellAspect, out var ar) ? ar : 1;
        var face = input.FaceCrop;
        double size = face.Width * s.CellZoom / 100.0;
        double cropW = a >= 1 ? size * a : size, cropH = a >= 1 ? size : size / a;
        var crop = SKRect.Create((float)(face.MidX - cropW / 2), (float)(face.MidY - cropH / 2), (float)cropW, (float)cropH);

        var cellBg = Col(s.CellBg, SKColors.Transparent);
        var frameColor = Col(s.FrameColor, SKColors.Black);
        float fw = (float)(s.FrameWidth * u);

        for (int i = 0; i < cells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            int row = i / g.Columns, col = i % g.Columns;
            float x = ox + col * (g.CellW + g.Gap);
            float y = oy + row * (g.CellH + g.LabelH + g.Gap);
            var rect = SKRect.Create(x, y, g.CellW, g.CellH);
            var rr = new SKRoundRect(rect, radius);

            if (s.CellShadow)
            {
                using var sp = new SKPaint
                {
                    Color = SKColors.Black,
                    ImageFilter = SKImageFilter.CreateDropShadowOnly(0, (float)(4 * u), (float)(6 * u), (float)(6 * u), SKColors.Black.WithAlpha(70)),
                    IsAntialias = true,
                };
                c.DrawRoundRect(rr, sp);
            }

            c.Save();
            c.ClipRoundRect(rr, SKClipOperation.Intersect, true);
            if (cellBg.Alpha > 0)
                using (var bp = new SKPaint { Color = cellBg }) c.DrawRect(rect, bp);

            var img = src.Get(cells[i].Path, out var k);
            if (img is not null)
            {
                try
                {
                    var srcRect = new SKRect((float)(crop.Left * k), (float)(crop.Top * k), (float)(crop.Right * k), (float)(crop.Bottom * k));
                    int cw = Math.Max(1, (int)Math.Round(g.CellW)), ch = Math.Max(1, (int)Math.Round(g.CellH));
                    using var face2 = ImageOps.Resample(img, srcRect, cw, ch, null);
                    c.DrawBitmap(face2, SKRect.Create(x, y, g.CellW, g.CellH));
                }
                finally { src.Release(img); }
            }

            if (s.LabelMode == "inside")
                DrawLabelInside(c, s, rect, LabelText(s, i, cells[i].Label), g.CellW);
            c.Restore();

            if (fw > 0)
            {
                using var fp = new SKPaint { Color = frameColor, Style = SKPaintStyle.Stroke, StrokeWidth = fw, IsAntialias = true };
                var frameRect = rect;
                frameRect.Inflate(-fw / 2, -fw / 2);
                float ir = Math.Max(0, radius - fw / 2);
                c.DrawRoundRect(frameRect, ir, ir, fp);
            }

            if (s.LabelMode == "below")
                DrawLabelBelow(c, s, rect, LabelText(s, i, cells[i].Label), g.CellW, g.Gap);
        }
        return SKRect.Create(ox, oy, g.TotalWidth, g.TotalHeight);
    }

    static string LabelText(DisplaySpec s, int index, string label)
    {
        if (!s.LabelLetters) return label;
        var letter = index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();
        return string.IsNullOrWhiteSpace(label) ? letter : $"{letter}  {label}";
    }

    static void DrawLabelBelow(SKCanvas c, DisplaySpec s, SKRect cell, string text, float cellW, float gap)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        float size = cellW * (float)(s.LabelSize / 100.0);
        using var font = MakeFont(s.LabelFont, s.LabelBold, size, text);
        FitFontWidth(font, text, cellW + gap * 0.8f);
        float y = cell.Bottom + cellW * 0.04f + font.Size * 0.95f;
        DrawText(c, text, cell.MidX, y, font, Col(s.LabelColor, SKColors.White),
            s.LabelStroke ? Col(s.LabelStrokeColor, SKColors.Black) : null, (float)(font.Size * s.LabelStrokeWidth / 100.0));
    }

    static void DrawLabelInside(SKCanvas c, DisplaySpec s, SKRect cell, string text, float cellW)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        float size = cellW * (float)(s.LabelSize / 100.0);
        using var font = MakeFont(s.LabelFont, s.LabelBold, size, text);
        FitFontWidth(font, text, cellW * 0.92f);
        float bandH = font.Size * 1.7f;
        var band = new SKRect(cell.Left, cell.Bottom - bandH, cell.Right, cell.Bottom);
        using (var bp = new SKPaint { Color = Col(s.LabelBandColor, new SKColor(0, 0, 0, 160)) }) c.DrawRect(band, bp);
        float y = band.MidY + font.Size * 0.36f;
        DrawText(c, text, cell.MidX, y, font, Col(s.LabelColor, SKColors.White),
            s.LabelStroke ? Col(s.LabelStrokeColor, SKColors.Black) : null, (float)(font.Size * s.LabelStrokeWidth / 100.0 * 0.6));
    }

    static void DrawText(SKCanvas c, string text, float cx, float baseline, SKFont font, SKColor fill, SKColor? stroke, float strokeWidth)
    {
        if (stroke is { } sc && strokeWidth > 0)
        {
            using var sp = new SKPaint
            {
                Color = sc, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth,
                StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round, IsAntialias = true,
            };
            c.DrawText(text, cx, baseline, SKTextAlign.Center, font, sp);
        }
        using var fp = new SKPaint { Color = fill, IsAntialias = true };
        c.DrawText(text, cx, baseline, SKTextAlign.Center, font, fp);
    }

    static void FitFontWidth(SKFont font, string text, float maxWidth)
    {
        var w = font.MeasureText(text);
        if (w > maxWidth && w > 0) font.Size = font.Size * maxWidth / w;
    }

    // ================= フォント =================

    /// <summary>OS の日本語フォント（指定がないときに使う）。</summary>
    public static string DefaultFontFamily => OperatingSystem.IsMacOS() ? "Hiragino Sans" : "Yu Gothic UI";

    static readonly Dictionary<(string, bool), SKTypeface> TypefaceCache = new();

    static SKFont MakeFont(string family, bool bold, float size, string sample)
    {
        var tf = ResolveTypeface(string.IsNullOrWhiteSpace(family) ? DefaultFontFamily : family, bold, sample);
        return new SKFont(tf, Math.Max(1, size)) { Edging = SKFontEdging.Antialias, Subpixel = true };
    }

    static SKTypeface ResolveTypeface(string family, bool bold, string sample)
    {
        SKTypeface tf;
        lock (TypefaceCache)
        {
            if (!TypefaceCache.TryGetValue((family, bold), out tf!))
            {
                var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
                tf = SKFontManager.Default.MatchFamily(family, style)
                     ?? SKFontManager.Default.MatchFamily(DefaultFontFamily, style)
                     ?? SKTypeface.Default;
                TypefaceCache[(family, bold)] = tf;
            }
        }
        // 選んだフォントに無い文字（日本語など）が含まれるときは、その文字を持つフォントに替える
        foreach (var ch in sample)
        {
            if (char.IsWhiteSpace(ch) || char.IsSurrogate(ch)) continue;
            if (!tf.ContainsGlyph(ch))
            {
                var alt = SKFontManager.Default.MatchCharacter(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal, ["ja-JP"], ch);
                if (alt is not null) return alt;
                break;
            }
        }
        return tf;
    }

    // ================= 小物 =================

    static SKColor Col(string? hex, SKColor fallback) => ImageOps.ParseColor(hex) ?? fallback;

    static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t), (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));

    static (SKPoint, SKPoint) GradientPoints(SKPoint center, float diag, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180;
        var dx = (float)(Math.Cos(rad) * diag / 2);
        var dy = (float)(Math.Sin(rad) * diag / 2);
        return (new SKPoint(center.X - dx, center.Y - dy), new SKPoint(center.X + dx, center.Y + dy));
    }

    /// <summary>"16:9" のような比を 幅÷高さ にする。</summary>
    public static bool TryRatio(string? text, out double ratio)
    {
        ratio = 1;
        var parts = (text ?? "").Split(':');
        if (parts.Length == 2 && double.TryParse(parts[0], out var a) && double.TryParse(parts[1], out var b) && a > 0 && b > 0)
        {
            ratio = a / b;
            return true;
        }
        return false;
    }
}
