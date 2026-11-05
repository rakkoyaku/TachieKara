using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using TachieKara.Models;
using TachieKara.Services;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TachieKara.ViewModels;

/// <summary>ディスプレイ画像（立ち絵＋表情一覧を 1 枚に並べた画像）の作成。</summary>
public sealed partial class MainViewModel
{
    const int DisplayPreviewMaxWidth = 1600;

    /// <summary>ディスプレイ画像の設定（右パネルの各項目がここに直接つながる）。</summary>
    public DisplaySpec Display { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCropMode), nameof(ExportButtonText), nameof(ExportSummary), nameof(Mode))]
    public partial bool IsDisplay { get; set; }
    public bool IsCropMode => !IsDisplay;

    [ObservableProperty] public partial AvBitmap? DisplayPreview { get; set; }
    [ObservableProperty] public partial bool IsRenderingDisplay { get; set; }
    [ObservableProperty] public partial string DisplaySizeText { get; set; } = "";

    /// <summary>テンプレートの一覧（用意したもの＋保存したもの）。</summary>
    public ObservableCollection<string> TemplateNames { get; } = new();
    [ObservableProperty] public partial string NewTemplateName { get; set; } = "";
    [ObservableProperty] public partial string? SelectedTemplate { get; set; }
    public bool CanDeleteTemplate => SelectedTemplate is { } n && _settings.DisplayTemplates.ContainsKey(n);

    /// <summary>OS に入っているフォント（先頭は「標準」）。日本語を表示できるかも調べておく。</summary>
    List<(string Name, bool Japanese)> _allFonts = [(DefaultFontLabel, true)];
    const string DefaultFontLabel = "（標準）";

    /// <summary>選べるフォント（「日本語フォントのみ」のときは日本語を表示できるものだけ）。</summary>
    [ObservableProperty] public partial IReadOnlyList<string> FontChoices { get; set; } = [DefaultFontLabel];
    [ObservableProperty] public partial bool OnlyJapaneseFonts { get; set; } = true;
    partial void OnOnlyJapaneseFontsChanged(bool value) => RefreshFontChoices();

    void RefreshFontChoices()
    {
        var list = _allFonts.Where(f => !OnlyJapaneseFonts || f.Japanese).Select(f => f.Name).ToList();
        // 今選んでいるフォントは一覧から消さない
        foreach (var cur in new[] { Display.LabelFont, Display.TitleFont, Display.CreditFont })
            if (!string.IsNullOrEmpty(cur) && !list.Contains(cur)) list.Insert(1, cur);
        FontChoices = list;
        // 一覧を差し替えるとコンボボックスの選択が外れるので、一覧の反映が済んでから選び直させる
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(LabelFontChoice));
            OnPropertyChanged(nameof(TitleFontChoice));
            OnPropertyChanged(nameof(CreditFontChoice));
        }, DispatcherPriority.Background);
    }

    public string LabelFontChoice
    {
        get => string.IsNullOrEmpty(Display.LabelFont) ? DefaultFontLabel : Display.LabelFont;
        set { if (value is not null) Display.LabelFont = value == DefaultFontLabel ? "" : value; }
    }

    public string CreditFontChoice
    {
        get => string.IsNullOrEmpty(Display.CreditFont) ? DefaultFontLabel : Display.CreditFont;
        set { if (value is not null) Display.CreditFont = value == DefaultFontLabel ? "" : value; }
    }

    /// <summary>クレジットに書き出しを足す（例: 「イラスト：」）。最後の行が空でなければ改行してから足す。</summary>
    [RelayCommand]
    void InsertCredit(string prefix)
    {
        var t = Display.CreditText ?? "";
        if (t.Length > 0 && !t.EndsWith('\n')) t += "\n";
        Display.CreditText = t + prefix;
        Display.ShowCredit = true;
    }

    public string TitleFontChoice
    {
        get => string.IsNullOrEmpty(Display.TitleFont) ? DefaultFontLabel : Display.TitleFont;
        set { if (value is not null) Display.TitleFont = value == DefaultFontLabel ? "" : value; }
    }

    // ---- キャラの色 ----

    public sealed record Swatch(string Hex, Avalonia.Media.IBrush Brush);

    /// <summary>立ち絵から取り出した色（目立つ順）。</summary>
    public ObservableCollection<Swatch> TradeColors { get; } = new();
    public bool HasTradeColors => TradeColors.Count > 0;
    /// <summary>テンプレートを選んだときにキャラの色へ自動で合わせる。</summary>
    [ObservableProperty] public partial bool AutoMatchColors { get; set; } = true;
    string? _paletteFor;

    /// <summary>立ち絵・奥の立ち絵として選べる画像。</summary>
    public IEnumerable<SourceImage> DisplayChoices => Images.Where(i => !i.IsError);

    public SourceImage? DisplaySpriteItem
    {
        get => FindImage(Display.SpritePath) ?? BaseImage;
        set => Display.SpritePath = value?.Path ?? "";
    }

    public SourceImage? DisplayGhostItem
    {
        get => FindImage(Display.GhostPath) ?? DisplaySpriteItem;
        set => Display.GhostPath = value?.Path ?? "";
    }

    public string BgImageName => string.IsNullOrEmpty(Display.BgImagePath) ? "未選択" : Path.GetFileName(Display.BgImagePath);

    public int DisplayCount => Images.Count(i => !i.IsError && i.InDisplay);

    public string DisplaySummary => $"{DisplayCount}表情  ·  {DisplaySizeText}  ·  {Display.Format.ToUpperInvariant()}";

    // 背景の種類ごとに出す設定
    public bool BgUsesColor2 => Display.BgKind is not ("solid" or "image");
    public bool BgUsesAngle => Display.BgKind is "gradient" or "band" or "split" or "stripes" or "dots" or "grid" or "checker"
        or "sunburst" or "halftone" or "gingham" or "waves" or "hex" or "chevron";
    public bool BgUsesSize => Display.BgKind is "radial" or "band" or "stripes" or "dots" or "grid" or "checker"
        or "sunburst" or "halftone" or "gingham" or "waves" or "hex" or "stars" or "circles" or "chevron" or "speedlines";
    public bool BgUsesOffset => Display.BgKind is "gradient" or "radial" or "band" or "split" or "sunburst" or "halftone" or "circles" or "speedlines";
    public bool BgUsesPatternOpacity => Display.BgKind is "stripes" or "dots" or "grid" or "checker"
        or "sunburst" or "halftone" or "gingham" or "waves" or "hex" or "stars" or "circles" or "chevron" or "speedlines";
    public bool BgUsesEdgeLines => Display.BgKind is "band" or "split";
    public bool BgIsImage => Display.BgKind == "image";
    public string BgSizeLabel => Display.BgKind switch
    {
        "band" => "帯の太さ",
        "radial" => "広がり",
        "sunburst" => "光の筋の数",
        "speedlines" => "線の数",
        _ => "模様の大きさ",
    };
    public string BgOffsetLabel => Display.BgKind switch
    {
        "band" => "帯の位置",
        "split" => "境目の位置",
        _ => "中心の位置",
    };
    public bool LabelIsInside => Display.LabelMode == "inside";
    public bool TitleUsesAlign => Display.TitleAnchor is "grid" or "grid-below" && Display.TitleStyle is not ("vertical" or "watermark");
    public bool TitleUsesAccent => Display.TitleStyle is "bar" or "plate" or "watermark";
    public bool TitleUsesStroke => Display.TitleStyle is "plain" or "bar" or "vertical";
    public bool TitleIsWatermark => Display.TitleStyle == "watermark";
    public bool TitleIsNotWatermark => Display.TitleStyle != "watermark";
    public bool LabelVisible => Display.LabelMode != "none";

    readonly Dictionary<string, SKBitmap> _bgPreviewCache = new();
    DispatcherTimer? _displayDebounce;
    CancellationTokenSource? _displayCts;
    bool _displayReady;

    void InitDisplay()
    {
        if (_settings.Display is { } saved) Display.CopyFrom(saved);
        AutoMatchColors = _settings.DisplayAutoColor;
        _ = LoadFontsAsync();
        Display.PropertyChanged += OnDisplaySpecChanged;
        RefreshTemplateNames();
        Images.CollectionChanged += OnImagesChangedForDisplay;
        _displayReady = true;
    }

    void OnImagesChangedForDisplay(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (SourceImage item in e.NewItems)
            {
                item.InDisplay = !_settings.DisplayExcluded.Contains(AppSettings.NameKey(item.Path));
                item.PropertyChanged += OnItemChangedForDisplay;
            }
        if (e.OldItems is not null)
            foreach (SourceImage item in e.OldItems)
                item.PropertyChanged -= OnItemChangedForDisplay;
        RaiseDisplayChoices();
        ScheduleDisplayRender();
    }

    void OnItemChangedForDisplay(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SourceImage item) return;
        if (e.PropertyName == nameof(SourceImage.InDisplay))
        {
            var key = AppSettings.NameKey(item.Path);
            if (item.InDisplay) _settings.DisplayExcluded.Remove(key); else _settings.DisplayExcluded.Add(key);
            OnPropertyChanged(nameof(DisplayCount));
            OnPropertyChanged(nameof(ExportSummary));
            ScheduleDisplayRender();
        }
        else if (e.PropertyName is nameof(SourceImage.CustomName) or nameof(SourceImage.Error))
        {
            ScheduleDisplayRender();
        }
    }

    void RaiseDisplayChoices()
    {
        OnPropertyChanged(nameof(DisplayChoices));
        OnPropertyChanged(nameof(DisplaySpriteItem));
        OnPropertyChanged(nameof(DisplayGhostItem));
        OnPropertyChanged(nameof(DisplayCount));
    }

    void OnDisplaySpecChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySpec.BgKind):
                foreach (var n in new[] { nameof(BgUsesColor2), nameof(BgUsesAngle), nameof(BgUsesSize), nameof(BgUsesOffset),
                             nameof(BgUsesPatternOpacity), nameof(BgUsesEdgeLines), nameof(BgIsImage), nameof(BgSizeLabel), nameof(BgOffsetLabel) })
                    OnPropertyChanged(n);
                break;
            case nameof(DisplaySpec.LabelMode):
                OnPropertyChanged(nameof(LabelIsInside));
                OnPropertyChanged(nameof(LabelVisible));
                break;
            case nameof(DisplaySpec.LabelFont):
                OnPropertyChanged(nameof(LabelFontChoice));
                break;
            case nameof(DisplaySpec.TitleFont):
                OnPropertyChanged(nameof(TitleFontChoice));
                break;
            case nameof(DisplaySpec.CreditFont):
                OnPropertyChanged(nameof(CreditFontChoice));
                break;
            case nameof(DisplaySpec.TitleAnchor) or nameof(DisplaySpec.TitleStyle):
                OnPropertyChanged(nameof(TitleUsesAlign));
                OnPropertyChanged(nameof(TitleUsesAccent));
                OnPropertyChanged(nameof(TitleUsesStroke));
                OnPropertyChanged(nameof(TitleIsWatermark));
                OnPropertyChanged(nameof(TitleIsNotWatermark));
                break;
            case nameof(DisplaySpec.SpritePath):
                _ = UpdatePaletteAsync();
                OnPropertyChanged(nameof(DisplaySpriteItem));
                OnPropertyChanged(nameof(DisplayGhostItem));
                break;
            case nameof(DisplaySpec.GhostPath):
                OnPropertyChanged(nameof(DisplayGhostItem));
                break;
            case nameof(DisplaySpec.BgImagePath):
                OnPropertyChanged(nameof(BgImageName));
                break;
            case nameof(DisplaySpec.Format):
                OnPropertyChanged(nameof(ExportSummary));
                break;
        }
        ScheduleDisplayRender();
    }

    partial void OnIsDisplayChanged(bool value)
    {
        OnPropertyChanged(nameof(Mode));
        if (value) _ = UpdatePaletteAsync();
        if (value) ScheduleDisplayRender(immediate: true);
    }

    partial void OnSelectedTemplateChanged(string? value) => OnPropertyChanged(nameof(CanDeleteTemplate));

    SourceImage? FindImage(string? path) =>
        string.IsNullOrEmpty(path) ? null : Images.FirstOrDefault(i => !i.IsError && PathComparer.Equals(i.Path, path));

    // ---- テンプレート ----

    void RefreshTemplateNames()
    {
        TemplateNames.Clear();
        foreach (var t in DisplayTemplates.BuiltIn) TemplateNames.Add(t.Name);
        foreach (var n in _settings.DisplayTemplates.Keys.OrderBy(k => k, NaturalComparer.Instance))
            if (!TemplateNames.Contains(n)) TemplateNames.Add(n);
        OnPropertyChanged(nameof(CanDeleteTemplate));
    }

    [RelayCommand]
    void ApplyTemplate(string? name)
    {
        name ??= SelectedTemplate;
        if (name is null) return;
        DisplaySpec? spec = _settings.DisplayTemplates.TryGetValue(name, out var saved)
            ? saved
            : DisplayTemplates.BuiltIn.FirstOrDefault(t => t.Name == name)?.Create();
        if (spec is null) return;
        // 素材に関わる項目（立ち絵の選択・タイトル文字・幅など）はそのまま残す
        Display.CopyFrom(spec, DisplaySpec.ContentProps);
        // 用意したテンプレートは、キャラの色に合わせて塗り替える（保存したテンプレートはそのままの色で）
        if (AutoMatchColors && !_settings.DisplayTemplates.ContainsKey(name) && TradeColors.Count > 0)
            MatchColors(null);
        SelectedTemplate = name;
        RaiseAllDisplayDerived();
    }

    [RelayCommand]
    void SaveTemplate()
    {
        var name = NewTemplateName.Trim();
        if (name.Length == 0) name = $"マイテンプレート {_settings.DisplayTemplates.Count + 1}";
        if (DisplayTemplates.BuiltIn.Any(t => t.Name == name)) name += "（保存）";
        _settings.DisplayTemplates[name] = Display.Clone();
        SettingsStore.Save(_settings);
        RefreshTemplateNames();
        SelectedTemplate = name;
        NewTemplateName = "";
        StatusText = $"テンプレート「{name}」を保存しました。";
    }

    [RelayCommand]
    void DeleteTemplate()
    {
        if (SelectedTemplate is not { } name || !_settings.DisplayTemplates.Remove(name)) return;
        SettingsStore.Save(_settings);
        RefreshTemplateNames();
        SelectedTemplate = null;
    }

    void RaiseAllDisplayDerived()
    {
        foreach (var n in new[] { nameof(BgUsesColor2), nameof(BgUsesAngle), nameof(BgUsesSize), nameof(BgUsesOffset),
                     nameof(BgUsesPatternOpacity), nameof(BgUsesEdgeLines), nameof(BgIsImage), nameof(BgSizeLabel), nameof(BgOffsetLabel),
                     nameof(LabelIsInside), nameof(LabelVisible), nameof(LabelFontChoice), nameof(TitleFontChoice),
                     nameof(TitleUsesAlign), nameof(TitleUsesAccent), nameof(TitleUsesStroke), nameof(TitleIsWatermark), nameof(TitleIsNotWatermark), nameof(DisplaySpriteItem), nameof(DisplayGhostItem),
                     nameof(BgImageName), nameof(ExportSummary) })
            OnPropertyChanged(n);
    }

    // ---- 背景画像 ----

    [RelayCommand]
    async Task PickBackgroundImage()
    {
        if (Dialogs is null) return;
        var path = await Dialogs.PickImageAsync("背景にする画像を選択");
        if (path is null) return;
        Display.BgImagePath = path;
        Display.BgKind = "image";
    }

    [RelayCommand]
    void ResetDisplayOffsets()
    {
        Display.SpriteOffsetX = 0;
        Display.SpriteOffsetY = 0;
        Display.SpriteScale = 100;
    }

    // ---- 描画 ----

    void ScheduleDisplayRender(bool immediate = false)
    {
        if (!_displayReady || !IsDisplay) return;
        _displayDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            _displayDebounce!.Stop();
            _ = RenderDisplayPreviewAsync();
        });
        _displayDebounce.Stop();
        if (immediate) _ = RenderDisplayPreviewAsync();
        else _displayDebounce.Start();
    }

    DisplayInput? BuildDisplayInput(DisplaySpec spec)
    {
        if (BaseImage is null) return null;
        // バストアップを表示していた場合は、しまってある顔アイコンの枠を使う
        var face = IsBust ? _faceRect : new CropRect(CropX, CropY, CropW);
        var cells = Images.Where(i => !i.IsError && i.InDisplay)
            .Select(i => new DisplayCell(i.Path, i.OutputName)).ToList();
        var sprite = DisplaySpriteItem ?? BaseImage;
        return new DisplayInput
        {
            Spec = spec,
            SpritePath = sprite.Path,
            GhostPath = DisplayGhostItem?.Path ?? sprite.Path,
            Cells = cells,
            BaseWidth = BaseImage.Width,
            BaseHeight = BaseImage.Height,
            FaceCrop = SKRect.Create(face.X, face.Y, face.Size, face.Size),
        };
    }

    async Task RenderDisplayPreviewAsync()
    {
        _displayCts?.Cancel();
        var cts = _displayCts = new CancellationTokenSource();
        var spec = Display.Clone();
        var input = BuildDisplayInput(spec);
        _ = UpdatePaletteAsync();
        if (input is null)
        {
            DisplayPreview = null;
            DisplaySizeText = "";
            return;
        }
        var (w, h) = DisplayRenderer.CanvasSize(spec, input.BaseWidth, input.BaseHeight, input.Cells.Count);
        DisplaySizeText = $"{w} × {h} px";
        OnPropertyChanged(nameof(ExportSummary));
        var scale = Math.Min(1.0, DisplayPreviewMaxWidth / (double)w);
        var source = new PreviewSource(this);
        IsRenderingDisplay = true;
        try
        {
            var bmp = await Task.Run(() =>
            {
                using var sk = DisplayRenderer.Render(input, source, scale, cts.Token);
                return ImageOps.ToAvalonia(sk);
            }, cts.Token);
            if (!cts.IsCancellationRequested) DisplayPreview = bmp;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"ディスプレイ画像のプレビューを作れませんでした: {ex.Message}";
        }
        finally
        {
            if (_displayCts == cts) IsRenderingDisplay = false;
        }
    }

    /// <summary>プレビュー用: 一覧の縮小画像を使う。</summary>
    sealed class PreviewSource(MainViewModel vm) : IDisplayImageSource
    {
        readonly Dictionary<string, (SKBitmap Bmp, double Scale)> _items = vm.Images
            .Where(i => i.Proxy is not null)
            .GroupBy(i => i.Path, PathComparer)
            .ToDictionary(g => g.Key, g => (g.First().Proxy!, g.First().ProxyScale), PathComparer);

        public SKBitmap? Get(string path, out double scale)
        {
            if (_items.TryGetValue(path, out var v)) { scale = v.Scale; return v.Bmp; }
            scale = 1;
            return vm.GetBackgroundPreview(path);
        }

        public void Release(SKBitmap bitmap) { }
    }

    /// <summary>
    /// 書き出し用: 原寸で読み込む。立ち絵は大きさを測るときと描くときで 2 回使うので、
    /// 直近の 2 枚だけ手元に残す（全部残すと表情が多いときにメモリが足りなくなる）。
    /// </summary>
    sealed class ExportSource : IDisplayImageSource, IDisposable
    {
        readonly List<(string Path, SKBitmap Bmp)> _recent = new();
        readonly HashSet<SKBitmap> _inUse = new();

        public SKBitmap? Get(string path, out double scale)
        {
            scale = 1;
            var hit = _recent.FindIndex(r => r.Path == path);
            if (hit >= 0)
            {
                var item = _recent[hit];
                _recent.RemoveAt(hit);
                _recent.Add(item);
                _inUse.Add(item.Bmp);
                return item.Bmp;
            }
            SKBitmap bmp;
            try { bmp = ImageOps.Decode(path); } catch { return null; }
            _recent.Add((path, bmp));
            _inUse.Add(bmp);
            Trim();
            return bmp;
        }

        public void Release(SKBitmap bitmap)
        {
            _inUse.Remove(bitmap);
            Trim();
        }

        void Trim()
        {
            while (_recent.Count > 2)
            {
                var idx = _recent.FindIndex(r => !_inUse.Contains(r.Bmp));   // 古い順に、使っていないものを捨てる
                if (idx < 0) break;
                _recent[idx].Bmp.Dispose();
                _recent.RemoveAt(idx);
            }
        }

        public void Dispose()
        {
            foreach (var r in _recent) r.Bmp.Dispose();
            _recent.Clear();
        }
    }

    SKBitmap? GetBackgroundPreview(string path)
    {
        lock (_bgPreviewCache)
        {
            if (_bgPreviewCache.TryGetValue(path, out var cached)) return cached;
            try
            {
                using var full = ImageOps.Decode(path);
                var small = ImageOps.Downscale(full, 2000);
                _bgPreviewCache[path] = small;
                return small;
            }
            catch { return null; }
        }
    }

    // ---- 書き出し ----

    async Task ExportDisplayAsync()
    {
        if (Dialogs is null || BaseImage is null) return;
        var spec = Display.Clone();
        var input = BuildDisplayInput(spec);
        if (input is null) return;
        if (input.Cells.Count == 0 && !spec.ShowSprite)
        {
            ShowToast("載せる画像がありません", "一覧のチェックで、ディスプレイ画像に載せる表情を選んでください。", isError: true);
            return;
        }
        var ext = spec.Format == "jpg" ? "jpg" : "png";
        var suggested = $"{BaseImage.FolderName}_ディスプレイ.{ext}";
        var path = await Dialogs.SaveFileAsync("ディスプレイ画像の保存先", suggested, BaseImage.Folder, ext);
        if (path is null) return;

        SaveSettings();
        IsExporting = true;
        ExportProgress = 0;
        ExportProgressText = "作成中…";
        try
        {
            await Task.Run(() =>
            {
                using var source = new ExportSource();
                using var bmp = DisplayRenderer.Render(input, source, 1.0);
                if (ext == "jpg") ImageOps.SaveJpeg(bmp, path, 92);
                else ImageOps.SavePng(bmp, path);
            });
            var (w, h) = DisplayRenderer.CanvasSize(spec, input.BaseWidth, input.BaseHeight, input.Cells.Count);
            ShowToast("ディスプレイ画像を書き出しました", $"{w}×{h}px · {Path.GetFileName(path)}",
                folders: [Path.GetDirectoryName(path)!]);
        }
        catch (Exception ex)
        {
            ShowToast("ディスプレイ画像を書き出せませんでした", ex.Message, isError: true);
        }
        finally
        {
            IsExporting = false;
        }
    }

