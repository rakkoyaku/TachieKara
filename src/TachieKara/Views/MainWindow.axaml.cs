using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TachieKara.Controls;
using TachieKara.Models;
using TachieKara.Services;
using TachieKara.ViewModels;

namespace TachieKara.Views;

public partial class MainWindow : Window, IDialogs
{
    public static IBrush CheckerBrush { get; } = CropCanvas.CreateChecker();

    public MainWindow()
    {
        InitializeComponent();
        InitReorder();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += (_, _) => Vm?.SaveSettings();

        // タイトルバーを広げているので、OS のウィンドウボタンと重ならないよう余白を取る
        // Windows では Avalonia がタイトル（アイコン＋名前）を描くので、自前のタイトルは Mac だけで出す
        TitleContent.IsVisible = OperatingSystem.IsMacOS();
        if (OperatingSystem.IsMacOS())
        {
            TitleContent.Margin = new Avalonia.Thickness(80, 0, 0, 0);   // 信号機ボタンの右から
            ModWheelKey.Text = "⌘ + ホイール";
        }
#if DEBUG
        if (Environment.GetEnvironmentVariable("TACHIEKARA_THEME") is { Length: > 0 } theme)
            Avalonia.Application.Current!.RequestedThemeVariant =
                theme == "Light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
#endif
#if DEBUG
        // 開発用: ディスプレイ画像を各テンプレートで作って保存する（TACHIEKARA_DISPLAYTEST=出力フォルダ）
        if (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYTEST") is { Length: > 0 } dispDir)
            Opened += async (_, _) =>
            {
                Directory.CreateDirectory(dispDir);
                await Task.Delay(14000);   // 読み込みと縮小画像の作成を待つ
                var vm = Vm!;
                // 表情名を少し付けておく
                var names = (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYNAMES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < Math.Min(names.Length, vm.Images.Count); i++)
                    if (names[i] != "-") vm.Images[i].CustomName = names[i];
                if (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYEXCLUDE") is { Length: > 0 } ex)
                    foreach (var idx in ex.Split(',').Select(int.Parse)) vm.Images[idx].InDisplay = false;
                if (Environment.GetEnvironmentVariable("TACHIEKARA_TITLEFIRST") is { Length: > 0 } tf)
                    (vm.Display.ShowTitle, vm.Display.TitleText, vm.Display.Subtitle) = (true, tf, Environment.GetEnvironmentVariable("TACHIEKARA_TITLESUB") ?? "");
                if (Environment.GetEnvironmentVariable("TACHIEKARA_TITLESTYLE") is { Length: > 0 } tst) vm.Display.TitleStyle = tst;
                foreach (var kv in (Environment.GetEnvironmentVariable("TACHIEKARA_SPEC") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    // 開発用: "名前=値;名前=値" で設定を直接入れる
                    var parts = kv.Split('=', 2);
                    var prop = typeof(TachieKara.Models.DisplaySpec).GetProperty(parts[0]);
                    if (prop is null) continue;
                    object val = prop.PropertyType == typeof(double) ? double.Parse(parts[1])
                        : prop.PropertyType == typeof(bool) ? bool.Parse(parts[1])
                        : prop.PropertyType == typeof(int) ? int.Parse(parts[1]) : parts[1];
                    prop.SetValue(vm.Display, val);
                }
                if (Environment.GetEnvironmentVariable("TACHIEKARA_TITLEALIGN") is { Length: > 0 } ta) vm.Display.TitleAlign = ta;
                if (Environment.GetEnvironmentVariable("TACHIEKARA_WMOP") is { Length: > 0 } wo) vm.Display.WatermarkOpacity = double.Parse(wo);
                vm.SetModeCommand.Execute("display");
                await Task.Delay(3000);
                async Task Win(string name)
                {
                    await Task.Delay(400);
                    using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height));
                    rtb.Render(this);
#pragma warning disable CS0618
                    rtb.Save(Path.Combine(dispDir, name + ".png"));
#pragma warning restore CS0618
                }
                await Win("window");
                var only = Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYTEMPLATES");
                foreach (var t in vm.TemplateNames.ToList())
                {
                    if (only is { Length: > 0 } && !only.Split(',').Contains(t)) continue;
                    vm.ApplyTemplateCommand.Execute(t);
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_AFTERTPL_STYLE") is { Length: > 0 } ats) vm.Display.TitleStyle = ats;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await Task.Delay(300);
                    while (vm.IsRenderingDisplay && sw.ElapsedMilliseconds < 20000) await Task.Delay(100);
                    await Task.Delay(300);
                    Console.WriteLine($"[display] {t}: {sw.ElapsedMilliseconds}ms {vm.DisplaySizeText}");
#pragma warning disable CS0618
                    vm.DisplayPreview?.Save(Path.Combine(dispDir, $"tpl_{t}.png"));
#pragma warning restore CS0618
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYWIN") == "1") await Win($"window_{t}");
                }
                if (Environment.GetEnvironmentVariable("TACHIEKARA_TITLETEST") is { Length: > 0 } titleText)
                {
                    // タイトルのスタイル × 置き場所を総当たりで描く
                    var d = vm.Display;
                    (d.ShowTitle, d.TitleText, d.Subtitle) = (true, titleText, Environment.GetEnvironmentVariable("TACHIEKARA_TITLESUB") ?? "");
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_TITLEALIGN") is { Length: > 0 } ta2) d.TitleAlign = ta2;
                    foreach (var kv in (Environment.GetEnvironmentVariable("TACHIEKARA_SPEC") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = kv.Split('=', 2);
                        var prop = typeof(TachieKara.Models.DisplaySpec).GetProperty(parts[0]);
                        if (prop is null) continue;
                        prop.SetValue(d, prop.PropertyType == typeof(double) ? double.Parse(parts[1])
                            : prop.PropertyType == typeof(bool) ? bool.Parse(parts[1])
                            : prop.PropertyType == typeof(int) ? int.Parse(parts[1]) : parts[1]);
                    }
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_WMOP") is { Length: > 0 } wo2) d.WatermarkOpacity = double.Parse(wo2);
                    var styles = (Environment.GetEnvironmentVariable("TACHIEKARA_TITLESTYLES") ?? "bar,plate,plain,vertical,watermark").Split(',');
                    var anchors = (Environment.GetEnvironmentVariable("TACHIEKARA_TITLEANCHORS") ?? "grid,grid-below,top-left,top-right,bottom-left,bottom-right").Split(',');
                    foreach (var st in styles)
                        foreach (var an in anchors)
                        {
                            (d.TitleStyle, d.TitleAnchor) = (st, an);
                            if (st == "watermark") d.WatermarkPosition = an;
                            await Task.Delay(250);
                            while (vm.IsRenderingDisplay) await Task.Delay(80);
                            await Task.Delay(150);
#pragma warning disable CS0618
                            vm.DisplayPreview?.Save(Path.Combine(dispDir, $"title_{st}_{an}.png"));
#pragma warning restore CS0618
                        }
                }
                if (Environment.GetEnvironmentVariable("TACHIEKARA_OUTLINETEST") is { Length: > 0 } olt)
                {
                    // フチの なめらかさ × 縁 を原寸で描き、立ち絵の頭のあたりを切り出して保存する
                    var d = vm.Display;
                    d.SpriteOutlineWidth = double.Parse(olt);
                    foreach (var sm in new[] { 0.0, 6, 14 })
                        foreach (var edge in new[] { "aa", "crisp" })
                        {
                            (d.SpriteOutlineSmooth, d.SpriteOutlineEdge) = (sm, edge);
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            using var full = await Task.Run(() => vm.RenderFullForTest());
                            if (full is null) continue;
                            using var img = SkiaSharp.SKImage.FromBitmap(full);
                            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                            await File.WriteAllBytesAsync(Path.Combine(dispDir, $"outline_{sm}_{edge}.png"), data.ToArray());
                            Console.WriteLine($"[outline] {sm} {edge} {sw.ElapsedMilliseconds}ms");
                        }
                }
                if (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYEXTRA") == "1")
                {
                    // タイトル・記号・セル内の表情名・自動の高さ・チェック柄・右置きをまとめて確かめる
                    var d = vm.Display;
                    (d.ShowTitle, d.TitleText, d.LabelLetters, d.LabelMode, d.Aspect, d.BgKind, d.SpriteSide, d.CellAspect, d.Ghost)
                        = (true, "Sample Title", true, "inside", "auto", "checker", "right", "4:3", true);
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYBG") is { Length: > 0 } bgPath)
                        (d.BgImagePath, d.BgKind, d.BgImageDim, d.BgImageBlur) = (bgPath, "image", 35, 6);
                    await Task.Delay(500);
                    while (vm.IsRenderingDisplay) await Task.Delay(100);
                    await Task.Delay(300);
#pragma warning disable CS0618
                    vm.DisplayPreview?.Save(Path.Combine(dispDir, "extra.png"));
#pragma warning restore CS0618
                    Console.WriteLine($"[display] extra {vm.DisplaySizeText}");
                }
                if (Environment.GetEnvironmentVariable("TACHIEKARA_DISPLAYSCROLL") == "1"
                    && this.GetVisualDescendants().OfType<DisplayInspector>().FirstOrDefault()?.FindAncestorOfType<ScrollViewer>() is { } sv)
                {
                    // 設定パネルを下まで順に撮る
                    for (int k = 0; k < 12; k++)
                    {
                        sv.Offset = new Avalonia.Vector(0, k * 560);
                        await Win($"inspector_{k:00}");
                        if (sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 1) break;
                    }
                    sv.Offset = default;
                }
                if (Environment.GetEnvironmentVariable("TACHIEKARA_SAVEAS") is { Length: > 0 })
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await vm.ExportCommand.ExecuteAsync(null);
                    Console.WriteLine($"[display] export {sw.ElapsedMilliseconds}ms toast={vm.ToastTitle} / {vm.ToastMessage}");
                    await Win("window_exported");
                }
                Close();
            };
#endif
#if DEBUG
        // 開発用: 実ウィンドウで更新の状態を順に切り替える（外から画面を撮って確認する用）
        if (Environment.GetEnvironmentVariable("TACHIEKARA_LIVESEQ") is { Length: > 0 })
            Opened += async (_, _) =>
            {
                var vm = Vm!;
                await Task.Delay(3000);
                var dlg = new AboutDialog { DataContext = DataContext };
                _ = dlg.ShowDialog(this);
                vm.IsCheckingUpdates = true;
                await Task.Delay(2500);
                vm.IsCheckingUpdates = false;
                vm.UpdateCheckResult = "確認できませんでした: ネットワークに接続できません。インターネット接続を確認してから、もう一度お試しください。";
                await Task.Delay(2500);
                (vm.UpdateVersion, vm.UpdateState) = ("0.1.1", "available");
                await Task.Delay(5000);
                vm.UpdateProgress = 0;
                vm.UpdateState = "downloading";
                for (int i = 0; i <= 100; i += 10) { vm.UpdateProgress = i; await Task.Delay(600); }
                vm.UpdateState = "ready";
                await Task.Delay(5000);
                dlg.Close();
                Close();
            };
#endif
#if DEBUG
        // 開発用: ソフトウェア情報ダイアログを各状態で撮る（TACHIEKARA_ABOUTSNAP=出力先.png）
        if (Environment.GetEnvironmentVariable("TACHIEKARA_ABOUTSNAP") is { Length: > 0 } aboutSnap)
            Opened += async (_, _) =>
            {
                var dlg = new AboutDialog { DataContext = DataContext };
                dlg.Show(this);
                async Task Snap(string suffix)
                {
                    await Task.Delay(900);
                    using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)dlg.Bounds.Width, (int)dlg.Bounds.Height));
                    rtb.Render(dlg);
#pragma warning disable CS0618
                    rtb.Save(Path.ChangeExtension(aboutSnap, null) + suffix + ".png");
#pragma warning restore CS0618
                }
                await Task.Delay(6000);   // 起動時の確認を待つ
                if (Environment.GetEnvironmentVariable("TACHIEKARA_FAKESTATE") is { Length: > 0 } fake)
                {
                    (Vm!.UpdateVersion, Vm.UpdateProgress, Vm.UpdateState) = ("0.1.1", 42, fake);
                    await Task.Delay(500);
                }
                using (var mrtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height)))
                {
                    mrtb.Render(this);
#pragma warning disable CS0618
                    mrtb.Save(Path.ChangeExtension(aboutSnap, null) + "_main.png");
#pragma warning restore CS0618
                }
                await Snap("_a");
                var vm = Vm!;
                if (vm.UpdateState == "available")
                {
                    await vm.DownloadUpdateCommand.ExecuteAsync(null);
                    await Snap("_b");
                }
                else if (vm.CanCheckUpdates)
                {
                    await vm.CheckUpdatesCommand.ExecuteAsync(null);
                    await Snap("_b");
                }
                dlg.Close();
                Close();
            };
