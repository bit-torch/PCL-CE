using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PCL.Desktop.ViewModels;

namespace PCL.Desktop.Views;

public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
    }

    private async void OnImportModpackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel viewModel)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = viewModel.ImportDialogTitle,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Modrinth Modpack")
                    {
                        Patterns = ["*.mrpack"],
                        MimeTypes = ["application/x-modrinth-modpack+zip", "application/zip"]
                    }
                ]
            });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path)
            await viewModel.ImportModpackAsync(path);
    }
}
