using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PCL.Desktop.ViewModels;

namespace PCL.Desktop.Views;

public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();
    }

    private async void OnExportInstanceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = viewModel.ExportDialogTitle,
                SuggestedFileName = viewModel.ExportDefaultName,
                DefaultExtension = "zip",
                FileTypeChoices =
                [
                    new FilePickerFileType("Instance archive") { Patterns = ["*.zip"] }
                ]
            });

        if (file?.TryGetLocalPath() is { Length: > 0 } destination)
            await viewModel.ExportInstanceAsync(destination);
    }
}
