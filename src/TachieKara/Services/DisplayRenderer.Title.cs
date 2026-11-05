using SkiaSharp;
using TachieKara.Models;

namespace TachieKara.Services;

/// <summary>タイトルの組み立て（スタイルごとの形と大きさ）と描画。</summary>
public static partial class DisplayRenderer
{
    /// <summary>組み立て済みのタイトル。Draw(canvas, 左上x, 左上y) で描く。</summary>
    sealed class TitleBlock
    {
        public float Width;
        public float Height;
        public bool Vertical;
        public required Action<SKCanvas, float, float> Draw;
    }

    static float TitleGap(DisplaySpec s, double u) => (float)(s.TitleSize * u * 0.2);

    /// <summary>タイトルを組み立てる。maxWidth を超えるときは文字を小さくする。</summary>
    static TitleBlock? BuildTitle(DisplaySpec s, double u, float maxWidth, float maxHeight, string align)
    {
        var text = s.TitleText?.Trim() ?? "";
        if (!s.ShowTitle || text.Length == 0 || s.TitleStyle == "watermark") return null;
        float size = (float)(s.TitleSize * u);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var block = s.TitleStyle switch
            {
                "plate" => PlateTitle(s, text, size, align),
                "bar" => BarTitle(s, text, size, align),
                "vertical" => VerticalTitle(s, text, size),
                _ => PlainTitle(s, text, size, align),
            };
            if ((block.Width <= maxWidth && block.Height <= maxHeight) || size < 6) return block;
            size *= Math.Min(maxWidth / block.Width, maxHeight / block.Height) * 0.98f;
        }
        return null;
    }

    // ---- 文字の部品 ----

    /// <summary>ぴったりの外形で測った文字。</summary>
    sealed class TextPiece : IDisposable
    {
        public required string Text;
        public required SKFont Font;
        public SKRect Bounds;        // 原点（ベースライン左端）からの外形
        public float Stroke;          // フチの太さ（外形はこの半分だけ広がる）
        public SKColor Fill;
        public SKColor? StrokeColor;
        public float W => Bounds.Width + Stroke;
        public float H => Bounds.Height + Stroke;

        /// <summary>外形の左上を (x, y) に合わせて描く。</summary>
        public void DrawAt(SKCanvas c, float x, float y)
        {
            float ox = x + Stroke / 2 - Bounds.Left, oy = y + Stroke / 2 - Bounds.Top;
            if (StrokeColor is { } sc && Stroke > 0)
            {
                using var sp = new SKPaint
                {
                    Color = sc, Style = SKPaintStyle.Stroke, StrokeWidth = Stroke,
                    StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round, IsAntialias = true,
                };
                c.DrawText(Text, ox, oy, SKTextAlign.Left, Font, sp);
            }
            using var fp = new SKPaint { Color = Fill, IsAntialias = true };
            c.DrawText(Text, ox, oy, SKTextAlign.Left, Font, fp);
        }

        public void Dispose() => Font.Dispose();
    }

    static TextPiece Piece(DisplaySpec s, string text, float size, bool bold, float strokeRatio, SKColor fill)
    {
        var font = MakeFont(s.TitleFont, bold, size, text);
        font.MeasureText(text, out var b);
        if (b.IsEmpty) b = new SKRect(0, -size * 0.8f, Math.Max(1, font.MeasureText(text)), size * 0.2f);
        bool stroke = s.TitleStroke && strokeRatio > 0;
        return new TextPiece
        {
            Text = text, Font = font, Bounds = b, Fill = fill,
            Stroke = stroke ? size * strokeRatio : 0,
            StrokeColor = stroke ? Col(s.TitleStrokeColor, SKColors.Black) : null,
        };
    }

    static float AlignX(string align, float boxW, float itemW) => align switch
    {
        "right" => boxW - itemW,
        "center" => (boxW - itemW) / 2,
        _ => 0,
    };

    static string SubtitleText(DisplaySpec s) => s.Subtitle?.Trim() ?? "";

    // ---- スタイル ----

    /// <summary>文字だけ（フチ付き）。小さな添え文字を下に。</summary>
    static TitleBlock PlainTitle(DisplaySpec s, string text, float size, string align)
    {
        var fill = Col(s.TitleColor, SKColors.White);
        var main = Piece(s, text, size, s.TitleBold, 0.16f, fill);
        var subText = SubtitleText(s);
        var sub = subText.Length > 0 ? Piece(s, subText, size * 0.4f, true, 0.2f, fill) : null;
        float gap = size * 0.12f;
        float w = Math.Max(main.W, sub?.W ?? 0);
        float h = main.H + (sub is null ? 0 : gap + sub.H);
        return new TitleBlock
        {
            Width = w, Height = h,
            Draw = (c, x, y) =>
            {
                main.DrawAt(c, x + AlignX(align, w, main.W), y);
                sub?.DrawAt(c, x + AlignX(align, w, sub.W), y + main.H + gap);
                main.Dispose(); sub?.Dispose();
            },
        };
    }

    /// <summary>文字の下に太い短い線、その横に添え文字。</summary>
    static TitleBlock BarTitle(DisplaySpec s, string text, float size, string align)
    {
        var fill = Col(s.TitleColor, SKColors.White);
        var accent = Col(s.TitleAccentColor, SKColors.Gray);
        var main = Piece(s, text, size, s.TitleBold, 0.16f, fill);
        var subText = SubtitleText(s);
        var sub = subText.Length > 0 ? Piece(s, subText, size * 0.38f, true, 0.2f, fill) : null;
        float barLen = size * 1.15f, barH = Math.Max(2, size * 0.11f), gap = size * 0.14f, subGap = size * 0.24f;
        float rowW = barLen + (sub is null ? 0 : subGap + sub.W);
        float rowH = Math.Max(barH, sub?.H ?? 0);
        float w = Math.Max(main.W, rowW);
        float h = main.H + gap + rowH;
        return new TitleBlock
        {
            Width = w, Height = h,
            Draw = (c, x, y) =>
            {
                main.DrawAt(c, x + AlignX(align, w, main.W), y);
                float ry = y + main.H + gap;
                float rx = x + AlignX(align, w, rowW);
                // 右揃えのときは「添え文字 ─ 線」の順にして、線が外側に来るようにする
                bool mirror = align == "right";
                float barX = mirror && sub is not null ? rx + sub.W + subGap : rx;
                float subX = mirror ? rx : rx + barLen + subGap;
                using (var bp = new SKPaint { Color = accent, IsAntialias = true })
                    c.DrawRoundRect(SKRect.Create(barX, ry + (rowH - barH) / 2, barLen, barH), barH / 2, barH / 2, bp);
                sub?.DrawAt(c, subX, ry + (rowH - sub.H) / 2);
                main.Dispose(); sub?.Dispose();
            },
        };
    }

    /// <summary>
    /// 色の板に文字。板の後ろに少しずらした枠線を重ね、板の内側に細い線を引いてポスター風にする。
    /// 添え文字は、板の下の辺にかかる小さなタグにする。
    /// </summary>
    static TitleBlock PlateTitle(DisplaySpec s, string text, float size, string align)
    {
        var accent = Col(s.TitleAccentColor, SKColors.Gray);
        var fill = Col(s.TitleColor, SKColors.White);
        var dark = Col(s.TitleStrokeColor, new SKColor(0x22, 0x22, 0x28));
        if (Luma(dark) > 0.45f) dark = new SKColor(0x22, 0x22, 0x28);
        // 板と文字の明るさが近いと読めないので、そのときは白か濃い色に替える
        var plateText = Math.Abs(Luma(fill) - Luma(accent)) >= 0.35 ? fill : Luma(accent) > 0.55 ? dark : SKColors.White;
        var main = Piece(s, text, size, s.TitleBold, 0, plateText);
        var subText = SubtitleText(s);
        // タグの文字は濃い地に、板の色（明るければ）か白で
        var tagText = Luma(accent) > 0.5f ? accent : SKColors.White;
        var sub = subText.Length > 0 ? Piece(s, subText, size * 0.3f, true, 0, tagText) : null;

        float px = size * 0.55f, py = size * 0.34f;
        float plateW = main.W + px * 2, plateH = main.H + py * 2;
        float offset = Math.Max(2, size * 0.09f);          // 後ろの枠のずらし量
        float tagPx = size * 0.22f, tagPy = size * 0.1f;
        float tagW = sub is null ? 0 : sub.W + tagPx * 2, tagH = sub is null ? 0 : sub.H + tagPy * 2;
        float w = Math.Max(plateW + offset, tagW + px * 0.6f);
        float h = plateH + offset + (sub is null ? 0 : tagH * 0.55f);
        return new TitleBlock
        {
            Width = w, Height = h,
            Draw = (c, x, y) =>
            {
                float left = x + AlignX(align, w, plateW + offset);
                var plate = SKRect.Create(left, y, plateW, plateH);
                float rad = Math.Min(plateH * 0.16f, size * 0.14f);

                // 後ろにずらした枠線
                var back = plate;
                back.Offset(offset, offset);
                using (var bp = new SKPaint { Color = dark, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, size * 0.045f) })
                    c.DrawRoundRect(back, rad, rad, bp);

                // 板（上をほんの少し明るく）
                var top = Lighten(accent, 0.1f);
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, plate.Top), new SKPoint(0, plate.Bottom),
                           [top, accent], [0, 1], SKShaderTileMode.Clamp))
                using (var pp = new SKPaint { Shader = shader, IsAntialias = true })
                    c.DrawRoundRect(plate, rad, rad, pp);

                // 内側の細い線
                var inner = plate;
                float inset = size * 0.1f;
                inner.Inflate(-inset, -inset);
                using (var ip = new SKPaint
                {
                    Color = plateText.WithAlpha(70), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, size * 0.02f),
                })
                    c.DrawRoundRect(inner, Math.Max(0, rad - inset), Math.Max(0, rad - inset), ip);

                main.DrawAt(c, left + px, y + py);

                // 添え文字のタグ（板の下の辺にかける）
                if (sub is not null)
                {
                    float tx = align == "right" ? plate.Right - px * 0.6f - tagW : plate.Left + px * 0.6f;
                    float ty = plate.Bottom - tagH * 0.45f;
                    var tag = SKRect.Create(tx, ty, tagW, tagH);
                    using (var tp = new SKPaint { Color = dark, IsAntialias = true })
                        c.DrawRoundRect(tag, tagH / 2, tagH / 2, tp);
                    sub.DrawAt(c, tx + tagPx, ty + tagPy);
                }
                main.Dispose(); sub?.Dispose();
            },
        };
    }

    static SKColor Lighten(SKColor c, float t) => new(
        (byte)(c.Red + (255 - c.Red) * t), (byte)(c.Green + (255 - c.Green) * t), (byte)(c.Blue + (255 - c.Blue) * t), c.Alpha);

    /// <summary>縦書き。添え文字は細い列にして隣に。</summary>
    static TitleBlock VerticalTitle(DisplaySpec s, string text, float size)
    {
        var fill = Col(s.TitleColor, SKColors.White);
        var mainCol = VerticalColumn(s, text, size, s.TitleBold, 0.16f, fill);
        var subText = SubtitleText(s);
        var subCol = subText.Length > 0 ? VerticalColumn(s, subText, size * 0.4f, true, 0.2f, fill) : null;
        float gap = size * 0.25f;
        float w = mainCol.W + (subCol is null ? 0 : gap + subCol.W);
        float h = Math.Max(mainCol.H, subCol?.H ?? 0);
        return new TitleBlock
        {
            Width = w, Height = h, Vertical = true,
            Draw = (c, x, y) =>
            {
                // 縦書きは右から読むので、本文を右、添え文字を左に置く
                mainCol.Draw(c, x + w - mainCol.W, y);
                subCol?.Draw(c, x, y);
            },
        };
    }

    sealed record Column(float W, float H, Action<SKCanvas, float, float> Draw);

    /// <summary>ほぼ英数字だけの文字列か（縦書きでは 1 行のまま 90° 回す）。</summary>
    static bool IsLatin(string text) => text.Count(ch => ch < 0x2E80) >= text.Length * 0.8;

    static Column VerticalColumn(DisplaySpec s, string text, float size, bool bold, float strokeRatio, SKColor fill)
    {
        if (IsLatin(text))
        {
            // 英字は横向きのまま 90° 回して、上から下へ読めるように置く
            var p = Piece(s, text, size, bold, strokeRatio, fill);
            return new Column(p.H, p.W, (c, x, y) =>
            {
                c.Save();
                c.Translate(x + p.H, y);
                c.RotateDegrees(90);
                p.DrawAt(c, 0, 0);
                c.Restore();
                p.Dispose();
            });
        }
        // 伸ばし棒や波線は 90° 回して縦書きらしくする
        const string Rotate = "ー－-‐—―〜～…‥()（）[]「」『』【】<>＜＞";
        var chars = text.EnumerateRunes().Select(r => r.ToString()).ToList();
        var pieces = chars.Select(ch => ch.Trim().Length == 0 ? null : Piece(s, ch, size, bold, strokeRatio, fill)).ToList();
        float adv = size * 1.04f;
        float colW = Math.Max(size * 0.6f, pieces.Where(p => p is not null).Select(p => Math.Max(p!.W, p.H)).DefaultIfEmpty(size).Max());
        float h = adv * chars.Count - size * 0.04f;
        return new Column(colW, h, (c, x, y) =>
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p is null) continue;
                float cx = x + colW / 2, cy = y + adv * i + adv / 2;
                if (Rotate.Contains(p.Text))
                {
                    c.Save();
                    c.RotateDegrees(90, cx, cy);
                    p.DrawAt(c, cx - p.W / 2, cy - p.H / 2);
                    c.Restore();
                }
                else p.DrawAt(c, cx - p.W / 2, cy - p.H / 2);
                p.Dispose();
            }
        });
    }

    /// <summary>背景の上に大きく薄く敷くタイトル（透かし）。立ち絵より奥に描く。</summary>
    static void DrawWatermark(SKCanvas c, DisplaySpec s, SKRect area, SKRect spriteArea, int W, int H)
    {
        var text = s.TitleText?.Trim() ?? "";
        if (!s.ShowTitle || text.Length == 0) return;
        using var font = MakeFont(s.TitleFont, s.TitleBold, 100, text);
        font.MeasureText(text, out var b);
        if (b.Width <= 0 || b.Height <= 0) return;

        // 大きさ: 文字の高さを画像の高さに対する % で決める（幅は画像からはみ出してもよい）
        float k = (float)(H * Math.Clamp(s.WatermarkSize, 3, 100) / 100.0) / b.Height;
        font.Size = 100 * k;
        font.MeasureText(text, out b);

        // 添え文字は小さくして下に添える
        var subText = SubtitleText(s);
        using var subFont = subText.Length > 0 ? MakeFont(s.TitleFont, s.TitleBold, font.Size * 0.24f, subText) : null;
        SKRect sb = default;
        if (subFont is not null) subFont.MeasureText(subText, out sb);
        float subGap = font.Size * 0.08f;
        float blockW = Math.Max(b.Width, sb.Width), blockH = b.Height + (subFont is null ? 0 : subGap + sb.Height);

        float angle = s.WatermarkFollowBg && s.BgKind is "band" or "split" or "stripes" or "halftone" or "chevron"
            ? (float)s.BgAngle : (float)s.WatermarkAngle;
        // 傾けたあとの外形の半分（端に置いたときにはみ出さないよう、この分だけ内側に寄せる）
        double rad = angle * Math.PI / 180;
        float halfW = (float)(Math.Abs(Math.Cos(rad)) * blockW / 2 + Math.Abs(Math.Sin(rad)) * blockH / 2);
        float halfH = (float)(Math.Abs(Math.Sin(rad)) * blockW / 2 + Math.Abs(Math.Cos(rad)) * blockH / 2);

        // 置き場所（名前どおり）: 立ち絵の後ろ / 画像の上・中央・下 / 四隅。そこから指定の分だけずらす
        var pos = s.WatermarkPosition;
        var box = pos == "sprite" && !spriteArea.IsEmpty ? spriteArea : area;
        float cx = pos switch
        {
            "top-left" or "bottom-left" => box.Left + halfW,
            "top-right" or "bottom-right" => box.Right - halfW,
            _ => box.MidX,
        };
        float cy = pos switch
        {
            "top" or "top-left" or "top-right" => box.Top + halfH,
            "bottom" or "bottom-left" or "bottom-right" => box.Bottom - halfH,
            _ => box.MidY,
        };
        cx += (float)(s.WatermarkOffsetX / 100.0 * W);
        cy += (float)(s.WatermarkOffsetY / 100.0 * H);

        float op = (float)Math.Clamp(s.WatermarkOpacity / 100.0, 0, 1);
        var fillColor = ImageOps.ParseColor(s.WatermarkColor) ?? AutoWatermarkColor(s);
        var lineColor = ImageOps.ParseColor(s.WatermarkOutlineColor) ?? fillColor;
        bool drawFill = s.WatermarkStyle is "fill" or "both";
        bool drawLine = s.WatermarkStyle is "outline" or "both";
        float lineW = (float)Math.Max(1, font.Size * s.WatermarkOutlineWidth / 100.0);

        c.Save();
        c.RotateDegrees(angle, cx, cy);
        void Draw(string t, SKFont f, float x, float y)
        {
            if (drawFill)
                using (var fp = new SKPaint { Color = fillColor.WithAlpha((byte)(fillColor.Alpha * op)), IsAntialias = true })
                    c.DrawText(t, x, y, SKTextAlign.Left, f, fp);
            if (drawLine)
                using (var lp = new SKPaint
                {
                    Color = lineColor.WithAlpha((byte)(lineColor.Alpha * (drawFill ? Math.Min(1, op * 0.6f + 0.35f) : op))),
                    IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = lineW, StrokeJoin = SKStrokeJoin.Round,
                })
                    c.DrawText(t, x, y, SKTextAlign.Left, f, lp);
        }
        float top = cy - blockH / 2;
        Draw(text, font, cx - b.MidX, top - b.Top);
        if (subFont is not null)
            Draw(subText, subFont, cx - sb.MidX, top + b.Height + subGap - sb.Top);
        c.Restore();
    }

    /// <summary>
    /// 透かしの色を自動で決める。背景（地の色と模様の色）の明るさの逆にして、模様に紛れないようにする。
    /// 色合いはタイトルのアクセント色から取る（無彩色なら背景の色から）。
    /// </summary>
    static SKColor AutoWatermarkColor(DisplaySpec s)
    {
        var c1 = Col(s.BgColor1, SKColors.White);
        var c2 = Col(s.BgColor2, c1);
        float bgLuma = s.BgKind switch
        {
            "solid" or "image" => Luma(c1),
            "gradient" or "radial" or "band" or "split" => (Luma(c1) + Luma(c2)) / 2,
            _ => Luma(c1) * 0.65f + Luma(c2) * 0.35f,   // 模様は地の上に一部だけ乗る
        };
        if (s.BgKind == "image") bgLuma *= 1 - (float)(s.BgImageDim / 100.0);
        var hueSrc = ColorAnalyzer.ToHsl(Col(s.TitleAccentColor, c2));
        if (hueSrc.S < 0.12) hueSrc = ColorAnalyzer.ToHsl(c2);
        return bgLuma > 0.5f
            ? ColorAnalyzer.FromHsl(new ColorAnalyzer.Hsl(hueSrc.H, Math.Min(hueSrc.S, 0.55), 0.26))
            : ColorAnalyzer.FromHsl(new ColorAnalyzer.Hsl(hueSrc.H, Math.Min(hueSrc.S, 0.45), 0.9));
    }

    /// <summary>クレジット（描いた人の名前など）を四隅のどこかに小さく入れる。</summary>
    static void DrawCredit(SKCanvas c, DisplaySpec s, int W, int H, double u)
    {
        var lines = (s.CreditText ?? "").Replace("\r", "").Split('\n')
            .Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        if (lines.Count == 0) return;

        float size = (float)Math.Max(4, s.CreditSize * u);
        var all = string.Concat(lines);
        using var font = MakeFont(s.CreditFont, s.CreditBold, size, all.Length > 0 ? all : "A");
        var metrics = font.Metrics;
        float lineH = (metrics.Descent - metrics.Ascent) * 1.15f;
        var widths = lines.Select(l => l.Length == 0 ? 0 : font.MeasureText(l)).ToList();
        float blockW = widths.Max(), blockH = lineH * lines.Count - (lineH - (metrics.Descent - metrics.Ascent));

        bool right = s.CreditPosition.EndsWith("right");
        bool top = s.CreditPosition.StartsWith("top");
        // 端からの距離は余白の半分ほど（ひっそりと、でも端に詰めすぎない）
        float inset = (float)Math.Max(size * 0.8, s.Margin * u * 0.5);
        float pad = s.CreditBackdrop ? size * 0.45f : 0;
        float x0 = right ? W - inset - blockW - pad : inset + pad;
        float y0 = top ? inset + pad : H - inset - blockH - pad;

        // 色: 指定がなければ背景の明るさの逆の灰色
        var bg1 = Col(s.BgColor1, SKColors.White);
        var auto = Luma(bg1) > 0.5f ? new SKColor(0x2A, 0x2A, 0x30) : new SKColor(0xF2, 0xF2, 0xF5);
        var color = ImageOps.ParseColor(s.CreditColor) ?? auto;
        byte alpha = (byte)(color.Alpha * Math.Clamp(s.CreditOpacity, 0, 100) / 100.0);

        if (s.CreditBackdrop)
        {
            // 文字の色の反対の明るさで、半透明の下地
            var under = Luma(color) > 0.5f ? new SKColor(0, 0, 0, 110) : new SKColor(255, 255, 255, 150);
            var rect = new SKRect(x0 - pad, y0 - pad, x0 + blockW + pad, y0 + blockH + pad);
            using var bp = new SKPaint { Color = under, IsAntialias = true };
            c.DrawRoundRect(rect, size * 0.5f, size * 0.5f, bp);
        }

        using var p = new SKPaint { Color = color.WithAlpha(alpha), IsAntialias = true };
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;
            float lx = right ? x0 + blockW - widths[i] : x0;
            float baseline = y0 + i * lineH - metrics.Ascent;
            c.DrawText(lines[i], lx, baseline, SKTextAlign.Left, font, p);
        }
    }

    static float Luma(SKColor c) => (0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue) / 255f;
}
