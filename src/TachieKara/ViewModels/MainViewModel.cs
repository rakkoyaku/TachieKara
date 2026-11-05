using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using TachieKara.Models;
using TachieKara.Services;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TachieKara.ViewModels;

/// <summary>ViewModel から呼ぶ画面側の機能（ダイアログなど）。</summary>
public interface IDialogs
{
    /// <summary>選択肢ダイアログ。押されたボタンの番号、閉じられたら -1。</summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string>? details, params string[] buttons);
    Task<string?> PickFolderAsync(string title);
    /// <summary>保存先を選ぶ。ext は "png" など。キャンセルなら null。</summary>
    Task<string?> SaveFileAsync(string title, string suggestedName, string? folder, string ext);
    /// <summary>画像ファイルを 1 つ選ぶ。キャンセルなら null。</summary>
    Task<string?> PickImageAsync(string title);
    void OpenFolder(string path);
}

public sealed partial class MainViewModel : ViewModelBase
{
    const int ProxyMaxSide = 1200;
    const int ThumbSize = 96;          // 一覧に 48dip で表示（高 DPI でもぼやけないよう 2 倍）

    readonly AppSettings _settings = SettingsStore.Load();
    readonly SemaphoreSlim _proxyGate = new(3);
    SKBitmap? _baseFull;
    int _thumbVersion;
    DispatcherTimer? _debounce;
    CancellationTokenSource? _exportCts;

    public IDialogs? Dialogs { get; set; }

    public ObservableCollection<SourceImage> Images { get; } = new();

    public MainViewModel()
    {
        OutputSize = _settings.OutputSize;
        KeepCropSize = _settings.KeepCropSize;
        OutFolderName = _settings.OutFolderName;
        Prefix = _settings.Prefix;
        Suffix = _settings.Suffix;
        UseBackground = _settings.UseBackground;
        BackgroundColor = _settings.BackgroundColor;
        ShowGuides = _settings.ShowGuides;
        GuideCenter = _settings.GuideCenter;
        GuideThirds = _settings.GuideThirds;
        GuideCircle = _settings.GuideCircle;
        BustFolderName = _settings.BustFolderName;
        IsBust = _settings.Mode == "bust";
        Images.CollectionChanged += (_, _) => RaiseListDerived();
        InitDisplay();
    }

    // ---- 状態 ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaseSummary), nameof(HasBase), nameof(CanExport), nameof(DestinationHint), nameof(ExportSummary))]
    public partial SourceImage? BaseImage { get; set; }

    [ObservableProperty] public partial AvBitmap? BasePreview { get; set; }
    /// <summary>キャンバスに表示中の画像（一覧で選んだ表情。未選択ならベース）。</summary>
    [ObservableProperty] public partial AvBitmap? CanvasImage { get; set; }
    [ObservableProperty] public partial AvBitmap? OutputPreview { get; set; }
    [ObservableProperty] public partial bool IsLoadingBase { get; set; }
    [ObservableProperty] public partial bool IsLoadingDisplay { get; set; }

    /// <summary>一覧で選択中の画像。</summary>
    [ObservableProperty] public partial SourceImage? SelectedImage { get; set; }

    // 今のモードの切り抜き枠（ベース画像のピクセル座標）。顔アイコンでは CropW == CropH を保つ
    [ObservableProperty] public partial int CropX { get; set; }
    [ObservableProperty] public partial int CropY { get; set; }
    [ObservableProperty] public partial int CropW { get; set; } = 512;
    [ObservableProperty] public partial int CropH { get; set; } = 512;

    /// <summary>false = 顔アイコン（正方形）、true = バストアップ（幅と高さを指定）。</summary>
    [ObservableProperty] public partial bool IsBust { get; set; }
    [ObservableProperty] public partial string BustFolderName { get; set; } = "";

    // 切り替えたときに戻せるよう、もう一方のモードの枠を持っておく
    CropRect _faceRect = new(0, 0, 512);
    BustRect _bustRect = new(0, 0, 1024, 1280);

    [ObservableProperty] public partial int OutputSize { get; set; }
    [ObservableProperty] public partial bool KeepCropSize { get; set; }
    [ObservableProperty] public partial string OutFolderName { get; set; } = "";
    [ObservableProperty] public partial string Prefix { get; set; } = "";
    [ObservableProperty] public partial string Suffix { get; set; } = "";
    [ObservableProperty] public partial bool UseBackground { get; set; }
    [ObservableProperty] public partial string BackgroundColor { get; set; } = "#FFFFFF";

