using System.Windows;
using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;

namespace PCL;

public partial class PageSetupDownloadSource : IPageView
{
    private bool _isLoaded;
    private bool _isRefreshing;

    public PageSetupDownloadSource()
    {
        InitializeComponent();
        Loaded += PageSetupDownloadSource_Loaded;
    }

    public void OnNavigatedTo(object? parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }

    private void PageSetupDownloadSource_Loaded(object sender, RoutedEventArgs e)
    {
        PanBack.ScrollToHome();
        if (_isLoaded) return;
        _isLoaded = true;
        RefreshSourceList();
    }

    /// <summary>清空所有自定义下载源。</summary>
    public void Reset()
    {
        DownloadSources.SaveSources([], 0);
        if (_isLoaded)
            RefreshSourceList();
    }

    private void RefreshSourceList()
    {
        _isRefreshing = true;
        try
        {
            PanSourceList.Children.Clear();
            var sources = DownloadSources.MirrorBases;
            var activeIndex = DownloadSources.ActiveIndex;
            for (var i = 0; i < sources.Count; i++)
            {
                var index = i;
                var item = new MyListItem
                {
                    Type = MyListItem.CheckType.RadioBox,
                    Title = sources[i],
                    Height = 36
                };
                item.Check += (_, _) =>
                {
                    if (!_isRefreshing)
                        SetActiveSource(index);
                };
                var btnDelete = new MyIconButton
                {
                    SvgIcon = "lucide/trash-2"
                };
                btnDelete.TrySetLocalizedValue("ToolTip", "Common.Action.Delete");
                btnDelete.Click += (_, _) => DeleteSource(index);
                item.Buttons = [btnDelete];
                PanSourceList.Children.Add(item);
                if (i == activeIndex)
                    item.SetChecked(true, false, false);
            }

            TextEmpty.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void SetActiveSource(int index)
    {
        var sources = DownloadSources.MirrorBases;
        if (index < 0 || index >= sources.Count) return;
        DownloadSources.SaveSources(sources, index);
    }

    private void DeleteSource(int index)
    {
        var sources = DownloadSources.MirrorBases.ToList();
        if (index < 0 || index >= sources.Count) return;

        var activeIndex = DownloadSources.ActiveIndex;
        sources.RemoveAt(index);
        if (index < activeIndex) activeIndex--;
        DownloadSources.SaveSources(sources, activeIndex);
        RefreshSourceList();
    }

    private void BtnAddSource_Click(object sender, MouseButtonEventArgs e)
    {
        AddSource();
    }

    private void TextNewSource_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddSource();
        e.Handled = true;
    }

    private void AddSource()
    {
        var normalized = DownloadSources.NormalizeSource(TextNewSource.Text);
        if (normalized.Length == 0 || !DownloadSources.IsValidSource(normalized))
        {
            HintService.Show(Hints.Error(Lang.Text("Setup.DownloadSource.Add.Invalid")));
            return;
        }

        var sources = DownloadSources.MirrorBases.ToList();
        if (sources.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            HintService.Show(Hints.Error(Lang.Text("Setup.DownloadSource.Add.Duplicate")));
            return;
        }

        sources.Add(normalized);
        DownloadSources.SaveSources(sources, sources.Count - 1);
        TextNewSource.Text = "";
        RefreshSourceList();
    }
}
