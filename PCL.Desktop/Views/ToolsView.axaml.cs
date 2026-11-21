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

    private async void OnImportSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel viewModel)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = viewModel.ImportSaveDialogTitle,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("World archive") { Patterns = ["*.zip"] }
                ]
            });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path)
            await viewModel.ImportSaveAsync(path);
    }

    private async void OnExportSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel viewModel)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = viewModel.ExportSaveDialogTitle,
                SuggestedFileName = viewModel.ExportSaveDefaultName,
                DefaultExtension = "zip",
                FileTypeChoices =
                [
                    new FilePickerFileType("World archive") { Patterns = ["*.zip"] }
                ]
            });

        if (file?.TryGetLocalPath() is { Length: > 0 } destination)
            await viewModel.ExportSelectedSaveAsync(destination);
    }

    private async void OnExportDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel viewModel)
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
                    new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }
                ]
            });

        if (file?.TryGetLocalPath() is { Length: > 0 } destination)
            await viewModel.ExportDiagnosticsAsync(destination);
    }

    private void OnApplyUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel viewModel)
            return;

        if (!viewModel.TryStartUpdate())
            return;

        (TopLevel.GetTopLevel(this) as Window)?.Close();
    }
}