    // ガイド線（表示のみ。書き出す画像には入らない）
    [ObservableProperty] public partial bool ShowGuides { get; set; } = true;
    [ObservableProperty] public partial bool GuideCenter { get; set; } = true;
    [ObservableProperty] public partial bool GuideThirds { get; set; } = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCircleGuide))]
    public partial bool GuideCircle { get; set; }
    /// <summary>円のガイドは正方形の顔アイコンだけで使う。</summary>
    public bool ShowCircleGuide => GuideCircle && !IsBust;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanExport))]
    public partial bool IsExporting { get; set; }
    [ObservableProperty] public partial double ExportProgress { get; set; }
    [ObservableProperty] public partial string ExportProgressText { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    // キャンバス右上に出す通知（完了・エラーなど）
    [ObservableProperty] public partial bool ToastVisible { get; set; }
    [ObservableProperty] public partial string ToastTitle { get; set; } = "";
    [ObservableProperty] public partial string ToastMessage { get; set; } = "";
    [ObservableProperty] public partial bool ToastIsError { get; set; }
    [ObservableProperty] public partial bool ToastHasFolders { get; set; }
    List<string> _toastFolders = new();
    DispatcherTimer? _toastTimer;

    public bool HasBase => BaseImage is not null;
    public bool HasImages => Images.Count > 0;
    public bool CanEdit => !IsExporting;
    public bool CanExport => HasBase && !IsExporting;
    public string BaseSummary => BaseImage is null ? "" : $"{BaseImage.FileName}　{BaseImage.SizeText}";
    public int EffectiveOutputSize => KeepCropSize ? CropW : Math.Clamp(OutputSize, 1, 8192);
    /// <summary>書き出す画像の大きさ。バストアップは切り抜いたサイズのまま。</summary>
    public int OutW => IsBust ? CropW : EffectiveOutputSize;
    public int OutH => IsBust ? CropH : EffectiveOutputSize;
    public bool IsFace => !IsBust;
    public string Mode => IsDisplay ? "display" : IsBust ? "bust" : "face";
    public string KindName => IsBust ? "バストアップ" : "顔アイコン";
    public string ExportButtonText => IsDisplay ? "ディスプレイ画像を書き出す" : $"{KindName}を書き出す";
    public string CurrentFolderName =>
        (IsBust ? BustFolderName : OutFolderName) is { } n && !string.IsNullOrWhiteSpace(n) ? n.Trim() : KindName;
    public string AspectText
    {
        get
        {
            if (CropW <= 0 || CropH <= 0) return "";
            int a = CropW, b = CropH;
            while (b != 0) (a, b) = (b, a % b);
            var (rw, rh) = (CropW / a, CropH / a);
            return rw <= 32 && rh <= 32 ? $"比率 {rw}:{rh}" : $"比率 {(double)CropW / CropH:0.00}:1";
        }
    }
    // 仕上がりプレビューの枠（288 の中に縦横比を保って収める）
    const double PreviewBox = 286;
    public double PreviewW => OutW >= OutH ? PreviewBox : Math.Max(8, PreviewBox * OutW / Math.Max(1, OutH));
    public double PreviewH => OutH >= OutW ? PreviewBox : Math.Max(8, PreviewBox * OutH / Math.Max(1, OutW));
    public string OutputPreset => KeepCropSize ? "orig" : OutputSize.ToString();
    public string BackgroundPreset => UseBackground ? "color" : "none";
    public Avalonia.Media.IBrush SwatchBrush =>
        ImageOps.ParseColor(BackgroundColor) is { } c
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue))
            : Avalonia.Media.Brushes.Transparent;

    public int WarnCount => Images.Count(i => i.IsWarning);
    public int ErrorCount => Images.Count(i => i.IsError);
    public bool HasWarnings => WarnCount > 0;
    public bool HasErrors => ErrorCount > 0;
    public string AssetCountText => Images.Count.ToString();

    SourceImage? _displayItem;
    SKBitmap? _displayFull;
    int _displayVersion;
    public bool IsViewingOther => _displayItem is not null && _displayItem != BaseImage;
    public string ViewingLabel => _displayItem?.DisplayName ?? "";
    public bool ViewingMismatch => _displayItem?.IsMismatch == true;
    public string PreviewCaption => (_displayItem ?? BaseImage) is { } d ? d.DisplayName : "";

    public string ExportSummary
    {
        get
        {
            if (IsDisplay) return DisplaySummary;
            var n = Images.Count(i => !i.IsError);
            var bg = UseBackground ? "背景色つき" : "透過";
            return $"{n}枚  ·  {OutW}×{OutH}px  ·  {bg} PNG";
        }
    }

    public string DestinationHint
    {
        get
        {
            var folders = Images.Where(i => !i.IsError).Select(i => i.Folder).Distinct(PathComparer).ToList();
            var name = CurrentFolderName;
            return folders.Count switch
            {
                0 => $"立ち絵のフォルダ / {name}",
                1 => $"{Path.GetFileName(folders[0])} / {name}",
                _ => $"{folders.Count}個のフォルダ（書き出し時に選びます）",
            };
        }
    }

    void RaiseListDerived()
    {
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(WarnCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(AssetCountText));
        OnPropertyChanged(nameof(ExportSummary));
        OnPropertyChanged(nameof(DestinationHint));
    }

    void RaiseOutputDerived()
    {
        ScheduleCropRefresh();
        OnPropertyChanged(nameof(EffectiveOutputSize));
        OnPropertyChanged(nameof(OutputPreset));
        OnPropertyChanged(nameof(OutW));
        OnPropertyChanged(nameof(OutH));
        OnPropertyChanged(nameof(AspectText));
        OnPropertyChanged(nameof(PreviewW));
        OnPropertyChanged(nameof(PreviewH));
        OnPropertyChanged(nameof(ExportSummary));
    }

    partial void OnCropXChanged(int value) => ScheduleCropRefresh();
    partial void OnCropYChanged(int value) => ScheduleCropRefresh();
    partial void OnCropWChanged(int value)
    {
        if (!IsBust && CropH != value) CropH = value;   // 顔アイコンは正方形
        RaiseOutputDerived();
    }
    partial void OnCropHChanged(int value)
    {
        if (!IsBust && CropW != value) CropW = value;
        RaiseOutputDerived();
    }

    partial void OnIsBustChanged(bool value)
    {
        // 抜けるモードの枠をしまって、入るモードの枠を出す
        if (value)
        {
            _faceRect = new CropRect(CropX, CropY, CropW);
            ApplyRect(_bustRect.X, _bustRect.Y, _bustRect.Width, _bustRect.Height);
        }
        else
        {
            _bustRect = new BustRect(CropX, CropY, CropW, CropH);
            ApplyRect(_faceRect.X, _faceRect.Y, _faceRect.Size, _faceRect.Size);
        }
        foreach (var n in new[] { nameof(IsFace), nameof(Mode), nameof(KindName), nameof(ExportButtonText),
                     nameof(CurrentFolderName), nameof(DestinationHint), nameof(ShowCircleGuide) })
            OnPropertyChanged(n);
        RaiseOutputDerived();
    }

    partial void OnBustFolderNameChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentFolderName));
        OnPropertyChanged(nameof(DestinationHint));
    }

    /// <summary>枠を置き換える。IsBust を切り替えてから呼ぶこと（顔アイコンでは W と H が連動するため）。</summary>
    void ApplyRect(int x, int y, int w, int h)
    {
        CropW = w;
        CropH = h;
        CropX = x;
        CropY = y;
    }

    [RelayCommand]
    void SetMode(string mode)
    {
        if (mode == "display") { IsDisplay = true; return; }
        IsDisplay = false;
        IsBust = mode == "bust";
    }
    partial void OnOutputSizeChanged(int value) => RaiseOutputDerived();
    partial void OnKeepCropSizeChanged(bool value) => RaiseOutputDerived();
    partial void OnUseBackgroundChanged(bool value)
    {
        ScheduleCropRefresh();
        OnPropertyChanged(nameof(BackgroundPreset));
        OnPropertyChanged(nameof(ExportSummary));
    }
    partial void OnBackgroundColorChanged(string value)
    {
        ScheduleCropRefresh();
        OnPropertyChanged(nameof(SwatchBrush));
    }
    partial void OnOutFolderNameChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentFolderName));
        OnPropertyChanged(nameof(DestinationHint));
    }
    partial void OnPrefixChanged(string value) => OnPropertyChanged(nameof(FileNameExample));
    partial void OnSuffixChanged(string value) => OnPropertyChanged(nameof(FileNameExample));
    partial void OnBaseImageChanged(SourceImage? value) => OnPropertyChanged(nameof(FileNameExample));

    /// <summary>書き出されるファイル名の例。</summary>
    public string FileNameExample =>
        $"{Prefix}{BaseImage?.OutputName ?? "通常"}{Suffix}.png";
    partial void OnSelectedImageChanged(SourceImage? value) => _ = ShowImageAsync(value);

    [RelayCommand]
    void SetOutputSize(string size)
    {
        if (size == "orig") { KeepCropSize = true; return; }
        KeepCropSize = false;
        OutputSize = int.Parse(size);
    }

    [RelayCommand]
    void SetBackground(string mode) => UseBackground = mode == "color";

    [RelayCommand]
    void ShowBase() => SelectedImage = BaseImage;

    // ---- 自動更新 ----

    readonly Updater _updater = new();
    public string AppVersion => _updater.CurrentVersion;
    /// <summary>インストール版のときだけ更新を確認できる（開発中の実行や単体 exe では不可）。</summary>
