using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TachieKara.Controls;

/// <summary>切り抜き枠に重ねるガイド線（中心の十字・三分割・円）の描画。</summary>
public static class GuideRenderer
{
    // どんな絵柄の上でも見えるよう、暗い下地線の上に明るい線を重ねる
    static readonly IPen Underlay = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 3);
    static readonly IPen DashedUnderlay = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 3, new DashStyle([2, 2], 0));
    static readonly IPen CenterPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 1);
    static readonly IPen ThirdsPen = new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), 1, new DashStyle([6, 6], 0));
    static readonly IPen CirclePen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0xAB)), 1);

    public static void Draw(DrawingContext ctx, Rect r, bool center, bool thirds, bool circle)
    {
        if (r.Width < 6) return;
        using var clip = ctx.PushClip(r);

        if (thirds)
        {
            for (int i = 1; i <= 2; i++)
            {
                var x = r.X + r.Width * i / 3;
                var y = r.Y + r.Height * i / 3;
                Line(ctx, DashedUnderlay, ThirdsPen, new Point(x, r.Top), new Point(x, r.Bottom));
                Line(ctx, DashedUnderlay, ThirdsPen, new Point(r.Left, y), new Point(r.Right, y));
            }
        }

        if (circle)
        {
            var g = new EllipseGeometry(r.Deflate(0.5));
            ctx.DrawGeometry(null, Underlay, g);
            ctx.DrawGeometry(null, CirclePen, g);
        }

        if (center)
        {
            var c = r.Center;
            Line(ctx, Underlay, CenterPen, new Point(c.X, r.Top), new Point(c.X, r.Bottom));
            Line(ctx, Underlay, CenterPen, new Point(r.Left, c.Y), new Point(r.Right, c.Y));
            // 中心点
            var dot = new Rect(c.X - 3, c.Y - 3, 6, 6);
            ctx.DrawEllipse(CenterPen.Brush, Underlay, dot.Center, 3, 3);
        }
    }

    static void Line(DrawingContext ctx, IPen under, IPen over, Point a, Point b)
    {
        ctx.DrawLine(under, a, b);
        ctx.DrawLine(over, a, b);
    }
}

/// <summary>出力プレビューなどの画像の上に重ねるガイド。自分の大きさいっぱいに描く。</summary>
public sealed class GuideOverlay : Control
{
    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<GuideOverlay, bool>(nameof(ShowGuides), true);
    public static readonly StyledProperty<bool> GuideCenterProperty =
        AvaloniaProperty.Register<GuideOverlay, bool>(nameof(GuideCenter), true);
    public static readonly StyledProperty<bool> GuideThirdsProperty =
        AvaloniaProperty.Register<GuideOverlay, bool>(nameof(GuideThirds), true);
    public static readonly StyledProperty<bool> GuideCircleProperty =
        AvaloniaProperty.Register<GuideOverlay, bool>(nameof(GuideCircle));

    public bool ShowGuides { get => GetValue(ShowGuidesProperty); set => SetValue(ShowGuidesProperty, value); }
    public bool GuideCenter { get => GetValue(GuideCenterProperty); set => SetValue(GuideCenterProperty, value); }
    public bool GuideThirds { get => GetValue(GuideThirdsProperty); set => SetValue(GuideThirdsProperty, value); }
    public bool GuideCircle { get => GetValue(GuideCircleProperty); set => SetValue(GuideCircleProperty, value); }

    static GuideOverlay()
    {
        AffectsRender<GuideOverlay>(ShowGuidesProperty, GuideCenterProperty, GuideThirdsProperty, GuideCircleProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GuideOverlay>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        if (!ShowGuides) return;
        GuideRenderer.Draw(ctx, new Rect(Bounds.Size), GuideCenter, GuideThirds, GuideCircle);
    }
}
