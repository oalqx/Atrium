using Atrium.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Atrium.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        this.InitializeComponent();
    }
}