#if DEBUG
    public bool CanCheckUpdates => _updater.IsInstalled || Environment.GetEnvironmentVariable("TACHIEKARA_FAKESTATE") is { Length: > 0 };
#else
    public bool CanCheckUpdates => _updater.IsInstalled;
#endif

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(IsUpdateDownloading), nameof(IsUpdateReady),
        nameof(IsUpdateError), nameof(HasUpdateBanner))]
    public partial string UpdateState { get; set; } = "none";   // none / available / downloading / ready / error
    [ObservableProperty] public partial string UpdateVersion { get; set; } = "";
    [ObservableProperty] public partial int UpdateProgress { get; set; }
    [ObservableProperty] public partial string UpdateMessage { get; set; } = "";
    /// <summary>手動で確認したときの結果（ヘルプ欄に出す）。</summary>
    [ObservableProperty] public partial string UpdateCheckResult { get; set; } = "";

    public bool IsUpdateAvailable => UpdateState == "available";
    public bool IsUpdateDownloading => UpdateState == "downloading";
    public bool IsUpdateReady => UpdateState == "ready";
    public bool IsUpdateError => UpdateState == "error";
    public bool HasUpdateBanner => UpdateState != "none";

    /// <summary>起動時に裏で更新を確認する。失敗しても何も出さない。</summary>
    /// <summary>起動時に新しいバージョンを確認するか（ソフトウェア情報で切り替える）。</summary>
    public bool CheckUpdatesOnStartup
    {
        get => _settings.CheckUpdatesOnStartup;
        set
        {
            if (_settings.CheckUpdatesOnStartup == value) return;
            _settings.CheckUpdatesOnStartup = value;
            SettingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public async Task CheckUpdatesOnStartupAsync()
    {
        if (!_updater.IsInstalled || !_settings.CheckUpdatesOnStartup) return;
        try
        {
            if (_updater.PendingRestartVersion is { } pending)
            {
                (UpdateVersion, UpdateState) = (pending, "ready");
                return;
            }
            if (await _updater.CheckAsync() is { } v)
                (UpdateVersion, UpdateState) = (v, "available");
        }
        catch { /* オフラインなど。起動時は黙っておく */ }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckNow))]
    public partial bool IsCheckingUpdates { get; set; }
    /// <summary>［アップデートを確認］を押せるか（確認中・ダウンロード中・準備完了のときは押せない）。</summary>
    public bool CanCheckNow => CanCheckUpdates && !IsCheckingUpdates && UpdateState is "none" or "available" or "error";
    /// <summary>確認の結果が「最新」だったか（緑のチェックを出す）。</summary>
    [ObservableProperty] public partial bool IsUpToDate { get; set; }
    public string RepoUrl => Updater.RepoUrl;

    partial void OnUpdateStateChanged(string value) => OnPropertyChanged(nameof(CanCheckNow));

    [RelayCommand]
    async Task CheckUpdates()
    {
        if (!_updater.IsInstalled || IsCheckingUpdates) return;
        if (UpdateState is "downloading" or "ready") return;
        IsCheckingUpdates = true;
        IsUpToDate = false;
        UpdateCheckResult = "";
        try
        {
            await Task.WhenAll(Task.Delay(400), Run());   // 一瞬で終わっても「確認した」ことが分かるよう少し待つ
            async Task Run()
            {
                if (await _updater.CheckAsync() is { } v)
                    (UpdateVersion, UpdateState) = (v, "available");
                else
                {
                    if (UpdateState == "error") UpdateState = "none";
                    IsUpToDate = true;
                    UpdateCheckResult = $"最新のバージョンです（{DateTime.Now:HH:mm} に確認）";
                }
            }
        }
        catch (Exception ex)
        {
            UpdateCheckResult = $"確認できませんでした: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    async Task DownloadUpdate()
    {
        if (UpdateState != "available" && UpdateState != "error") return;
        UpdateState = "downloading";
        UpdateProgress = 0;
        try
        {
            await _updater.DownloadAsync(p => Dispatcher.UIThread.Post(() => UpdateProgress = p));
            UpdateState = "ready";
        }
        catch (Exception ex)
        {
            UpdateMessage = ex.Message;
            UpdateState = "error";
        }
    }

    /// <summary>設定を保存してから終了し、更新を適用して起動し直す。</summary>
    [RelayCommand]
    void RestartToUpdate()
    {
        if (IsExporting) return;
        SaveSettings();
        _updater.ApplyAndRestart();
    }

    [RelayCommand]
    void DismissUpdate()
    {
        // ダウンロード済みなら、閉じても次回の起動時に反映される
        if (UpdateState != "downloading") UpdateState = "none";
    }

    // ---- 通知 ----

    void ShowToast(string title, string message, bool isError = false, List<string>? folders = null)
    {
        ToastTitle = title;
        ToastMessage = message;
        ToastIsError = isError;
        _toastFolders = folders ?? new();
        ToastHasFolders = _toastFolders.Count > 0;
        ToastVisible = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer(TimeSpan.FromSeconds(12), DispatcherPriority.Background, (_, _) =>
        {
            _toastTimer?.Stop();
            ToastVisible = false;
        });
        _toastTimer.Start();
    }

    [RelayCommand]
    void DismissToast() { _toastTimer?.Stop(); ToastVisible = false; }

    [RelayCommand]
    void OpenToastFolders()
    {
        foreach (var d in _toastFolders) Dialogs?.OpenFolder(d);
        DismissToast();
    }

    // ---- 表示中の画像 ----

    async Task ShowImageAsync(SourceImage? item)
    {
        var v = ++_displayVersion;
        if (item is null || item.IsError || item == BaseImage || BaseImage is null)
        {
            SetDisplay(BaseImage, _baseFull, BasePreview);
            IsLoadingDisplay = false;
            return;
        }
        IsLoadingDisplay = true;
        try
        {
            var (full, av) = await Task.Run(() =>
            {
                var f = ImageOps.Decode(item.Path);
                return (f, ImageOps.ToAvalonia(f));
            });
            if (v != _displayVersion) { full.Dispose(); return; }
            SetDisplay(item, full, av);
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
            RaiseListDerived();
        }
        finally
        {
            if (v == _displayVersion) IsLoadingDisplay = false;
        }
    }

    void SetDisplay(SourceImage? item, SKBitmap? full, AvBitmap? av)
    {
        var old = _displayFull;
        _displayItem = item;
        _displayFull = full;
        CanvasImage = av;
        // 裏でプレビュー生成に使っている可能性があるので、少し待ってから解放する
        if (old is not null && old != _baseFull && old != full)
            _ = Task.Delay(3000).ContinueWith(_ => old.Dispose());
        OnPropertyChanged(nameof(IsViewingOther));
        OnPropertyChanged(nameof(ViewingLabel));
        OnPropertyChanged(nameof(ViewingMismatch));
        OnPropertyChanged(nameof(PreviewCaption));
        ScheduleCropRefresh();
    }

    // ---- 取り込み ----

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Images.Select(i => i.Path), PathComparer);
        var files = new List<string>();
        int skippedFolders = 0;
        await Task.Run(() =>
        {
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        // 子フォルダは辿らない（差分のサブフォルダなどは、フォルダごと別に追加してもらう）
                        foreach (var f in Directory.EnumerateFiles(p))
                            if (ImageOps.IsSupported(f)) files.Add(Path.GetFullPath(f));
                        skippedFolders += Directory.EnumerateDirectories(p).Count();
                    }
                    else if (File.Exists(p) && ImageOps.IsSupported(p))
                    {
                        files.Add(Path.GetFullPath(p));
                    }
                }
                catch { /* アクセスできないものは無視 */ }
            }
        });

        var newFiles = files.Distinct(PathComparer).Where(f => !existing.Contains(f))
            .OrderBy(f => Path.GetDirectoryName(f), NaturalComparer.Instance)
            .ThenBy(Path.GetFileName, NaturalComparer.Instance)
            .ToList();
        if (newFiles.Count == 0)
        {
            StatusText = files.Count == 0 ? "対応する画像が見つかりませんでした。" : "すでに一覧にあるファイルです。";
            return;
        }

        StatusText = $"{newFiles.Count}件を読み込み中…";
        var items = await Task.Run(() => newFiles.Select(f =>
        {
            try
            {
                var (w, h) = ImageOps.ReadSize(f);
                return new SourceImage(f, w, h, new FileInfo(f).Length);
            }
            catch (Exception ex)
            {
                long len = 0;
                try { len = new FileInfo(f).Length; } catch { }
                return new SourceImage(f, 0, 0, len) { Error = ex.Message };
            }
        }).ToList());

        foreach (var item in items)
        {
            if (_settings.Names.TryGetValue(AppSettings.NameKey(item.Path), out var name)) item.CustomName = name;
            Images.Add(item);
        }

        if (BaseImage is null && items.FirstOrDefault(i => !i.IsError) is { } first)
            await SetBaseAsync(first);
        else
            UpdateMismatch();

        StatusText = $"{items.Count}件を追加しました。" +
                     (skippedFolders > 0 ? $"（子フォルダ {skippedFolders}個は対象外。必要ならフォルダごと追加してください）" : "");

        foreach (var item in items.Where(i => !i.IsError))
            _ = LoadProxyAsync(item);
    }

    static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    async Task LoadProxyAsync(SourceImage item)
    {
        await _proxyGate.WaitAsync();
        try
        {
            if (!Images.Contains(item)) return;
            var (proxy, thumb) = await Task.Run(() =>
            {
                using var full = ImageOps.Decode(item.Path);
                var proxy = ImageOps.Downscale(full, ProxyMaxSide);
                using var t = ImageOps.Downscale(proxy, ThumbSize);
                return (proxy, ImageOps.ToAvalonia(t));
            });
            item.Proxy = proxy;
            item.Thumbnail = thumb;
            ScheduleDisplayRender();
            await RenderCropThumbAsync(item, _thumbVersion);
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
            RaiseListDerived();
        }
        finally
        {
            _proxyGate.Release();
        }
    }

    public void Remove(IReadOnlyList<SourceImage> items)
    {
        if (IsExporting || items.Count == 0) return;
        bool baseRemoved = false;
        foreach (var i in items.ToList())
        {
            if (i == BaseImage) baseRemoved = true;
            Images.Remove(i);
            i.Proxy?.Dispose();
            i.Proxy = null;
        }
        if (baseRemoved)
            _ = SetBaseAsync(Images.FirstOrDefault(i => !i.IsError));
        else
            UpdateMismatch();
        StatusText = $"{items.Count}件を一覧から外しました。";
    }

    [RelayCommand]
    void ClearAll() => Remove(Images.ToList());

    public async Task SetBaseAsync(SourceImage? item)
    {
        if (item is { IsError: true }) return;
        if (BaseImage is not null) BaseImage.IsBase = false;
        BaseImage = item;
        if (item is not null) item.IsBase = true;
        UpdateMismatch();

        if (item is null)
        {
            BasePreview = null;
            OutputPreview = null;
            SetDisplay(null, null, null);
            var gone = _baseFull;
            _baseFull = null;
            if (gone is not null) _ = Task.Delay(3000).ContinueWith(_ => gone.Dispose());
            return;
        }

        // 保存済みの枠を復元。なければ上寄り中央に置く
        var key = AppSettings.CropKey(item.Folder, item.Width, item.Height);
        bool sameSize = _baseFull is not null && _baseFull.Width == item.Width && _baseFull.Height == item.Height;
        if (sameSize) StashActiveRect();   // 同じ大きさのベースに替えたときは今の枠をそのまま使う
        if (_settings.Crops.TryGetValue(key, out var saved))
            _faceRect = saved;
        else if (!sameSize)
        {
            var size = Math.Max(16, Math.Min(item.Width, item.Height) / 4);
            _faceRect = new CropRect((item.Width - size) / 2, item.Height / 10, size);
        }
        if (_settings.BustCrops.TryGetValue(key, out var savedBust))
            _bustRect = savedBust;
        else if (!sameSize)
            _bustRect = DefaultBustRect(item.Width, item.Height, _faceRect);
        if (IsBust) ApplyRect(_bustRect.X, _bustRect.Y, _bustRect.Width, _bustRect.Height);
        else ApplyRect(_faceRect.X, _faceRect.Y, _faceRect.Size, _faceRect.Size);

        IsLoadingBase = true;
        StatusText = $"ベース画像を読み込み中… {item.FileName}";
        try
        {
            var (full, preview) = await Task.Run(() =>
            {
                var full = ImageOps.Decode(item.Path);
                return (full, ImageOps.ToAvalonia(full));
            });
            if (BaseImage != item) { full.Dispose(); return; }
            var oldBase = _baseFull;
            _baseFull = full;
            BasePreview = preview;
            // 他の表情を表示中でなければ、キャンバスもベースに切り替える
            if (SelectedImage is null || SelectedImage == item || _displayItem is null || _displayFull == oldBase)
                SetDisplay(item, full, preview);
            if (oldBase is not null && oldBase != _displayFull)
                _ = Task.Delay(3000).ContinueWith(_ => oldBase.Dispose());
            StatusText = "";
            ScheduleCropRefresh();
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
            StatusText = $"ベース画像を読み込めませんでした: {ex.Message}";
        }
        finally
        {
            IsLoadingBase = false;
        }
    }

    /// <summary>今表示している枠を、そのモードの保存先に書き戻す。</summary>
    void StashActiveRect()
    {
        if (IsBust) _bustRect = new BustRect(CropX, CropY, CropW, CropH);
        else _faceRect = new CropRect(CropX, CropY, CropW);
    }

    /// <summary>バストアップ枠の初期値。前回使ったサイズで、顔アイコン枠の少し上から取る。</summary>
    BustRect DefaultBustRect(int imgW, int imgH, CropRect face)
    {
        int w = _settings.BustWidth > 0 ? _settings.BustWidth : Math.Max(16, (int)Math.Round(face.Size * 2.2));
        int h = _settings.BustHeight > 0 ? _settings.BustHeight : (int)Math.Round(w * 1.25);
        w = Math.Min(w, imgW);
        h = Math.Min(h, imgH);
        var cx = face.X + face.Size / 2.0;
        int x = (int)Math.Round(Math.Clamp(cx - w / 2.0, 0, Math.Max(0, imgW - w)));
        int y = (int)Math.Round(Math.Clamp(face.Y - face.Size * 0.35, 0, Math.Max(0, imgH - h)));
        return new BustRect(x, y, w, h);
    }

    void UpdateMismatch()
    {
        foreach (var i in Images)
        {
            i.IsMismatch = BaseImage is not null && !i.IsError && (i.Width != BaseImage.Width || i.Height != BaseImage.Height);
            i.BaseSizeText = BaseImage?.SizeText ?? "";
        }
        RaiseListDerived();
    }

    // ---- 名前（表情名） ----

    public void BeginRename(SourceImage item)
    {
        if (IsExporting || item.IsError) return;
        foreach (var other in Images.Where(i => i.IsRenaming && i != item).ToList()) CommitRename(other);
        item.EditName = item.OutputName;
        item.IsRenaming = true;
    }

    /// <summary>編集中の名前を確定する。空欄や元の名前と同じなら、付けた名前を外す。</summary>
    public void CommitRename(SourceImage item)
    {
        if (!item.IsRenaming) return;
        item.IsRenaming = false;
        var name = SanitizeFileName((item.EditName ?? "").Trim()).TrimEnd('.', ' ');
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        item.CustomName = name.Length == 0 || name == item.Stem ? null : name;

        var key = AppSettings.NameKey(item.Path);
        if (item.CustomName is null) _settings.Names.Remove(key);
        else _settings.Names[key] = item.CustomName;
        SettingsStore.Save(_settings);
        OnPropertyChanged(nameof(FileNameExample));
        OnPropertyChanged(nameof(ViewingLabel));
        OnPropertyChanged(nameof(PreviewCaption));
    }

    public void CancelRename(SourceImage item) => item.IsRenaming = false;

    public void Sort(string column)
    {
        var nat = NaturalComparer.Instance;
        var ordered = column switch
        {
            "status" => Images.OrderBy(i => i.IsBase ? 0 : i.IsError ? 3 : i.IsMismatch ? 2 : 1),
            "folder" => Images.OrderBy(i => i.Folder, nat),
            "size" => Images.OrderBy(i => (long)i.Width * i.Height).ThenBy(i => i.Width),
            "filesize" => Images.OrderBy(i => i.FileSize),
            _ => Images.OrderBy(i => i.FileName, nat),
        };
        var sorted = ordered
            .ThenBy(i => i.Folder, nat)
            .ThenBy(i => i.FileName, NaturalComparer.Instance)
            .ToList();
        if (sorted.SequenceEqual(Images)) sorted.Reverse();   // 2 回押したら逆順
        for (int n = 0; n < sorted.Count; n++)
        {
            var cur = Images.IndexOf(sorted[n]);
            if (cur != n) Images.Move(cur, n);
        }
    }

    // ---- 切り抜き結果のプレビュー ----

    void ScheduleCropRefresh()
    {
        ScheduleDisplayRender();
        _debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            _ = RefreshCropPreviewsAsync();
        });
        _debounce.Stop();
        _debounce.Start();
    }

    async Task RefreshCropPreviewsAsync()
    {
        var version = ++_thumbVersion;
        var baseFull = _displayFull ?? _baseFull;   // 選択中の表情で仕上がりを見せる
        if (baseFull is not null)
        {
            var rect = new SKRect(CropX, CropY, CropX + CropW, CropY + CropH);
            // 書き出しと同じ処理で作る（大きすぎる場合は表示用に 600px に抑える）
            var (pw, ph) = FitWithin(OutW, OutH, 600);
            var bg = UseBackground ? ImageOps.ParseColor(BackgroundColor) : null;
            var bmp = await Task.Run(() =>
            {
                using var r = ImageOps.Resample(baseFull, rect, pw, ph, bg);
                return ImageOps.ToAvalonia(r);
            });
            if (version == _thumbVersion) OutputPreview = bmp;
        }
        foreach (var item in Images.ToList())
        {
            if (version != _thumbVersion) return;
            await RenderCropThumbAsync(item, version);
        }
    }

    async Task RenderCropThumbAsync(SourceImage item, int version)
    {
        var proxy = item.Proxy;
        if (proxy is null || CropW <= 0 || CropH <= 0) return;
        var s = (float)item.ProxyScale;
        var rect = new SKRect(CropX * s, CropY * s, (CropX + CropW) * s, (CropY + CropH) * s);
        var (tw, th) = FitWithin(CropW, CropH, ThumbSize);
        var bmp = await Task.Run(() =>
        {
            using var r = ImageOps.Resample(proxy, rect, tw, th, null);
            return ImageOps.ToAvalonia(r);
        });
        if (version == _thumbVersion || item.CropThumbnail is null) item.CropThumbnail = bmp;
    }

    /// <summary>縦横比を保って max 以下に収めた大きさ（拡大はしない）。</summary>
    static (int W, int H) FitWithin(int w, int h, int max)
    {
        if (w <= 0 || h <= 0) return (1, 1);
        var k = Math.Min(1.0, (double)max / Math.Max(w, h));
        return (Math.Max(1, (int)Math.Round(w * k)), Math.Max(1, (int)Math.Round(h * k)));
    }

    // ---- 書き出し ----

    [RelayCommand]
    void CancelExport() => _exportCts?.Cancel();

    [RelayCommand]
    async Task ExportAsync()
    {
        if (Dialogs is null || BaseImage is null || IsExporting) return;
        if (IsDisplay) { await ExportDisplayAsync(); return; }

        var targets = Images.Where(i => !i.IsError).ToList();
        var mismatched = targets.Where(i => i.IsMismatch).ToList();
        var errors = Images.Where(i => i.IsError).ToList();

        if (mismatched.Count > 0)
        {
            var choice = await Dialogs.ChooseAsync("サイズ違いの画像があります",
                $"ベース（{BaseImage.SizeText}）と縦横サイズが違う画像が {mismatched.Count}件あります。\n" +
                "含めた場合も同じピクセル位置で切り抜くため、顔の位置がずれる可能性があります。\n" +
                "一覧の「切り抜き」列で結果を確認できます。",
                mismatched.Select(i => $"⚠ {i.FileName}（{i.SizeText}）").ToList(),
                "除外して作成", "含めて作成", "キャンセル");
            if (choice is < 0 or 2) return;
            if (choice == 0) targets = targets.Except(mismatched).ToList();
        }
        if (targets.Count == 0) return;

        var folderName = SanitizeFileName(CurrentFolderName);
        var kind = KindName;
        var mode = Mode;

        // 出力先の決定
        var folders = targets.Select(t => t.Folder).Distinct(PathComparer).ToList();
        Dictionary<SourceImage, string> destDirs;
        if (folders.Count == 1)
        {
            destDirs = targets.ToDictionary(t => t, _ => Path.Combine(folders[0], folderName));
        }
        else
        {
            var common = CommonParent(folders);
            var buttons = new List<string>();
            if (common is not null) buttons.Add("共通の親フォルダに1つ作る");
            buttons.Add("各フォルダにそれぞれ作る");
            buttons.Add("場所を選ぶ…");
            buttons.Add("キャンセル");
            var details = folders.Select(f => "・" + f).ToList();
            if (common is not null) details.Add($"\n共通の親フォルダ: {common}");
            var choice = await Dialogs.ChooseAsync("出力先を選んでください",
                $"立ち絵が {folders.Count}個のフォルダにまたがっています。「{folderName}」フォルダをどこに作りますか？",
                details, buttons.ToArray());
            if (choice < 0) return;
            var label = buttons[choice];
            if (label == "キャンセル") return;
            if (label == "各フォルダにそれぞれ作る")
            {
                destDirs = targets.ToDictionary(t => t, t => Path.Combine(t.Folder, folderName));
            }
            else
            {
                string root;
                if (label == "場所を選ぶ…")
                {
                    var picked = await Dialogs.PickFolderAsync($"「{folderName}」フォルダを作る場所");
                    if (picked is null) return;
                    root = picked;
                }
                else root = common!;
                destDirs = targets.ToDictionary(t => t, _ => Path.Combine(root, folderName));
            }
        }

        // ファイル名の決定（同じ出力先で名前が重なる場合はフォルダ名を付ける）
        var plan = new List<(SourceImage Src, string Dest)>();
        foreach (var group in targets.GroupBy(t => destDirs[t], PathComparer))
        {
            // 名前を付けた画像はその名前で書き出す
            var nameCounts = group.GroupBy(t => t.OutputName, PathComparer)
                .ToDictionary(g => g.Key, g => g.Count(), PathComparer);
            // 元の名前のまま出せるものを先に確保し、重なったものだけ「フォルダ名_」を付ける
            var used = new HashSet<string>(PathComparer);
            var unique = group.Where(t => nameCounts[t.OutputName] == 1).ToList();
            foreach (var t in unique) used.Add(SanitizeFileName($"{Prefix}{t.OutputName}{Suffix}"));
            foreach (var t in group)
            {
                var stem = t.OutputName;
                string candidate;
                if (nameCounts[stem] == 1)
                {
                    candidate = SanitizeFileName($"{Prefix}{stem}{Suffix}");
                }
                else
                {
                    // 別フォルダ同士の重なりならフォルダ名で区別する。同じフォルダ内で同じ名前を付けた場合は連番だけ付ける
                    var multiFolder = group.Where(o => PathComparer.Equals(o.OutputName, stem))
                        .Select(o => o.Folder).Distinct(PathComparer).Count() > 1;
                    var name = SanitizeFileName(multiFolder ? $"{Prefix}{t.FolderName}_{stem}{Suffix}" : $"{Prefix}{stem}{Suffix}");
                    candidate = name;
                    for (int n = 2; !used.Add(candidate); n++) candidate = $"{name}_{n}";
                }
                plan.Add((t, Path.Combine(group.Key, candidate + ".png")));
            }
        }

        // 上書き確認
        var existing = plan.Where(p => File.Exists(p.Dest)).ToList();
        if (existing.Count > 0)
        {
            var choice = await Dialogs.ChooseAsync("同じ名前のファイルがあります",
                $"出力先に同じ名前のファイルが {existing.Count}件あります。",
                existing.Select(e => "・" + e.Dest).ToList(),
                "上書きする", "そのファイルは飛ばす", "キャンセル");
            if (choice is < 0 or 2) return;
            if (choice == 1) plan = plan.Except(existing).ToList();
        }
        if (plan.Count == 0) return;

        // 設定の保存
        SaveSettings();

        // 実行
        IsExporting = true;
        ExportProgress = 0;
        _exportCts = new CancellationTokenSource();
        var ct = _exportCts.Token;
        var crop = new SKRectI(CropX, CropY, CropX + CropW, CropY + CropH);
        var (outW, outH) = (OutW, OutH);
        var bg = UseBackground ? ImageOps.ParseColor(BackgroundColor) : null;
        var failures = new List<string>();
        int done = 0;
        ExportProgressText = $"0 / {plan.Count}";
        try
        {
            foreach (var dir in plan.Select(p => Path.GetDirectoryName(p.Dest)!).Distinct(PathComparer))
                Directory.CreateDirectory(dir);

            await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
                async (p, token) =>
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            using var src = ImageOps.Decode(p.Src.Path);
                            token.ThrowIfCancellationRequested();
                            using var icon = ImageOps.Resample(src, crop, outW, outH, bg);
                            ImageOps.SavePng(icon, p.Dest);
                        }, token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lock (failures) failures.Add($"{p.Src.FileName}: {ex.Message}");
                    }
                    var n = Interlocked.Increment(ref done);
                    Dispatcher.UIThread.Post(() =>
                    {
                        ExportProgress = 100.0 * n / plan.Count;
                        ExportProgressText = $"{n} / {plan.Count}";
                    });
                });

            foreach (var g in plan.GroupBy(p => Path.GetDirectoryName(p.Dest)!, PathComparer))
            {
                try
                {
                    SettingsStore.WriteCropJson(g.Key, new
                    {
                        app = "TachieKara",
                        createdAt = DateTimeOffset.Now,
                        @base = new { file = BaseImage.Path, width = BaseImage.Width, height = BaseImage.Height },
                        mode,
                        crop = new { x = crop.Left, y = crop.Top, width = crop.Width, height = crop.Height },
                        output = new { width = outW, height = outH },
                        background = bg?.ToString(),
                        files = g.Select(p => new { source = p.Src.Path, output = Path.GetFileName(p.Dest) }),
                    });
                }
                catch { /* 記録が書けなくてもアイコン自体は出力済み */ }
            }
        }
        catch (OperationCanceledException)
        {
            ShowToast("キャンセルしました", $"{done}件は書き出し済みです。");
            return;
        }
        finally
        {
            IsExporting = false;
            _exportCts.Dispose();
            _exportCts = null;
        }

        var dirs = plan.Select(p => Path.GetDirectoryName(p.Dest)!).Distinct(PathComparer).ToList();
        var ok = plan.Count - failures.Count;
        var msg = $"{outW}×{outH}px · " + string.Join("、", dirs.Select(d => Path.GetFileName(Path.GetDirectoryName(d)) + "/" + Path.GetFileName(d)));
        if (errors.Count > 0) msg += $"\n読み込めなかった {errors.Count}件は対象外でした。";
        if (failures.Count > 0)
        {
            ShowToast($"{ok}件を書き出し、{failures.Count}件は失敗しました",
                msg + "\n" + string.Join("\n", failures.Take(5)), isError: true, folders: dirs);
        }
        else
        {
            ShowToast($"{ok}件の{kind}を書き出しました", msg, folders: dirs);
        }
    }

    public void SaveSettings()
    {
        _settings.OutputSize = OutputSize;
        _settings.KeepCropSize = KeepCropSize;
        _settings.OutFolderName = OutFolderName;
        _settings.Prefix = Prefix;
        _settings.Suffix = Suffix;
        _settings.UseBackground = UseBackground;
        _settings.BackgroundColor = BackgroundColor;
        _settings.ShowGuides = ShowGuides;
        _settings.GuideCenter = GuideCenter;
        _settings.GuideThirds = GuideThirds;
        _settings.GuideCircle = GuideCircle;
        _settings.Mode = IsBust ? "bust" : "face";
        SaveDisplaySettings();
        _settings.BustFolderName = BustFolderName;
        if (BaseImage is not null)
        {
            StashActiveRect();
            var key = AppSettings.CropKey(BaseImage.Folder, BaseImage.Width, BaseImage.Height);
            _settings.Crops[key] = _faceRect;
            _settings.BustCrops[key] = _bustRect;
            if (IsBust) (_settings.BustWidth, _settings.BustHeight) = (CropW, CropH);
        }
        SettingsStore.Save(_settings);
    }

    static string? CommonParent(IReadOnlyList<string> folders)
    {
        var split = folders.Select(f => Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar)).ToList();
        int len = 0;
        while (split.All(s => s.Length > len) &&
               split.All(s => PathComparer.Equals(s[len], split[0][len]))) len++;
        if (len == 0) return null;
        var parts = split[0].Take(len).ToArray();
        // UNC (\\server\share) や "C:" だけのような浅すぎる結果は避ける
        var joined = string.Join(Path.DirectorySeparatorChar, parts);
        if (OperatingSystem.IsWindows())
        {
            if (joined.StartsWith(@"\\") && len < 4) return null;
            if (len == 1) joined += Path.DirectorySeparatorChar;
        }
        else if (joined == "") joined = "/";
        return joined;
    }

    static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']))
            name = name.Replace(c, '_');
        return name;
    }
}
