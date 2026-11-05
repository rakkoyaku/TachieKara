using System.Reflection;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TachieKara.Models;

/// <summary>
/// ディスプレイ画像（立ち絵＋表情一覧を 1 枚に並べた画像）の設定。
/// 長さはすべて「画像の幅を 1000 としたときの値」で持つので、幅を変えても見た目の比率は変わらない。
/// </summary>
public sealed partial class DisplaySpec : ObservableObject
{
    // ---- キャンバス ----
    [ObservableProperty] public partial int Width { get; set; } = 2400;
    /// <summary>"1:1" "4:3" "3:2" "16:9" "3:4" "9:16" "auto"（立ち絵に合わせる）</summary>
    [ObservableProperty] public partial string Aspect { get; set; } = "3:2";
    [ObservableProperty] public partial double Margin { get; set; } = 40;

    // ---- 背景 ----
    /// <summary>solid / gradient / radial / band / split / stripes / dots / grid / checker / image</summary>
    [ObservableProperty] public partial string BgKind { get; set; } = "band";
    [ObservableProperty] public partial string BgColor1 { get; set; } = "#ECE6F4";
    [ObservableProperty] public partial string BgColor2 { get; set; } = "#7868AA";
    /// <summary>帯のふちの線などに使う 3 色目。</summary>
    [ObservableProperty] public partial string BgColor3 { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial double BgAngle { get; set; } = -12;
    /// <summary>帯の太さ・模様の大きさなど（種類ごとに意味が変わる）。</summary>
    [ObservableProperty] public partial double BgSize { get; set; } = 60;
    /// <summary>帯の位置・グラデーションの中心など（-50〜50）。</summary>
    [ObservableProperty] public partial double BgOffset { get; set; } = 0;
    [ObservableProperty] public partial double PatternOpacity { get; set; } = 35;
    [ObservableProperty] public partial bool BgEdgeLines { get; set; } = true;
    [ObservableProperty] public partial string BgImagePath { get; set; } = "";
    [ObservableProperty] public partial double BgImageDim { get; set; } = 0;
    [ObservableProperty] public partial double BgImageBlur { get; set; } = 0;
    // 重ねる効果
    [ObservableProperty] public partial bool Grain { get; set; }
    [ObservableProperty] public partial double GrainAmount { get; set; } = 25;
    [ObservableProperty] public partial bool Vignette { get; set; }
    [ObservableProperty] public partial bool InnerFrame { get; set; }
    [ObservableProperty] public partial string InnerFrameColor { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial double InnerFrameWidth { get; set; } = 4;
    [ObservableProperty] public partial double InnerFrameInset { get; set; } = 18;

    // ---- 立ち絵 ----
    [ObservableProperty] public partial bool ShowSprite { get; set; } = true;
    /// <summary>立ち絵の透明な余白を除いてから大きさを決める。</summary>
    [ObservableProperty] public partial bool SpriteTrim { get; set; } = true;
    /// <summary>使う立ち絵のパス。空ならベース画像。</summary>
    [ObservableProperty] public partial string SpritePath { get; set; } = "";
    /// <summary>"left" / "right"</summary>
    [ObservableProperty] public partial string SpriteSide { get; set; } = "left";
    /// <summary>立ち絵に使う幅（画像幅に対する %）。</summary>
    [ObservableProperty] public partial double SpriteArea { get; set; } = 42;
    /// <summary>立ち絵の大きさ（領域に収まる大きさを 100 とする %）。</summary>
    [ObservableProperty] public partial double SpriteScale { get; set; } = 100;
    [ObservableProperty] public partial double SpriteOffsetX { get; set; }
    [ObservableProperty] public partial double SpriteOffsetY { get; set; }
    /// <summary>"bottom" / "center"</summary>
    [ObservableProperty] public partial string SpriteAlign { get; set; } = "bottom";
    [ObservableProperty] public partial bool SpriteOutline { get; set; } = true;
    [ObservableProperty] public partial string SpriteOutlineColor { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial double SpriteOutlineWidth { get; set; } = 5;
    /// <summary>フチのなめらかさ（0 = シルエットどおり。大きいほど細かいギザギザやすき間をならす）。</summary>
    [ObservableProperty] public partial double SpriteOutlineSmooth { get; set; } = 6;
    /// <summary>フチの縁: "aa"（アンチエイリアス）/ "crisp"（くっきり）</summary>
    [ObservableProperty] public partial string SpriteOutlineEdge { get; set; } = "aa";
    [ObservableProperty] public partial bool SpriteShadow { get; set; } = true;
    [ObservableProperty] public partial string SpriteShadowColor { get; set; } = "#2A1846";
    [ObservableProperty] public partial double SpriteShadowOpacity { get; set; } = 30;
    [ObservableProperty] public partial double SpriteShadowDistance { get; set; } = 8;
    [ObservableProperty] public partial double SpriteShadowBlur { get; set; } = 8;
    // 奥に大きく重ねる立ち絵
    [ObservableProperty] public partial bool Ghost { get; set; }
    [ObservableProperty] public partial string GhostPath { get; set; } = "";
    [ObservableProperty] public partial double GhostOpacity { get; set; } = 30;
    [ObservableProperty] public partial double GhostScale { get; set; } = 160;
    [ObservableProperty] public partial double GhostOffsetX { get; set; } = -12;
    [ObservableProperty] public partial double GhostOffsetY { get; set; } = 18;

    // ---- 表情グリッド ----
    /// <summary>セルの縦横比 "1:1" "4:3" "3:2" "16:9" "3:4"</summary>
    [ObservableProperty] public partial string CellAspect { get; set; } = "1:1";
    /// <summary>顔アイコンの枠に対する寄り・引き（100 = 枠ちょうど、大きいほど引く）。</summary>
    [ObservableProperty] public partial double CellZoom { get; set; } = 100;
    /// <summary>0 = 自動（一番大きく並ぶ列数）</summary>
    [ObservableProperty] public partial int Columns { get; set; }
    [ObservableProperty] public partial double CellGap { get; set; } = 14;
    /// <summary>セルの大きさの上限（画像幅に対する %）。表情が少ないときに大きくなりすぎないように。</summary>
    [ObservableProperty] public partial double CellMax { get; set; } = 22;
    /// <summary>"center" / "top" / "bottom"</summary>
    [ObservableProperty] public partial string GridAlign { get; set; } = "center";
    [ObservableProperty] public partial string CellBg { get; set; } = "#F7F5FA";
    [ObservableProperty] public partial double FrameWidth { get; set; } = 3;
    [ObservableProperty] public partial string FrameColor { get; set; } = "#2D2446";
    [ObservableProperty] public partial double CornerRadius { get; set; } = 10;
    [ObservableProperty] public partial bool CellShadow { get; set; }
    // グリッドの下に敷くカード
    [ObservableProperty] public partial bool GridCard { get; set; }
    [ObservableProperty] public partial string GridCardColor { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial double GridCardOpacity { get; set; } = 80;

    // ---- 表情名 ----
    /// <summary>"none" / "below" / "inside"</summary>
    [ObservableProperty] public partial string LabelMode { get; set; } = "below";
    [ObservableProperty] public partial string LabelFont { get; set; } = "";
    [ObservableProperty] public partial bool LabelBold { get; set; } = true;
    /// <summary>セルの幅に対する文字の大きさ（%）。</summary>
    [ObservableProperty] public partial double LabelSize { get; set; } = 13;
    [ObservableProperty] public partial string LabelColor { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial bool LabelStroke { get; set; } = true;
    [ObservableProperty] public partial string LabelStrokeColor { get; set; } = "#2D2446";
    [ObservableProperty] public partial double LabelStrokeWidth { get; set; } = 18;
    [ObservableProperty] public partial string LabelBandColor { get; set; } = "#CC2D2446";
    /// <summary>A・B・C… の記号を付ける。</summary>
    [ObservableProperty] public partial bool LabelLetters { get; set; }

    // ---- タイトル ----
    [ObservableProperty] public partial bool ShowTitle { get; set; }
    [ObservableProperty] public partial string TitleText { get; set; } = "";
    /// <summary>小さく添える文字（例: Expression Sheet）。</summary>
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    /// <summary>plain（文字だけ）/ plate（色の板）/ bar（下線）/ vertical（縦書き）/ watermark（大きな透かし）</summary>
    [ObservableProperty] public partial string TitleStyle { get; set; } = "bar";
    /// <summary>grid（表情の並びの上）/ grid-below（並びの下）/ top-left / top-right / bottom-left / bottom-right</summary>
    [ObservableProperty] public partial string TitleAnchor { get; set; } = "grid";
    /// <summary>"left" / "center" / "right"（並びの上・下に置くときの揃え）</summary>
    [ObservableProperty] public partial string TitleAlign { get; set; } = "left";
    [ObservableProperty] public partial string TitleFont { get; set; } = "";
    [ObservableProperty] public partial bool TitleBold { get; set; } = true;
    [ObservableProperty] public partial double TitleSize { get; set; } = 72;
    [ObservableProperty] public partial string TitleColor { get; set; } = "#FFFFFF";
    [ObservableProperty] public partial bool TitleStroke { get; set; } = true;
    [ObservableProperty] public partial string TitleStrokeColor { get; set; } = "#2D2446";
    /// <summary>板・下線・透かしの色。</summary>
    [ObservableProperty] public partial string TitleAccentColor { get; set; } = "#7868AA";
    /// <summary>透かしの濃さ（%）。</summary>
    [ObservableProperty] public partial double WatermarkOpacity { get; set; } = 18;
    /// <summary>透かしの文字の高さ（画像の高さに対する %）。</summary>
    [ObservableProperty] public partial double WatermarkSize { get; set; } = 24;
    /// <summary>透かしの置き場所: sprite（立ち絵の後ろ）/ top / center / bottom / top-left / top-right / bottom-left / bottom-right</summary>
    [ObservableProperty] public partial string WatermarkPosition { get; set; } = "sprite";
    /// <summary>"fill"（塗り）/ "outline"（輪郭だけ）/ "both"（塗り＋輪郭）</summary>
    [ObservableProperty] public partial string WatermarkStyle { get; set; } = "both";
    /// <summary>透かしの色（空なら自動: 背景が明るければ濃い色、暗ければ明るい色）。</summary>
    [ObservableProperty] public partial string WatermarkColor { get; set; } = "";
    /// <summary>輪郭の色（空なら塗りと同じ色）。</summary>
    [ObservableProperty] public partial string WatermarkOutlineColor { get; set; } = "";
    /// <summary>輪郭の太さ（文字の大きさに対する %）。</summary>
    [ObservableProperty] public partial double WatermarkOutlineWidth { get; set; } = 1.8;
    /// <summary>背景の斜め帯などの角度に合わせて傾ける。</summary>
    [ObservableProperty] public partial bool WatermarkFollowBg { get; set; } = true;
    [ObservableProperty] public partial double WatermarkAngle { get; set; }
    [ObservableProperty] public partial double WatermarkOffsetX { get; set; }
    [ObservableProperty] public partial double WatermarkOffsetY { get; set; }
    /// <summary>立ち絵や表情の上に重ねる（ふつうは背景のすぐ上）。</summary>
    [ObservableProperty] public partial bool WatermarkFront { get; set; }

    // ---- クレジット（描いた人の名前など） ----
    [ObservableProperty] public partial bool ShowCredit { get; set; }
    /// <summary>自由に書けるクレジット（複数行可）。例: 「イラスト：○○」「衣装デザイン：○○」</summary>
    [ObservableProperty] public partial string CreditText { get; set; } = "";
    /// <summary>"top-left" / "top-right" / "bottom-left" / "bottom-right"</summary>
    [ObservableProperty] public partial string CreditPosition { get; set; } = "bottom-right";
    [ObservableProperty] public partial string CreditFont { get; set; } = "";
    [ObservableProperty] public partial bool CreditBold { get; set; }
    /// <summary>文字の大きさ（画像の幅を 1000 としたとき）。</summary>
    [ObservableProperty] public partial double CreditSize { get; set; } = 13;
    /// <summary>空なら自動（背景に合わせて濃い灰色か明るい灰色）。</summary>
    [ObservableProperty] public partial string CreditColor { get; set; } = "";
    [ObservableProperty] public partial double CreditOpacity { get; set; } = 80;
    /// <summary>読みやすいよう、文字の下に半透明の下地を敷く。</summary>
    [ObservableProperty] public partial bool CreditBackdrop { get; set; }

    // ---- 書き出し ----
    /// <summary>"png" / "jpg"</summary>
    [ObservableProperty] public partial string Format { get; set; } = "png";

    // ---- 複製 ----

    static readonly PropertyInfo[] Props = typeof(DisplaySpec)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>すべての値を other からコピーする（keep に挙げた名前は残す）。</summary>
    public void CopyFrom(DisplaySpec other, params string[] keep)
    {
        foreach (var p in Props)
            if (!keep.Contains(p.Name)) p.SetValue(this, p.GetValue(other));
    }

    public DisplaySpec Clone()
    {
        var c = new DisplaySpec();
        c.CopyFrom(this);
        return c;
    }

    /// <summary>テンプレートを当てても変えない項目（画像の選び方や文字など、素材に関わるもの）。</summary>
    [JsonIgnore]
    public static readonly string[] ContentProps =
        [nameof(SpritePath), nameof(GhostPath), nameof(TitleText), nameof(Subtitle), nameof(ShowTitle), nameof(BgImagePath),
         nameof(Width), nameof(Format), nameof(LabelFont), nameof(TitleFont),
         nameof(ShowCredit), nameof(CreditText), nameof(CreditPosition), nameof(CreditFont), nameof(CreditBold),
         nameof(CreditSize), nameof(CreditColor), nameof(CreditOpacity), nameof(CreditBackdrop)];

    /// <summary>色の項目（キャラの色に合わせるときに書き換える）。</summary>
    static readonly PropertyInfo[] ColorProps = Props
        .Where(p => p.PropertyType == typeof(string) && (p.Name.Contains("Color") || p.Name is nameof(CellBg)))
        .ToArray();

    /// <summary>
    /// 今の配色をキャラの色に合わせて塗り替える。配色全体の色相を、基準の色が main の色相に来るように回す
    /// （差し色との関係はそのまま）。基準は面積の大きい地の色、淡すぎるときは模様の色。
    /// </summary>
    public void Recolor(SkiaSharp.SKColor main)
    {
        static bool Usable(string hex, bool allowLight)
        {
            if (!SkiaSharp.SKColor.TryParse(hex, out var c)) return false;
            var h = TachieKara.Services.ColorAnalyzer.ToHsl(c);
            return h.S >= 0.12 && (allowLight || h.L <= 0.88);
        }
        string? baseHex = Usable(BgColor1, false) ? BgColor1
            : Usable(BgColor2, true) ? BgColor2
            : Usable(BgColor1, true) ? BgColor1
            : ColorProps.Select(p => (string?)p.GetValue(this) ?? "").FirstOrDefault(h => Usable(h, true));
        if (baseHex is null) return;
        var baseHue = TachieKara.Services.ColorAnalyzer.ToHsl(SkiaSharp.SKColor.Parse(baseHex)).H;
        var shift = TachieKara.Services.ColorAnalyzer.ToHsl(main).H - baseHue;
        foreach (var p in ColorProps)
            p.SetValue(this, TachieKara.Services.ColorAnalyzer.Rotate((string?)p.GetValue(this) ?? "", shift, main));
    }
}
