using TachieKara.Models;

namespace TachieKara.Services;

/// <summary>最初から用意しているディスプレイ画像のテンプレート。</summary>
public static class DisplayTemplates
{
    public sealed record Template(string Name, Func<DisplaySpec> Create);

    public static readonly IReadOnlyList<Template> BuiltIn =
    [
        new("斜め帯", () => new DisplaySpec { TitleStyle = "bar", TitleAccentColor = "#FFFFFF" }),   // ほかは既定値
        new("シンプル", () => new DisplaySpec
        {
            BgKind = "solid", BgColor1 = "#F4F2F7",
            SpriteShadow = false, SpriteOutline = false,
            FrameWidth = 0, CornerRadius = 14, CellBg = "#FFFFFF", CellShadow = true,
            LabelColor = "#3A3350", LabelStroke = false, LabelSize = 11,
            TitleStyle = "bar", TitleColor = "#3A3350", TitleStroke = false, TitleAccentColor = "#8E78F0",
        }),
        new("カード", () => new DisplaySpec
        {
            BgKind = "gradient", BgColor1 = "#FBD3E9", BgColor2 = "#BBA6F5", BgAngle = 30,
            GridCard = true, GridCardColor = "#FFFFFF", GridCardOpacity = 85,
            FrameWidth = 0, CornerRadius = 16, CellBg = "#F3EEFB",
            LabelColor = "#4A3B6B", LabelStroke = false, LabelSize = 11,
            SpriteOutlineColor = "#FFFFFF", SpriteShadowColor = "#6B4FD8",
            TitleStyle = "plate", TitleColor = "#FFFFFF", TitleAccentColor = "#8E6FE0",
        }),
        new("ドット", () => new DisplaySpec
        {
            BgKind = "dots", BgColor1 = "#FFF4D6", BgColor2 = "#FF9EB5", BgSize = 46, PatternOpacity = 45, BgAngle = 0,
            FrameColor = "#5B3A4A", FrameWidth = 4, CornerRadius = 22, CellBg = "#FFFFFF",
            LabelColor = "#FFFFFF", LabelStrokeColor = "#5B3A4A",
            SpriteShadowColor = "#5B3A4A",
            TitleStyle = "plate", TitleColor = "#FFFFFF", TitleAccentColor = "#FF7FA0", TitleStrokeColor = "#5B3A4A",
        }),
        new("方眼", () => new DisplaySpec
        {
            BgKind = "grid", BgColor1 = "#F7F7F2", BgColor2 = "#7FA7C9", BgSize = 50, PatternOpacity = 30, BgAngle = 0,
            InnerFrame = true, InnerFrameColor = "#33475B", InnerFrameWidth = 3,
            FrameColor = "#33475B", FrameWidth = 3, CornerRadius = 4, CellBg = "#E9EEF3",
            LabelMode = "inside", LabelBandColor = "#CC33475B", LabelStroke = false, LabelSize = 10,
            SpriteShadowColor = "#33475B",
            TitleStyle = "bar", TitleColor = "#33475B", TitleStroke = false, TitleAccentColor = "#7FA7C9",
        }),
        new("ストライプ", () => new DisplaySpec
        {
            BgKind = "stripes", BgColor1 = "#E8F6F3", BgColor2 = "#7ED3C1", BgSize = 70, PatternOpacity = 35, BgAngle = 0,
            FrameColor = "#1F4E47", FrameWidth = 3, CornerRadius = 12, CellBg = "#FFFFFF",
            LabelStrokeColor = "#1F4E47", SpriteShadowColor = "#1F4E47",
            TitleStyle = "plain", TitleStrokeColor = "#1F4E47", TitleAccentColor = "#7ED3C1",
        }),
        new("斜め分割", () => new DisplaySpec
        {
            BgKind = "split", BgColor1 = "#F2EEE6", BgColor2 = "#3E4A61", BgColor3 = "#E8B04A", BgAngle = 18, BgOffset = -6,
            FrameColor = "#E8B04A", FrameWidth = 3, CornerRadius = 6, CellBg = "#2E3749",
            LabelStrokeColor = "#1E2533", SpriteShadowColor = "#1E2533",
            TitleStyle = "vertical", TitleStrokeColor = "#1E2533", TitleAccentColor = "#E8B04A",
        }),
        new("ダーク", () => new DisplaySpec
        {
            BgKind = "radial", BgColor1 = "#14121C", BgColor2 = "#3B2F63", BgSize = 70,
            Grain = true, GrainAmount = 20, Vignette = true,
            FrameColor = "#8E78F0", FrameWidth = 3, CornerRadius = 12, CellBg = "#221E30",
            LabelColor = "#FFFFFF", LabelStrokeColor = "#14121C",
            SpriteOutlineColor = "#FFFFFF", SpriteShadowColor = "#000000", SpriteShadowOpacity = 55,
            TitleStyle = "watermark", TitleAccentColor = "#8E78F0", TitleStrokeColor = "#14121C",
        }),
        new("サンバースト", () => new DisplaySpec
        {
            BgKind = "sunburst", BgColor1 = "#FFE9A8", BgColor2 = "#FFB347", PatternOpacity = 45, BgSize = 48, BgOffset = -22,
            FrameColor = "#7A4B12", FrameWidth = 4, CornerRadius = 14, CellBg = "#FFFFFF",
            LabelStrokeColor = "#7A4B12", SpriteShadowColor = "#7A4B12",
            TitleStyle = "plate", TitleAccentColor = "#E8833A", TitleStrokeColor = "#5A3209",
        }),
        new("ハーフトーン", () => new DisplaySpec
        {
            BgKind = "halftone", BgColor1 = "#F3F6FF", BgColor2 = "#5B7BD5", PatternOpacity = 30, BgSize = 40, BgAngle = 20,
            FrameColor = "#24345E", FrameWidth = 3, CornerRadius = 8, CellBg = "#FFFFFF",
            LabelStrokeColor = "#24345E", SpriteShadowColor = "#24345E",
            TitleStyle = "bar", TitleStrokeColor = "#24345E", TitleAccentColor = "#5B7BD5",
        }),
        new("ギンガム", () => new DisplaySpec
        {
            BgKind = "gingham", BgColor1 = "#FFFFFF", BgColor2 = "#F29CB7", PatternOpacity = 45, BgSize = 60, BgAngle = 0,
            FrameColor = "#7C3B52", FrameWidth = 4, CornerRadius = 18, CellBg = "#FFF5F8",
            LabelStrokeColor = "#7C3B52", SpriteShadowColor = "#7C3B52",
            TitleStyle = "plate", TitleAccentColor = "#F27AA0", TitleStrokeColor = "#5E2338",
        }),
        new("ウェーブ", () => new DisplaySpec
        {
            BgKind = "waves", BgColor1 = "#E6F7FB", BgColor2 = "#7ACCE0", PatternOpacity = 40, BgSize = 48, BgAngle = 0,
            FrameColor = "#1D5A6B", FrameWidth = 3, CornerRadius = 20, CellBg = "#FFFFFF",
            LabelStrokeColor = "#1D5A6B", SpriteShadowColor = "#1D5A6B",
            TitleStyle = "plain", TitleStrokeColor = "#1D5A6B", TitleAccentColor = "#7ACCE0",
        }),
        new("ハニカム", () => new DisplaySpec
        {
            BgKind = "hex", BgColor1 = "#FFF8E6", BgColor2 = "#E0A93B", PatternOpacity = 40, BgSize = 40, BgAngle = 0,
            FrameColor = "#5C3D0E", FrameWidth = 3, CornerRadius = 10, CellBg = "#FFFFFF",
            LabelStrokeColor = "#5C3D0E", SpriteShadowColor = "#5C3D0E",
            TitleStyle = "bar", TitleStrokeColor = "#5C3D0E", TitleAccentColor = "#E0A93B",
        }),
        new("星空", () => new DisplaySpec
        {
            BgKind = "stars", BgColor1 = "#1B1D3F", BgColor2 = "#FFE9A6", PatternOpacity = 70, BgSize = 40, Vignette = true,
            FrameColor = "#FFE9A6", FrameWidth = 3, CornerRadius = 12, CellBg = "#2A2D5A",
            LabelColor = "#FFFFFF", LabelStrokeColor = "#12142E",
            SpriteShadowColor = "#000000", SpriteShadowOpacity = 50,
            TitleStyle = "plate", TitleAccentColor = "#FFE9A6", TitleStrokeColor = "#12142E",
        }),
        new("同心円", () => new DisplaySpec
        {
            BgKind = "circles", BgColor1 = "#F6EEFF", BgColor2 = "#C9B3F2", PatternOpacity = 45, BgSize = 70, BgOffset = -22,
            FrameColor = "#4A3480", FrameWidth = 3, CornerRadius = 40, CellBg = "#FFFFFF",
            LabelStrokeColor = "#4A3480", SpriteShadowColor = "#4A3480",
            TitleStyle = "bar", TitleStrokeColor = "#4A3480", TitleAccentColor = "#9C7BE8",
        }),
        new("シェブロン", () => new DisplaySpec
        {
            BgKind = "chevron", BgColor1 = "#EEF8EE", BgColor2 = "#8FD19E", PatternOpacity = 35, BgSize = 44, BgAngle = 0,
            FrameColor = "#2E5D3A", FrameWidth = 3, CornerRadius = 10, CellBg = "#FFFFFF",
            LabelStrokeColor = "#2E5D3A", SpriteShadowColor = "#2E5D3A",
            TitleStyle = "plate", TitleAccentColor = "#5FB574", TitleStrokeColor = "#1F4228",
        }),
        new("集中線", () => new DisplaySpec
        {
            BgKind = "speedlines", BgColor1 = "#FFFFFF", BgColor2 = "#2B2B33", PatternOpacity = 85, BgSize = 80, BgOffset = -22,
            FrameColor = "#2B2B33", FrameWidth = 5, CornerRadius = 4, CellBg = "#F2F2F2",
            LabelStrokeColor = "#2B2B33", SpriteShadowColor = "#2B2B33",
            TitleStyle = "vertical", TitleStrokeColor = "#2B2B33", TitleAccentColor = "#2B2B33",
        }),
    ];
}
