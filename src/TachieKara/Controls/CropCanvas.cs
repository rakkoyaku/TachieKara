using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TachieKara.Controls;

/// <summary>
/// 画像を拡大縮小・移動しながら、切り抜き枠（正方形または任意の長方形）を置くコントロール。
/// 座標はすべて画像のピクセル座標。枠は画像の外にはみ出してもよい。
/// </summary>
public sealed class CropCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<CropCanvas, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<int> CropXProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropX), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> CropYProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropY), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> CropWidthProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropWidth), 256, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> CropHeightProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropHeight), 256, defaultBindingMode: BindingMode.TwoWay);
    /// <summary>true なら枠は正方形のまま拡縮する（顔アイコン）。false なら幅と高さを別々に変えられる（バストアップ）。</summary>
    public static readonly StyledProperty<bool> LockSquareProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(LockSquare), true);
    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<CropCanvas, string?>(nameof(Placeholder));
    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(ShowGuides), true, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> GuideCenterProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(GuideCenter), true);
    public static readonly StyledProperty<bool> GuideThirdsProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(GuideThirds), true);
    public static readonly StyledProperty<bool> GuideCircleProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(GuideCircle));
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CropCanvas, IBrush?>(nameof(Background), new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x11)));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<CropCanvas, IBrush?>(nameof(AccentBrush), new SolidColorBrush(Color.FromRgb(0x8E, 0x78, 0xF0)));
    /// <summary>false にすると切り抜き枠を出さず、画像を見るだけの表示になる（ディスプレイ画像のプレビュー用）。</summary>
    public static readonly StyledProperty<bool> ShowCropProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(ShowCrop), true);
    public static readonly DirectProperty<CropCanvas, double> ZoomProperty =
        AvaloniaProperty.RegisterDirect<CropCanvas, double>(nameof(Zoom), o => o.Zoom);

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public int CropX { get => GetValue(CropXProperty); set => SetValue(CropXProperty, value); }
    public int CropY { get => GetValue(CropYProperty); set => SetValue(CropYProperty, value); }
    public int CropWidth { get => GetValue(CropWidthProperty); set => SetValue(CropWidthProperty, value); }
    public int CropHeight { get => GetValue(CropHeightProperty); set => SetValue(CropHeightProperty, value); }
    public bool LockSquare { get => GetValue(LockSquareProperty); set => SetValue(LockSquareProperty, value); }
    public bool ShowCrop { get => GetValue(ShowCropProperty); set => SetValue(ShowCropProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public bool ShowGuides { get => GetValue(ShowGuidesProperty); set => SetValue(ShowGuidesProperty, value); }
    public bool GuideCenter { get => GetValue(GuideCenterProperty); set => SetValue(GuideCenterProperty, value); }
    public bool GuideThirds { get => GetValue(GuideThirdsProperty); set => SetValue(GuideThirdsProperty, value); }
    public bool GuideCircle { get => GetValue(GuideCircleProperty); set => SetValue(GuideCircleProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    double _zoom = 1;
    public double Zoom { get => _zoom; private set => SetAndRaise(ZoomProperty, ref _zoom, value); }

    Vector _offset;              // 画面座標 = 画像座標 * Zoom + _offset
    bool _needsFit = true;

    enum DragMode { None, Move, Resize, Pan }
    DragMode _drag;
    Point _dragStartScreen;
    Vector _dragStartOffset;
    int _dragStartX, _dragStartY, _dragStartW, _dragStartH;
    Point _anchor;               // リサイズ時に固定される角（画像座標）
    int _cornerSignX, _cornerSignY;
    bool _spaceDown;

    const double HandleSize = 22;          // 四隅のつまみの当たり判定（見た目は L 字）
    const double BracketLength = 18;
    static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(0xA6, 0x08, 0x08, 0x0C));
    static readonly IPen FrameShadow = new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), 3);
    static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), 1);
    static readonly IPen BracketShadow = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    static readonly IPen BracketPen = new Pen(Brushes.White, 3.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    static readonly IBrush Checker = CreateChecker();

    static CropCanvas()
    {
        AffectsRender<CropCanvas>(SourceProperty, CropXProperty, CropYProperty, CropWidthProperty, CropHeightProperty, LockSquareProperty, ShowCropProperty, PlaceholderProperty,
            ShowGuidesProperty, GuideCenterProperty, GuideThirdsProperty, GuideCircleProperty,
            BackgroundProperty, AccentBrushProperty);
        FocusableProperty.OverrideDefaultValue<CropCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<CropCanvas>(true);
    }

    public static IBrush CreateChecker()
    {
        const int cell = 8;
        var wb = new WriteableBitmap(new PixelSize(cell * 2, cell * 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            unsafe
            {
                for (int y = 0; y < cell * 2; y++)
                {
                    var row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
                    for (int x = 0; x < cell * 2; x++)
                        row[x] = ((x / cell + y / cell) % 2 == 0) ? 0xFFCCCCCC : 0xFFF4F4F4;
                }
            }
        }
        return new ImageBrush(wb)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, cell * 2, cell * 2, RelativeUnit.Absolute),
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            var oldSize = (change.OldValue as Bitmap)?.PixelSize;
            var newSize = (change.NewValue as Bitmap)?.PixelSize;
            if (oldSize != newSize) { _needsFit = true; PostFit(); }
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_needsFit) PostFit();
    }

    // 描画中にプロパティ（Zoom）を変えると例外になるので、表示合わせは描画の外で行う
    void PostFit() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (_needsFit && Source is not null && Bounds.Width > 0) FitToView();
    }, Avalonia.Threading.DispatcherPriority.Render);

    // ---- 表示位置 ----

    /// <summary>画像全体が収まるように表示する。</summary>
    public void FitToView()
    {
        if (Source is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var s = Source.PixelSize;
        SetZoomAndCenter(Math.Min(Bounds.Width / s.Width, Bounds.Height / s.Height) * 0.95,
            new Point(s.Width / 2.0, s.Height / 2.0));
        _needsFit = false;
    }

    /// <summary>切り抜き枠が大きく見えるように表示する。</summary>
    public void ZoomToCrop()
    {
        if (Source is null || Bounds.Width <= 0 || CropWidth <= 0 || CropHeight <= 0) return;
        var z = Math.Min(Bounds.Width / (CropWidth * 1.6), Bounds.Height / (CropHeight * 1.6));
        SetZoomAndCenter(z, new Point(CropX + CropWidth / 2.0, CropY + CropHeight / 2.0));
        _needsFit = false;
    }

    void SetZoomAndCenter(double zoom, Point imageCenter)
    {
        Zoom = Math.Clamp(zoom, 0.01, 32);
        _offset = new Vector(Bounds.Width / 2 - imageCenter.X * Zoom, Bounds.Height / 2 - imageCenter.Y * Zoom);
        InvalidateVisual();
    }

    void ZoomAt(Point screen, double factor)
    {
        var img = ToImage(screen);
        Zoom = Math.Clamp(Zoom * factor, 0.01, 32);
        _offset = new Vector(screen.X - img.X * Zoom, screen.Y - img.Y * Zoom);
        InvalidateVisual();
    }

    /// <summary>表示の中心を基準に拡大・縮小する（ツールバーのボタン用）。</summary>
    public void ZoomBy(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    Point ToImage(Point screen) => new((screen.X - _offset.X) / Zoom, (screen.Y - _offset.Y) / Zoom);
    Point ToScreen(double x, double y) => new(x * Zoom + _offset.X, y * Zoom + _offset.Y);
    Rect CropScreenRect => new(ToScreen(CropX, CropY), new Size(CropWidth * Zoom, CropHeight * Zoom));

    // ---- 描画 ----

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Background ?? Brushes.Black, bounds);
        if (Source is null) return;            // 空の状態は画面側（XAML）で表示する
        if (_needsFit) return;                 // PostFit が済むまで画像は描かない

        var px = Source.PixelSize;
        var imgRect = new Rect(ToScreen(0, 0), new Size(px.Width * Zoom, px.Height * Zoom));
        ctx.FillRectangle(Checker, imgRect.Intersect(bounds));

        var interp = Zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interp }))
            ctx.DrawImage(Source, new Rect(0, 0, px.Width, px.Height), imgRect);

        if (!ShowCrop) return;

        // 枠の外を暗くする
        var c = CropScreenRect;
        // 暗くするのは画像の上だけ（背景まで暗くするとライトテーマで全体が灰色に見える）
        var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(imgRect.Intersect(bounds)), new RectangleGeometry(c));
        ctx.DrawGeometry(Dim, null, geo);

        if (ShowGuides) GuideRenderer.Draw(ctx, c, GuideCenter, GuideThirds, GuideCircle);

        // 枠線と、四隅の L 字つまみ
        ctx.DrawRectangle(null, FrameShadow, c);
        ctx.DrawRectangle(null, FramePen, c);
        var corners = BracketGeometry(c);
        ctx.DrawGeometry(null, BracketShadow, corners);
        ctx.DrawGeometry(null, BracketPen, corners);

        // サイズ表示（操作中は座標も出す）
        var text = _drag is DragMode.Move or DragMode.Resize
            ? $"X {CropX}   Y {CropY}   {CropWidth} × {CropHeight} px"
            : $"{CropWidth} × {CropHeight} px";
        var label = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 11.5, Brushes.White);
        var pill = new Size(label.Width + 16, label.Height + 6);
        var lx = Math.Clamp(c.Center.X - pill.Width / 2, 6, Math.Max(6, bounds.Width - pill.Width - 6));
        var ly = c.Bottom + 10;
        if (ly + pill.Height > bounds.Height - 64) ly = c.Bottom - pill.Height - 10;   // 下のツールバーと重ならないように
        var pr = new Rect(new Point(lx, ly), pill);
        ctx.DrawRectangle(AccentBrush, null, pr, pill.Height / 2, pill.Height / 2);
        ctx.DrawText(label, pr.TopLeft + new Point(8, 3));
    }

    static StreamGeometry BracketGeometry(Rect c)
    {
        var len = Math.Min(BracketLength, c.Width / 3);
        var g = new StreamGeometry();
        using var gc = g.Open();
        void L(Point corner, double dx, double dy)
        {
            gc.BeginFigure(corner + new Vector(dx * len, 0), false);
            gc.LineTo(corner);
            gc.LineTo(corner + new Vector(0, dy * len));
            gc.EndFigure(false);
        }
        L(c.TopLeft, 1, 1);
        L(c.TopRight, -1, 1);
        L(c.BottomLeft, 1, -1);
        L(c.BottomRight, -1, -1);
        return g;
    }

    static Rect[] HandleRects(Rect c)
    {
        var s = HandleSize;
        return
        [
            new Rect(c.Left - s / 2, c.Top - s / 2, s, s),
            new Rect(c.Right - s / 2, c.Top - s / 2, s, s),
            new Rect(c.Left - s / 2, c.Bottom - s / 2, s, s),
            new Rect(c.Right - s / 2, c.Bottom - s / 2, s, s),
        ];
    }

    // ---- 操作 ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null) return;
        Focus();
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (e.ClickCount == 2 && props.IsLeftButtonPressed)
        {
            FitToView();
            e.Handled = true;
            return;
        }

        _dragStartScreen = p;
        _dragStartOffset = _offset;
        _dragStartX = CropX;
        _dragStartY = CropY;
        _dragStartW = CropWidth;
        _dragStartH = CropHeight;

        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || _spaceDown || !ShowCrop)
        {
            _drag = DragMode.Pan;
        }
        else if (props.IsLeftButtonPressed)
        {
            var c = CropScreenRect;
            var handles = HandleRects(c);
            int hit = Array.FindIndex(handles, h => h.Contains(p));
            if (hit >= 0)
            {
                _drag = DragMode.Resize;
                _cornerSignX = hit % 2 == 0 ? -1 : 1;   // 0:左上 1:右上 2:左下 3:右下
                _cornerSignY = hit < 2 ? -1 : 1;
                _anchor = new Point(_cornerSignX < 0 ? CropX + CropWidth : CropX,
                                    _cornerSignY < 0 ? CropY + CropHeight : CropY);
            }
            else if (c.Contains(p))
            {
                _drag = DragMode.Move;
            }
            else
            {
                _drag = DragMode.Pan;
            }
        }
        e.Pointer.Capture(this);
        UpdateCursor(p);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        switch (_drag)
        {
            case DragMode.Pan:
                _offset = _dragStartOffset + (p - _dragStartScreen);
                InvalidateVisual();
                break;
            case DragMode.Move:
                var d = (p - _dragStartScreen) / Zoom;
                CropX = _dragStartX + (int)Math.Round(d.X);
                CropY = _dragStartY + (int)Math.Round(d.Y);
                break;
            case DragMode.Resize:
                var ip = ToImage(p);
                var dx = (ip.X - _anchor.X) * _cornerSignX;   // 角を外へ引くと正
                var dy = (ip.Y - _anchor.Y) * _cornerSignY;
                int w, h;
                if (LockSquare)
                {
                    w = h = ClampSize((int)Math.Round(Math.Max(dx, dy)));
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    // Shift: 縦横比を保つ
                    var k = Math.Max(dx / _dragStartW, dy / _dragStartH);
                    (w, h) = ScaleKeepingRatio(_dragStartW, _dragStartH, k);
                }
                else
                {
                    w = ClampSize((int)Math.Round(dx));
                    h = ClampSize((int)Math.Round(dy));
                }
                SetRect((int)Math.Round(_cornerSignX < 0 ? _anchor.X - w : _anchor.X),
                        (int)Math.Round(_cornerSignY < 0 ? _anchor.Y - h : _anchor.Y), w, h);
                break;
            default:
                UpdateCursor(p);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = DragMode.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
        UpdateCursor(e.GetPosition(this));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null) return;
        var steps = e.Delta.Y;
        if (ShowCrop && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            // 枠の中心を保ったまま拡縮
            ResizeAroundCenter(Math.Pow(1.05, steps));
        }
        else
        {
            ZoomAt(e.GetPosition(this), Math.Pow(1.2, steps));
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Source is null) return;
        if (!ShowCrop) return;
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left: CropX -= step; break;
            case Key.Right: CropX += step; break;
            case Key.Up: CropY -= step; break;
            case Key.Down: CropY += step; break;
            case Key.OemPlus or Key.Add: ResizeAroundCenter((CropWidth + step * 2.0) / CropWidth); break;
            case Key.OemMinus or Key.Subtract: ResizeAroundCenter((CropWidth - step * 2.0) / CropWidth); break;
            case Key.G: ShowGuides = !ShowGuides; break;
            case Key.Space: _spaceDown = true; Cursor = new Cursor(StandardCursorType.Hand); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space) { _spaceDown = false; Cursor = Cursor.Default; }
    }

    /// <summary>枠の中心と縦横比を保ったまま factor 倍にする。</summary>
    void ResizeAroundCenter(double factor)
    {
        var cx = CropX + CropWidth / 2.0;
        var cy = CropY + CropHeight / 2.0;
        var (w, h) = LockSquare
            ? (ClampSize((int)Math.Round(CropWidth * factor)), 0)
            : ScaleKeepingRatio(CropWidth, CropHeight, factor);
        if (LockSquare) h = w;
        SetRect((int)Math.Round(cx - w / 2.0), (int)Math.Round(cy - h / 2.0), w, h);
    }

    (int W, int H) ScaleKeepingRatio(int w0, int h0, double k)
    {
        var minK = 8.0 / Math.Min(w0, h0);
        k = Math.Max(k, minK);
        return (ClampSize((int)Math.Round(w0 * k)), ClampSize((int)Math.Round(h0 * k)));
    }

    void SetRect(int x, int y, int w, int h)
    {
        CropWidth = w;
        CropHeight = h;
        CropX = x;
        CropY = y;
    }

    int ClampSize(int size)
    {
        var max = Source is null ? 100000 : Math.Max(Source.PixelSize.Width, Source.PixelSize.Height) * 2;
        return Math.Clamp(size, 8, max);
    }

    void UpdateCursor(Point p)
    {
        if (Source is null || _spaceDown) return;
        if (!ShowCrop) { Cursor = _drag == DragMode.Pan ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default; return; }
        var c = CropScreenRect;
        var handles = HandleRects(c);
        int hit = Array.FindIndex(handles, h => h.Contains(p));
        Cursor = _drag switch
        {
            DragMode.Pan => new Cursor(StandardCursorType.SizeAll),
            DragMode.Move => new Cursor(StandardCursorType.SizeAll),
            _ when hit is 0 or 3 => new Cursor(StandardCursorType.TopLeftCorner),
            _ when hit is 1 or 2 => new Cursor(StandardCursorType.TopRightCorner),
            _ when c.Contains(p) => new Cursor(StandardCursorType.SizeAll),
            _ => Cursor.Default,
        };
    }
}
