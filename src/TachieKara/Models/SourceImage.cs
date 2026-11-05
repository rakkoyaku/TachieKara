using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TachieKara.Models;

/// <summary>取り込んだ立ち絵 1 枚。</summary>
public sealed partial class SourceImage : ObservableObject
{
    public SourceImage(string path, int width, int height, long fileSize)
    {
        Path = path;
        Width = width;
        Height = height;
        FileSize = fileSize;
    }

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string FolderName => System.IO.Path.GetFileName(Folder);
    public int Width { get; }
    public int Height { get; }
    public long FileSize { get; }
    public string SizeText => Width > 0 ? $"{Width} × {Height}" : "-";
    public string FileSizeText => FileSize >= 1024 * 1024
        ? $"{FileSize / 1024.0 / 1024.0:0.0} MB"
        : $"{FileSize / 1024.0:0} KB";

    /// <summary>一覧の 2 行目に出す基本情報。名前を付けたときは元のファイル名も出す。</summary>
    public string MetaText => HasCustomName
        ? (Width > 0 ? $"{FileName}  ·  {Width}×{Height}" : FileName)
        : (Width > 0 ? $"{Width}×{Height}  ·  {FileSizeText}" : FileSizeText);

    /// <summary>拡張子を除いた元のファイル名。</summary>
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>ユーザーが付けた名前（表情名）。null なら元のファイル名を使う。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputName), nameof(HasCustomName), nameof(DisplayName), nameof(MetaText))]
    public partial string? CustomName { get; set; }

    /// <summary>書き出すファイル名（拡張子・前後の文字なし）。</summary>
    public string OutputName => HasCustomName ? CustomName!.Trim() : Stem;
    public bool HasCustomName => !string.IsNullOrWhiteSpace(CustomName);
    /// <summary>一覧の 1 行目。名前を付けていればその名前、なければファイル名。</summary>
    public string DisplayName => HasCustomName ? CustomName!.Trim() : FileName;

    /// <summary>ディスプレイ画像に載せるか。</summary>
    [ObservableProperty] public partial bool InDisplay { get; set; } = true;

    // 一覧での名前の編集
    [ObservableProperty] public partial bool IsRenaming { get; set; }
    [ObservableProperty] public partial string EditName { get; set; } = "";

    /// <summary>一覧のサムネイルと切り抜き確認に使う縮小画像。</summary>
    public SKBitmap? Proxy { get; set; }
    public double ProxyScale => Proxy is null ? 0 : (double)Proxy.Width / Width;

    [ObservableProperty] public partial AvBitmap? Thumbnail { get; set; }
    [ObservableProperty] public partial AvBitmap? CropThumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph), nameof(StatusTip), nameof(IsWarning), nameof(HasBadge), nameof(BadgeText))]
    public partial bool IsBase { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph), nameof(StatusTip), nameof(IsWarning), nameof(HasBadge), nameof(BadgeText))]
    public partial bool IsMismatch { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph), nameof(StatusTip), nameof(IsWarning), nameof(IsError), nameof(HasBadge), nameof(BadgeText))]
    public partial string? Error { get; set; }

    /// <summary>ベース画像のサイズ（警告文に使う）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTip))]
    public partial string BaseSizeText { get; set; } = "";

    public bool IsError => Error is not null;
    public bool HasBadge => IsBase || IsWarning || IsError;
    public string BadgeText => IsError ? "読込エラー" : IsBase ? "ベース" : IsWarning ? "サイズ違い" : "";
    public bool IsWarning => !IsError && !IsBase && IsMismatch;

    public string StatusGlyph => IsError ? "✕" : IsBase ? "★" : IsMismatch ? "⚠" : "";

    public string StatusTip => IsError
        ? $"読み込めません: {Error}\nこのファイルは書き出されません。"
        : IsBase
            ? "ベース画像（プレビューと切り抜き位置の基準）"
            : IsMismatch
                ? $"ベースと縦横サイズが違います（ベース {BaseSizeText} / この画像 {SizeText}）。\n同じピクセル位置で切り抜くため、顔の位置がずれる可能性があります。"
                : "ベースと同じサイズ";
}
