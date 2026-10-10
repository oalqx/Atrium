using Atrium.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atrium.App.Views;

public sealed partial class SourcesPage : Page
{
    public SourcesViewModel ViewModel { get; }

    public SourcesPage()
    {
        ViewModel = App.GetService<SourcesViewModel>();
        this.InitializeComponent();
    }

    private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
    {
        var text = NewFolderInput.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (await ViewModel.AddCustomFolderAsync(text)) NewFolderInput.Text = string.Empty;
        }
    }

    private async void OnRemoveFolderClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            await ViewModel.RemoveCustomFolderAsync(path);
        }
    }
}
