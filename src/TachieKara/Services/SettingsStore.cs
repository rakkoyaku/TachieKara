using System.Text.Json;

namespace TachieKara.Services;

public sealed record CropRect(int X, int Y, int Size);
public sealed record BustRect(int X, int Y, int Width, int Height);

public sealed class AppSettings
{
    public int OutputSize { get; set; } = 256;
    public bool KeepCropSize { get; set; }
    public string OutFolderName { get; set; } = "顔アイコン";
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public bool UseBackground { get; set; }
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public bool ShowGuides { get; set; } = true;
    public bool GuideCenter { get; set; } = true;
    public bool GuideThirds { get; set; } = true;
    public bool GuideCircle { get; set; }

    /// <summary>"face"（顔アイコン）または "bust"（バストアップ）。</summary>
    public string Mode { get; set; } = "face";
    public string BustFolderName { get; set; } = "バストアップ";
    /// <summary>最後に使ったバストアップの切り抜きサイズ。新しい立ち絵セットの初期値に使う。</summary>
    public int BustWidth { get; set; }
    public int BustHeight { get; set; }
    public Dictionary<string, BustRect> BustCrops { get; set; } = new();

    /// <summary>画像ごとに付けた名前（表情名）。キー: 画像のフルパス（小文字）。</summary>
    public Dictionary<string, string> Names { get; set; } = new();

    /// <summary>ディスプレイ画像の今の設定。</summary>
    public TachieKara.Models.DisplaySpec? Display { get; set; }
    /// <summary>保存したディスプレイ画像のテンプレート（名前 → 設定）。</summary>
    public Dictionary<string, TachieKara.Models.DisplaySpec> DisplayTemplates { get; set; } = new();
    /// <summary>ディスプレイ画像に載せない画像（NameKey の形）。</summary>
    public HashSet<string> DisplayExcluded { get; set; } = new();
    /// <summary>テンプレートを選んだときにキャラの色へ自動で合わせる。</summary>
    public bool DisplayAutoColor { get; set; } = true;
    /// <summary>起動時に新しいバージョンを確認する。</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    public static string NameKey(string path) => Path.GetFullPath(path).ToLowerInvariant();

    /// <summary>キー: "ベース画像のフォルダ|幅x高さ"。同じ立ち絵セットを開き直したとき枠を復元する。</summary>
    public Dictionary<string, CropRect> Crops { get; set; } = new();

    public static string CropKey(string folder, int w, int h) =>
        $"{Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()}|{w}x{h}";
}

public static class SettingsStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
    };

    /// <summary>設定ファイルの場所。TACHIEKARA_SETTINGS_DIR を指定するとそこを使う（動作確認で本来の設定を汚さないため）。</summary>
    static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("TACHIEKARA_SETTINGS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TachieKara"),
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch { /* 壊れていたら初期値で起動 */ }
        return new();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Options));
        }
        catch { /* 設定が保存できなくても本処理は止めない */ }
    }

    public static void WriteCropJson(string dir, object payload) =>
        File.WriteAllText(Path.Combine(dir, "crop.json"), JsonSerializer.Serialize(payload, Options));
}
