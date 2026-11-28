using System.Text.Json.Serialization;

namespace PCL.Kernel.Homepage;

/// <summary>Minecraft 官网信息流中的一条新闻。</summary>
public sealed class NewsItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("imageAltText")]
    public string? ImageAltText { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonIgnore]
    public DateTime PublishDate => DateTimeOffset.FromUnixTimeSeconds(Time).LocalDateTime;
}

internal sealed class NewsResponse
{
    [JsonPropertyName("result")]
    public NewsResult? Result { get; set; }
}

internal sealed class NewsResult
{
    [JsonPropertyName("results")]
    public List<NewsItem>? Results { get; set; }

    [JsonPropertyName("numFound")]
    public int NumFound { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }
}