#endif
#if DEBUG
        // 開発用: 更新の流れ（確認 → ダウンロード → 再起動で反映）を自動で通す。結果は update-selftest.log へ
        if (Environment.GetEnvironmentVariable("TACHIEKARA_UPDATE_SELFTEST") is { Length: > 0 } logPath)
            Opened += async (_, _) =>
            {
                void Log(string s) => File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {s}\n");
                await Task.Delay(6000);   // 起動時の確認（3 秒後）が終わるのを待つ
                var vm = Vm!;
                Log($"version={vm.AppVersion} installed={vm.CanCheckUpdates} state={vm.UpdateState} available={vm.UpdateVersion}");
                if (vm.UpdateState == "available")
                {
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_SNAPSHOT") is { Length: > 0 } snap0)
                    {
                        using var rtb0 = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height));
                        rtb0.Render(this);
#pragma warning disable CS0618
                        rtb0.Save(Path.ChangeExtension(snap0, null) + "_avail.png");
#pragma warning restore CS0618
                    }
                    await vm.DownloadUpdateCommand.ExecuteAsync(null);
                    Log($"after download: state={vm.UpdateState} progress={vm.UpdateProgress} msg={vm.UpdateMessage}");
                    if (Environment.GetEnvironmentVariable("TACHIEKARA_SNAPSHOT") is { Length: > 0 } snap)
                    {
                        await Task.Delay(800);
                        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height));
                        rtb.Render(this);