#if DEBUG
    /// <summary>開発用: 今の設定で原寸のディスプレイ画像を作る。</summary>
    public SKBitmap? RenderFullForTest()
    {
        var input = BuildDisplayInput(Display.Clone());
        if (input is null) return null;
        using var source = new ExportSource();
        return DisplayRenderer.Render(input, source, 1.0);
    }
#endif

    void SaveDisplaySettings()
    {
        _settings.Display = Display.Clone();
        _settings.DisplayAutoColor = AutoMatchColors;
    }

    async Task LoadFontsAsync()
    {
        var fonts = await Task.Run(() =>
        {
            var list = new List<(string, bool)> { (DefaultFontLabel, true) };
            try
            {
                foreach (var f in SKFontManager.Default.FontFamilies
                             .Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith('@'))
                             .Distinct().OrderBy(f => f, StringComparer.CurrentCulture))
                {
                    bool jp = false;
                    try
                    {
                        using var tf = SKFontManager.Default.MatchFamily(f);
                        jp = tf is not null && tf.FamilyName == f && tf.ContainsGlyph('あ') && tf.ContainsGlyph('漢');
                    }
                    catch { }
                    list.Add((f, jp));
                }
            }
            catch { }
            return list;
        });
        _allFonts = fonts;
        RefreshFontChoices();
    }

    /// <summary>立ち絵の色を調べて TradeColors を作り直す（立ち絵が替わったときだけ）。</summary>
    async Task UpdatePaletteAsync()
    {
        var item = DisplaySpriteItem;
        if (item?.Proxy is not { } proxy || _paletteFor == item.Path) return;
        _paletteFor = item.Path;
        var colors = await Task.Run(() => ColorAnalyzer.ExtractPalette(proxy));
        TradeColors.Clear();
        foreach (var c in colors)
            TradeColors.Add(new Swatch($"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}",
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(c.Red, c.Green, c.Blue))));
        OnPropertyChanged(nameof(HasTradeColors));
    }

    /// <summary>今の配色をキャラの色に合わせる。hex を指定するとその色を主役にする。</summary>
    [RelayCommand]
    void MatchColors(string? hex)
    {
        if (TradeColors.Count == 0) return;
        Display.Recolor(SKColor.Parse(hex ?? TradeColors[0].Hex));
    }
}
