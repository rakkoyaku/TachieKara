using Avalonia.Controls;
using Avalonia.Interactivity;
using TachieKara.ViewModels;

namespace TachieKara.Views;

/// <summary>ディスプレイ画像の設定パネル。DataContext は MainViewModel。</summary>
public partial class DisplayInspector : UserControl
{
    public DisplayInspector() => InitializeComponent();

    void OnSwatch(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && DataContext is MainViewModel vm)
            vm.MatchColorsCommand.Execute(hex);
    }

    void OnApplyTemplate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && DataContext is MainViewModel vm)
            vm.ApplyTemplateCommand.Execute(name);
    }
}
