using System.Text.Json;

namespace PCL.Kernel.Resources;

/// <summary>收藏的一条资源项目摘要，可在离线状态下展示。</summary>
public sealed record FavoriteResource(
    string Id,
    string Slug,
    string Title,
    string Author,
    string ProjectType,
    string? IconUrl)
{
    public string Subtitle =>
        string.IsNullOrWhiteSpace(Author) ? ProjectType : $"{ProjectType} · {Author}";
}

/// <summary>把资源收藏保存到启动器数据目录，按最近收藏排序。</summary>
public sealed class FavoritesStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly List<FavoriteResource> _items = [];

    public FavoritesStore(string path)
    {
        _path = path;
        Load();
    }

    public IReadOnlyList<FavoriteResource> Items => _items;

    public bool Contains(string? id) =>
        id is { Length: > 0 } && _items.Any(item => item.Id == id);

    /// <summary>已收藏则取消，未收藏则置顶收藏。返回 true 表示新增。</summary>
    public bool Toggle(FavoriteResource favorite)
    {
        if (_items.RemoveAll(item => item.Id == favorite.Id) > 0)
        {
            Save();
            return false;
        }

        _items.Insert(0, favorite);
        Save();
        return true;
    }

    public bool Remove(string? id)
    {
        if (id is not { Length: > 0 } || _items.RemoveAll(item => item.Id == id) == 0)
            return false;

        Save();
        return true;
    }

    private void Load()
    {
        _items.Clear();

        try
        {
            if (!File.Exists(_path))
                return;

            var items = JsonSerializer.Deserialize<List<FavoriteResource>>(
                File.ReadAllText(_path), Options);

            if (items is null)
                return;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) ||
                    _items.Any(existing => existing.Id == item.Id))
                {
                    continue;
                }

                _items.Add(item);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 收藏文件损坏时按空列表处理，不阻塞启动。
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, Options));
            File.Move(temp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写入失败时保留内存中的收藏，下次修改会再次尝试。
        }
    }
}