#pragma warning disable CS0618
                        rtb.Save(snap);
#pragma warning restore CS0618
                    }
                    if (vm.UpdateState == "ready" && Environment.GetEnvironmentVariable("TACHIEKARA_UPDATE_RESTART") == "1")
                    {
                        Log("restarting to apply");
                        vm.RestartToUpdateCommand.Execute(null);
                        return;
                    }
                }
                Close();
            };
#endif
#if DEBUG
        // 開発用: TACHIEKARA_SNAPSHOT=出力先.png を指定すると、起動後にウィンドウ内容を書き出す
        if (Environment.GetEnvironmentVariable("TACHIEKARA_SNAPSHOT") is { Length: > 0 } snap)
            Opened += async (_, _) => await DebugSnapshotsAsync(snap);
#endif
    }

#if DEBUG
    async Task DebugSnapshotsAsync(string path)
    {
        async Task Shot(string suffix)
        {
            var size = new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
            rtb.Render(this);
#pragma warning disable CS0618
            rtb.Save(Path.ChangeExtension(path, null) + suffix + ".png");
#pragma warning restore CS0618
        }
        await Task.Delay(TimeSpan.FromSeconds(15));
        await Shot("_1");
        if (Environment.GetEnvironmentVariable("TACHIEKARA_DIALOGTEST") is { Length: > 0 })
        {
            var dlg = new ChoiceDialog("サイズ違いの画像があります",
                "ベース（2000 × 3000）と縦横サイズが違う画像が 1件あります。\n含めた場合も同じピクセル位置で切り抜くため、顔の位置がずれる可能性があります。",
                ["⚠ sample.png（1500 × 1500）"], ["除外して作成", "含めて作成", "キャンセル"], DialogKind.Warning);
            dlg.Show(this);
            await Task.Delay(1500);
            var ds = new Avalonia.PixelSize((int)dlg.Bounds.Width, (int)dlg.Bounds.Height);
            using (var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(ds))
            {
                rtb.Render(dlg);
#pragma warning disable CS0618
                rtb.Save(Path.ChangeExtension(path, null) + "_dialog.png");
#pragma warning restore CS0618
            }
            dlg.Close();
        }
        if (Vm is { HasImages: true } v)
        {
            // 枠は画像の大きさに対する割合で決める（顔はだいたい上の方の中央）
            int bw = v.BaseImage?.Width ?? 1000, bh = v.BaseImage?.Height ?? 1500;
            if (Environment.GetEnvironmentVariable("TACHIEKARA_MODE") == "bust")
            {
                v.IsBust = true;
                (v.CropX, v.CropY, v.CropW, v.CropH) = ((int)(bw * 0.3), (int)(bh * 0.13), (int)(bw * 0.4), (int)(bw * 0.5));
            }
            else { v.IsBust = false; (v.CropX, v.CropY, v.CropW) = ((int)(bw * 0.4), (int)(bh * 0.16), (int)(bw * 0.22)); }
            await Task.Delay(300);
            Canvas.ZoomToCrop();
            if (FileList.ItemCount > 2) FileList.SelectedIndex = 2;
            await Task.Delay(4000);
            await Shot("_2");

            if (Environment.GetEnvironmentVariable("TACHIEKARA_RENAMETEST") is { Length: > 0 } && v.Images.Count > 2)
            {
                // 実際の入力欄に文字を入れ、Tab → 次の画像、Enter で確定する流れを確かめる
                TextBox? Box() => (FileList.ContainerFromItem(FileList.SelectedItem!) as Control)?
                    .GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible && t.Classes.Contains("rename"));
                void Key_(Key k) => Box()?.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = k });
                FileList.SelectedIndex = 1;
                OnRename(null, new RoutedEventArgs());
                await Task.Delay(500);
                Console.WriteLine($"[rename] box focused={Box()?.IsFocused} text={Box()?.Text}");
                Box()!.Text = "笑顔";
                Key_(Key.Tab);
                await Task.Delay(500);
                Console.WriteLine($"[rename] after tab: selected={FileList.SelectedIndex} renaming={v.Images[2].IsRenaming} focused={Box()?.IsFocused}");
                await Shot("_rename");
                Box()!.Text = "ぺろっ";
                Key_(Key.Enter);
                await Task.Delay(300);
                Console.WriteLine($"[rename] names: {string.Join(", ", v.Images.Select(i => i.OutputName))}");
            }

            if (Environment.GetEnvironmentVariable("TACHIEKARA_REORDERTEST") is { Length: > 0 } && v.Images.Count > 3)
            {
                string Order() => string.Join(" ", v.Images.Select(i => i.Stem));
                Console.WriteLine($"[reorder] start: {Order()}");
                FileList.SelectedIndex = 2;
                OnListKeyDown(FileList, new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Up, KeyModifiers = KeyModifiers.Alt });
                Console.WriteLine($"[reorder] alt+up (#2): {Order()} sel={FileList.SelectedIndex}");
                FileList.Selection.Clear(); FileList.Selection.Select(0); FileList.Selection.Select(2);
                OnMoveDown(null, new RoutedEventArgs());
                Console.WriteLine($"[reorder] down (#0,#2): {Order()} sel={string.Join(",", FileList.Selection.SelectedIndexes)}");
                OnMoveTop(null, new RoutedEventArgs());
                Console.WriteLine($"[reorder] top: {Order()}");
                var first = v.Images[0];
                MoveSelected(sel => v.MoveTo(sel, v.Images.Count), [first]);
                Console.WriteLine($"[reorder] drop first at end: {Order()}");
                v.IsDisplay = true;
                await Task.Delay(2500);
                _reorderItems = [v.Images[1]]; _reordering = true;
                UpdateDropLine(140);
                Console.WriteLine($"[reorder] dropline index={_reorderIndex}");
                await Task.Delay(300);
                await Shot("_reorder");
                EndReorder();
            }
        }
        if (Environment.GetEnvironmentVariable("TACHIEKARA_AUTOANSWER") is { Length: > 0 } answers && Vm is { } vm)
        {
            _autoAnswers = new Queue<int>(answers.Split(',').Select(int.Parse));
            if (vm.IsFace && vm.BaseImage is { } b0) (vm.CropX, vm.CropY, vm.CropW) = ((int)(b0.Width * 0.42), (int)(b0.Height * 0.17), (int)(b0.Width * 0.2));
            await vm.ExportCommand.ExecuteAsync(null);
            await Task.Delay(800);
            await Shot("_3");
        }
        Close();
    }
