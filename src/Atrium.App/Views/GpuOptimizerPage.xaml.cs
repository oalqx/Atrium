using Atrium.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atrium.App.Views;

public sealed partial class GpuOptimizerPage : Page
{
    public GpuOptimizerViewModel ViewModel { get; }

    public GpuOptimizerPage()
    {
        ViewModel = App.GetService<GpuOptimizerViewModel>();
        this.InitializeComponent();
    }

    private async void OnToggleSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.Tag is GameGpuItemViewModel item)
        {
            if (ts.IsOn != item.IsHighPerformance)
            {
                await item.ToggleGpuPreferenceCommand.ExecuteAsync(null);
            }
        }
    }
}
