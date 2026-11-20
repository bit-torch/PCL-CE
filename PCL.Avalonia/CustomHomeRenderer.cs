using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PCL.Avalonia;

/// <summary>A declarative subset of custom home XAML. It does not instantiate CLR types or evaluate markup extensions.</summary>
public static class CustomHomeRenderer
{
    public static Control Render(string xml, Action<Uri> openLink)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 2_000_000, XmlResolver = null });
        var document = XDocument.Load(reader);
        return Build(document.Root ?? throw new InvalidDataException("主页内容为空。"), openLink, 0);
    }
    private static Control Build(XElement element, Action<Uri> openLink, int depth)
    {
        if (depth > 40) throw new InvalidDataException("主页嵌套层级过多。");
        if (element.Attributes().Any(a => a.Name.LocalName is "Class" or "Code" || a.Value.Contains('{')))
            throw new InvalidDataException("主页不允许执行代码、绑定或标记扩展。");
        string Value(string name, string fallback = "") => element.Attribute(name)?.Value ?? fallback;
        Control control;
        switch (element.Name.LocalName)
        {
            case "StackPanel": case "MyCard":
                var panel = new StackPanel { Spacing = 10, Orientation = Value("Orientation") == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical };
                foreach (var child in element.Elements()) panel.Children.Add(Build(child, openLink, depth + 1));
                control = panel; break;
            case "WrapPanel":
                var wrap = new WrapPanel { Orientation = Value("Orientation") == "Vertical" ? Orientation.Vertical : Orientation.Horizontal };
                foreach (var child in element.Elements()) wrap.Children.Add(Build(child, openLink, depth + 1));
                control = wrap; break;
            case "Grid":
                var grid = new Grid();
                foreach (var definition in element.Elements().Where(e => e.Name.LocalName == "Grid.RowDefinitions").Elements())
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Parse(definition.Attribute("Height")?.Value ?? "*")));
                foreach (var definition in element.Elements().Where(e => e.Name.LocalName == "Grid.ColumnDefinitions").Elements())
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse(definition.Attribute("Width")?.Value ?? "*")));
                foreach (var child in element.Elements().Where(e => !e.Name.LocalName.Contains('.')))
                {
                    var item = Build(child, openLink, depth + 1);
                    int Index(string attribute, int fallback) => int.TryParse(child.Attribute(attribute)?.Value, out var value) ? Math.Clamp(value, fallback, 1000) : fallback;
                    Grid.SetRow(item, Index("Grid.Row", 0)); Grid.SetColumn(item, Index("Grid.Column", 0));
                    Grid.SetRowSpan(item, Index("Grid.RowSpan", 1)); Grid.SetColumnSpan(item, Index("Grid.ColumnSpan", 1));
                    grid.Children.Add(item);
                }
                control = grid; break;
            case "TextBlock": case "Label": case "Run":
                control = new TextBlock { Text = Value("Text", Value("Content", element.Value)), TextWrapping = TextWrapping.Wrap, FontSize = double.TryParse(Value("FontSize"), System.Globalization.CultureInfo.InvariantCulture, out var fontSize) ? Math.Clamp(fontSize, 8, 80) : 15 }; break;
            case "Button": case "MyButton":
                var address = Value("NavigateUri", Value("EventData"));
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidDataException("主页按钮只允许打开 HTTP(S) 链接。");
                var button = new Button { Content = Value("Text", Value("Content", element.Value)) };
                button.Click += (_, _) => openLink(uri); control = button; break;
            case "Border":
                var content = element.Elements().FirstOrDefault(); control = new Border { Padding = Thickness.Parse(Value("Padding", "14")), Child = content is null ? null : Build(content, openLink, depth + 1) }; break;
            case "ScrollViewer":
                var scrollContent = element.Elements().FirstOrDefault();
                control = new ScrollViewer { Content = scrollContent is null ? null : Build(scrollContent, openLink, depth + 1) }; break;
            case "Separator": control = new Separator(); break;
            default: throw new InvalidDataException("尚不支持的主页控件：" + element.Name.LocalName);
        }
        control.Margin = Thickness.Parse(Value("Margin", "0"));
        return control;
    }
}