#endif

    MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is { } vm) vm.Dialogs = this;
    }

    // ---- ドラッグ＆ドロップ ----

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        DropOverlay.IsVisible = e.DragEffects != DragDropEffects.None;
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = Vm is { IsExporting: false } && e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (Vm is not { IsExporting: false } vm) return;
        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return;
        var paths = items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
        await vm.AddPathsAsync(paths);
    }

    // ---- 追加 ----

    async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "立ち絵を選択",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("画像")
                {
                    Patterns = ImageOps.SupportedExtensions.Select(x => "*" + x).ToArray(),
                    AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "org.webmproject.webp", "com.microsoft.bmp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp"],
                },
            ],
        });
        await Vm.AddPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "立ち絵のフォルダを選択",
            AllowMultiple = true,
        });
        await Vm.AddPathsAsync(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    // ---- 一覧 ----

    List<SourceImage> SelectedImages => FileList.SelectedItems?.OfType<SourceImage>().ToList() ?? [];

    async void OnSetBase(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || SelectedImages.FirstOrDefault() is not { } item) return;
        await Vm.SetBaseAsync(item);
    }

    void OnRemove(object? sender, RoutedEventArgs e) => Vm?.Remove(SelectedImages);

    // ダブルクリックで名前（表情名）を付ける
    void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsInTextBox(e.Source)) return;
        if (Vm is { IsExporting: false } && (e.Source as Control)?.DataContext is SourceImage item)
        {
            FileList.SelectedItem = item;
            StartRename(item);
            e.Handled = true;
        }
    }

    void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsInTextBox(e.Source) || Vm is not { IsExporting: false } vm) return;   // 名前の編集中は一覧の操作をしない
        if (e.Key is Key.Delete or Key.Back)
        {
            vm.Remove(SelectedImages);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            OnRename(sender, e);
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt)
        {
            if (e.Key == Key.Up) OnMoveUp(sender, e); else OnMoveDown(sender, e);
            e.Handled = true;
        }
    }

    static bool IsInTextBox(object? source) =>
        source is Avalonia.Visual v && (v is TextBox || v.FindAncestorOfType<TextBox>() is not null);

    // ---- 名前（表情名） ----

    void OnRename(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is SourceImage item) StartRename(item);
    }

    void StartRename(SourceImage item)
    {
        if (Vm is null) return;
        Vm.BeginRename(item);
        if (!item.IsRenaming) return;
        FileList.ScrollIntoView(item);
        // 入力欄が表示されてからフォーカスを移す
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (FileList.ContainerFromItem(item) is not Control container) return;
            var box = container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("rename"));
            if (box is null) return;
            box.Focus();
            box.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null || (sender as Control)?.DataContext is not SourceImage item) return;
        switch (e.Key)
        {
            case Key.Enter:
                Vm.CommitRename(item);
                FileList.Focus();
                break;
            case Key.Escape:
                Vm.CancelRename(item);
                FileList.Focus();
                break;
            case Key.Tab:
            {
                // Tab で次の画像（Shift+Tab で前の画像）の名前を続けて付ける
                Vm.CommitRename(item);
                var list = Vm.Images;
                var i = list.IndexOf(item) + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                if (i >= 0 && i < list.Count)
                {
                    FileList.SelectedItem = list[i];
                    StartRename(list[i]);
                }
                else FileList.Focus();
                break;
            }
            default:
                return;
        }
        e.Handled = true;
    }

    void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SourceImage item) Vm?.CommitRename(item);
    }

    void OnOpenItemFolder(object? sender, RoutedEventArgs e)
    {
        if (SelectedImages.FirstOrDefault() is { } item) OpenFolder(item.Folder);
    }

    // ---- 手での並べ替え ----

    void OnMoveUp(object? sender, RoutedEventArgs e) => MoveSelected(sel => Vm!.MoveBy(sel, -1));
    void OnMoveDown(object? sender, RoutedEventArgs e) => MoveSelected(sel => Vm!.MoveBy(sel, +1));
    void OnMoveTop(object? sender, RoutedEventArgs e) => MoveSelected(sel => Vm!.MoveTo(sel, 0));
    void OnMoveBottom(object? sender, RoutedEventArgs e) => MoveSelected(sel => Vm!.MoveTo(sel, Vm.Images.Count));

    void MoveSelected(Func<List<SourceImage>, bool> move, List<SourceImage>? items = null)
    {
        if (Vm is null) return;
        var sel = items ?? SelectedImages;
        if (sel.Count == 0 || !move(sel)) return;
        // 並べ替えると選択が外れることがあるので、選び直して見える所へ
        // （SelectedItem を代入すると複数選択が 1 つになるので、主の項目を先に選ぶ）
        var primary = FileList.SelectedItem is SourceImage p && sel.Contains(p) ? p : sel[0];
        FileList.Selection.Clear();
        FileList.Selection.Select(Vm.Images.IndexOf(primary));
        foreach (var s in sel.Where(s => s != primary)) FileList.Selection.Select(Vm.Images.IndexOf(s));
        FileList.ScrollIntoView(primary);
    }

    // ドラッグ: 押した項目（選択中なら選択中のものすべて）を、離した位置へ差し込む
    Point? _reorderStart;
    List<SourceImage>? _reorderItems;
    bool _reordering;
    int _reorderIndex = -1;

    void InitReorder()
    {
        FileList.AddHandler(PointerPressedEvent, OnReorderPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileList.AddHandler(PointerMovedEvent, OnReorderMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileList.AddHandler(PointerReleasedEvent, OnReorderReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileList.AddHandler(PointerCaptureLostEvent, (_, _) => EndReorder(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    void OnReorderPressed(object? sender, PointerPressedEventArgs e)
    {
        EndReorder();
        if (Vm is not { IsExporting: false } || !e.GetCurrentPoint(FileList).Properties.IsLeftButtonPressed) return;
        if (e.KeyModifiers != KeyModifiers.None) return;   // Ctrl / Shift は選択の操作
        if (e.Source is not Avalonia.Visual v || IsInTextBox(v) || v is CheckBox || v.FindAncestorOfType<CheckBox>() is not null) return;
        if (v.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not SourceImage item) return;
        // 押した時点の選択を覚えておく（押すと ListBox が 1 つだけの選択にしてしまうため）
        var sel = SelectedImages;
        _reorderItems = sel.Contains(item) ? sel : [item];
        _reorderStart = e.GetPosition(FileList);
    }

    void OnReorderMoved(object? sender, PointerEventArgs e)
    {
        if (_reorderStart is not { } start || _reorderItems is null || Vm is null) return;
        var p = e.GetPosition(FileList);
        if (!_reordering)
        {
            if (Math.Abs(p.Y - start.Y) < 6) return;
            _reordering = true;
            e.Pointer.Capture(FileList);
            MoveSelectionBack();
        }
        // 端の近くでは一覧を送る
        if (FileList.Scroll is { } sc)
        {
            const double edge = 28;
            var dy = p.Y < edge ? -(edge - p.Y) : p.Y > FileList.Bounds.Height - edge ? p.Y - (FileList.Bounds.Height - edge) : 0;
            if (dy != 0) sc.Offset = new Vector(sc.Offset.X, Math.Clamp(sc.Offset.Y + dy * 0.6, 0, Math.Max(0, sc.Extent.Height - sc.Viewport.Height)));
        }
        UpdateDropLine(p.Y);
        e.Handled = true;
    }

    void OnReorderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_reordering && _reorderItems is { } items && _reorderIndex >= 0)
        {
            var index = _reorderIndex;
            EndReorder();
            e.Pointer.Capture(null);
            MoveSelected(sel => Vm!.MoveTo(sel, index), items);
            e.Handled = true;
            return;
        }
        EndReorder();
    }

    // ListBox が押した時点で選択を 1 つにしてしまうので、ドラッグが始まったら選択を戻す
    void MoveSelectionBack()
    {
        if (_reorderItems is not { Count: > 1 } items || Vm is null) return;
        FileList.Selection.Clear();
        foreach (var s in items) FileList.Selection.Select(Vm.Images.IndexOf(s));
    }

    void UpdateDropLine(double y)
    {
        // 見えている項目の中で、指の位置がその項目の上半分にある最初のもの → その前に差し込む
        int index = -1;
        double lineY = 0, lastBottom = 0;
        int lastIndex = -1;
        foreach (var c in FileList.GetRealizedContainers().OfType<Control>())
        {
            int i = FileList.IndexFromContainer(c);
            if (i < 0 || c.TranslatePoint(new Point(0, 0), FileList) is not { } tl) continue;
            var top = tl.Y; var bottom = top + c.Bounds.Height;
            if (y < top + c.Bounds.Height / 2 && (index < 0 || i < index)) { index = i; lineY = top; }
            if (i > lastIndex) { lastIndex = i; lastBottom = bottom; }
        }
        if (index < 0) { index = lastIndex + 1; lineY = lastBottom; }
        _reorderIndex = index;
        DropLine.Margin = new Thickness(10, Math.Max(0, lineY - 1.5), 10, 0);
        DropLine.IsVisible = true;
    }

    void EndReorder()
    {
        _reorderStart = null;
        _reorderItems = null;
        _reordering = false;
        _reorderIndex = -1;
        DropLine.IsVisible = false;
    }

    // ---- ソフトウェア情報 ----

    async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog { DataContext = DataContext };
        await dlg.ShowDialog(this);
    }

    // ---- 表示 ----

    /// <summary>今表示しているキャンバス（ディスプレイ画像のときはそのプレビュー）。</summary>
    CropCanvas ActiveCanvas => Vm?.IsDisplay == true ? DisplayCanvas : Canvas;

    void OnFit(object? sender, RoutedEventArgs e) => ActiveCanvas.FitToView();
    void OnZoomIn(object? sender, RoutedEventArgs e) => ActiveCanvas.ZoomBy(1.25);
    void OnZoomOut(object? sender, RoutedEventArgs e) => ActiveCanvas.ZoomBy(1 / 1.25);
    void OnZoomToCrop(object? sender, RoutedEventArgs e) => Canvas.ZoomToCrop();

    // ---- IDialogs ----

#if DEBUG
    Queue<int>? _autoAnswers;
#endif

    public async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string>? details, params string[] buttons)
    {
#if DEBUG
        if (_autoAnswers is { Count: > 0 })
        {
            var a = _autoAnswers.Dequeue();
            Console.WriteLine($"[auto] {title}: {message} => {buttons[a]}\n  {string.Join("\n  ", details ?? [])}");
            return a;
        }
#endif
        var kind = title.Contains("サイズ違い") || title.Contains("同じ名前") ? DialogKind.Warning
                 : buttons.Length > 2 ? DialogKind.Question : DialogKind.Info;
        var dlg = new ChoiceDialog(title, message, details, buttons, kind);
        return await dlg.ShowDialog<int?>(this) ?? -1;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return r.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string? folder, string ext)
    {
#if DEBUG
        // 開発用: 保存先を自動で決める
        if (Environment.GetEnvironmentVariable("TACHIEKARA_SAVEAS") is { Length: > 0 } auto) return auto;
#endif
        IStorageFolder? start = null;
        if (folder is not null)
            try { start = await StorageProvider.TryGetFolderFromPathAsync(folder); } catch { }
        var type = ext == "jpg"
            ? new FilePickerFileType("JPEG 画像") { Patterns = ["*.jpg", "*.jpeg"], AppleUniformTypeIdentifiers = ["public.jpeg"], MimeTypes = ["image/jpeg"] }
            : new FilePickerFileType("PNG 画像") { Patterns = ["*.png"], AppleUniformTypeIdentifiers = ["public.png"], MimeTypes = ["image/png"] };
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = start,
            DefaultExtension = ext,
            FileTypeChoices = [type],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickImageAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("画像")
                {
                    Patterns = ImageOps.SupportedExtensions.Select(x => "*" + x).ToArray(),
                    AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "org.webmproject.webp", "com.microsoft.bmp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp"],
                },
            ],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public void OpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", [path]);
            else
                Process.Start("xdg-open", [path]);
        }
        catch { /* 開けなくても致命的ではない */ }
    }
}
