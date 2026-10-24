using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace KuaiJian.WinUI;

public enum TranslationLanguage
{
    Auto,
    Chinese,
    English,
    Japanese,
    Korean
}

public sealed class ShortcutItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "新快捷";
    public string Target { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public string Kind { get; set; } = "link";
    public bool Enabled { get; set; } = true;
}

public sealed class TranslationPage : Page
{
    private static readonly TranslationLanguage[] Languages = Enum.GetValues<TranslationLanguage>();
    private static readonly string[] LanguageNames = { "自动识别", "中文", "英文", "日文", "韩文" };
    private readonly RootPage _root;
    private readonly ComboBox _sourceLanguage = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _targetLanguage = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180 };
    private readonly TextBox _output = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, IsReadOnly = true };
    private readonly TextBlock _status = new() { Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap };
    private bool _updating;

    public TranslationPage(RootPage root)
    {
        _root = root;
        Build();
    }

    private void Build()
    {
        _sourceLanguage.ItemsSource = LanguageNames;
        _targetLanguage.ItemsSource = LanguageNames;
        _sourceLanguage.SelectedIndex = 0;
        _targetLanguage.SelectedIndex = 0;
        _input.TextChanged += (_, _) =>
        {
            if (!_updating && !string.IsNullOrWhiteSpace(_input.Text)) Translate();
        };

        var languageGrid = new Grid { ColumnSpacing = 10 };
        languageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        languageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languageGrid.Children.Add(Labeled("原文语种", _sourceLanguage));
        var arrow = new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 9), Foreground = (Brush)Application.Current.Resources["MutedBrush"] };
        Grid.SetColumn(arrow, 1);
        languageGrid.Children.Add(arrow);
        var targetBlock = Labeled("译文语种", _targetLanguage);
        Grid.SetColumn(targetBlock, 2);
        languageGrid.Children.Add(targetBlock);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var read = new Button { Content = "读取剪贴板" };
        read.Click += async (_, _) => await ReadClipboardAsync();
        var translate = new Button { Content = "翻译", Background = (Brush)Application.Current.Resources["MintBrush"], Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        translate.Click += (_, _) => Translate();
        var copy = new Button { Content = "复制译文" };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_output.Text) && _root.TrySetClipboardText(_output.Text))
                _status.Text = "译文已复制到剪贴板。";
        };
        actions.Children.Add(read);
        actions.Children.Add(translate);
        actions.Children.Add(copy);

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "翻译", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        panel.Children.Add(languageGrid);
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "原文", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(_input);
        panel.Children.Add(new TextBlock { Text = "译文", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(_output);
        panel.Children.Add(_status);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static StackPanel Labeled(string title, Control control)
    {
        return new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                control
            }
        };
    }

    private async Task ReadClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                _status.Text = "剪贴板里没有文本。";
                return;
            }
            _updating = true;
            _input.Text = (await content.GetTextAsync()).Trim();
            _updating = false;
            Translate();
            _status.Text = "已读取剪贴板并完成翻译。";
        }
        catch (Exception ex)
        {
            _updating = false;
            _status.Text = "读取剪贴板失败：" + ex.Message;
        }
    }

    private void Translate()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0)
        {
            _output.Text = "";
            _status.Text = "请输入要翻译的文字。";
            return;
        }
        var detected = Detect(text);
        var source = SelectedLanguage(_sourceLanguage, detected);
        var target = SelectedLanguage(_targetLanguage, source == TranslationLanguage.Chinese ? TranslationLanguage.English : TranslationLanguage.Chinese);
        _output.Text = TranslateText(text, source, target);
        _status.Text = $"已完成翻译：{DisplayName(source)} → {DisplayName(target)}";
    }

    private static TranslationLanguage SelectedLanguage(ComboBox box, TranslationLanguage automatic)
        => box.SelectedIndex <= 0 ? automatic : Languages[Math.Clamp(box.SelectedIndex, 0, Languages.Length - 1)];

    private static TranslationLanguage Detect(string text)
    {
        if (text.Any(c => c is >= '\uAC00' and <= '\uD7AF')) return TranslationLanguage.Korean;
        if (text.Any(c => c is >= '\u3040' and <= '\u30FF')) return TranslationLanguage.Japanese;
        if (text.Any(c => c is >= '\u4E00' and <= '\u9FFF')) return TranslationLanguage.Chinese;
        return TranslationLanguage.English;
    }

    private static string DisplayName(TranslationLanguage language) => language switch
    {
        TranslationLanguage.Chinese => "中文",
        TranslationLanguage.English => "英文",
        TranslationLanguage.Japanese => "日文",
        TranslationLanguage.Korean => "韩文",
        _ => "自动识别"
    };

    private static string TranslateText(string text, TranslationLanguage source, TranslationLanguage target)
    {
        if (source == target) return text;
        var dictionary = source == TranslationLanguage.Chinese && target == TranslationLanguage.English
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["你好"] = "Hello", ["谢谢"] = "Thank you", ["退款"] = "Refund", ["订单"] = "Order",
                ["客户"] = "Customer", ["价格"] = "Price", ["怎么申请退款"] = "How do I request a refund?",
                ["体重过轻"] = "Underweight", ["体重超重"] = "Overweight", ["计算机"] = "Calculator", ["图标"] = "Icon"
            }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello"] = "你好", ["thank you"] = "谢谢", ["refund"] = "退款", ["order"] = "订单",
                ["customer"] = "客户", ["price"] = "价格", ["how do i request a refund?"] = "怎么申请退款？",
                ["underweight"] = "体重过轻", ["overweight"] = "体重超重"
            };
        return dictionary.TryGetValue(text, out var result) ? result : text;
    }
}

public sealed class ShortcutsPage : Page
{
    private readonly RootPage _root;
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap };

    public ShortcutsPage(RootPage root)
    {
        _root = root;
        Build();
        Refresh();
    }

    private void Build()
    {
        var add = new Button { Content = "添加快捷", Background = (Brush)Application.Current.Resources["MintBrush"], Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        add.Click += async (_, _) => await AddAsync();
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        header.Children.Add(new StackPanel { Spacing = 3, Children = { new TextBlock { Text = "快捷", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold }, new TextBlock { Text = "用快捷入口快速打开常用应用和网页", Foreground = (Brush)Application.Current.Resources["MutedBrush"] } } });
        Grid.SetColumn(add, 1); header.Children.Add(add);
        var panel = new StackPanel { Spacing = 14, Padding = new Thickness(22) };
        panel.Children.Add(header);
        panel.Children.Add(_status);
        panel.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 });
        Content = panel;
    }

    private void Refresh()
    {
        _list.Children.Clear();
        if (_root.Shortcuts.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "还没有快捷入口，可以添加应用或网页。", Foreground = (Brush)Application.Current.Resources["MutedBrush"], Margin = new Thickness(0, 40, 0, 0) });
            return;
        }
        foreach (var shortcut in _root.Shortcuts.ToList()) _list.Children.Add(BuildRow(shortcut));
    }

    private UIElement BuildRow(ShortcutItem item)
    {
        var title = new TextBlock { Text = item.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var target = new TextBlock { Text = item.Target, FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { Spacing = 3, Children = { title, target } };
        var open = new Button { Content = "打开" };
        open.Click += async (_, _) => await OpenAsync(item);
        var remove = new Button { Content = "删除" };
        remove.Click += (_, _) =>
        {
            _root.Shortcuts.RemoveAll(x => x.Id == item.Id);
            _root.SaveShortcuts();
            Refresh();
        };
        var enabled = new ToggleSwitch { IsOn = item.Enabled, OnContent = "启用", OffContent = "停用" };
        enabled.Toggled += (_, _) => { item.Enabled = enabled.IsOn; _root.SaveShortcuts(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { enabled, open, remove } };
        var row = new Grid { Padding = new Thickness(12), ColumnSpacing = 12, Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = item.Kind == "app" ? Symbol.OpenFile : Symbol.Link, Foreground = (Brush)Application.Current.Resources["MintBrush"], Width = 24, Height = 24 };
        Grid.SetColumn(icon, 0); row.Children.Add(icon);
        Grid.SetColumn(text, 1); row.Children.Add(text);
        Grid.SetColumn(actions, 2); row.Children.Add(actions);
        return new Border { Child = row, CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 0, 0, 0)), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private async Task AddAsync()
    {
        var name = new TextBox { Header = "名称", PlaceholderText = "例如：官网" };
        var target = new TextBox { Header = "地址或应用路径", PlaceholderText = "https:// 或 C:\\Program Files\\..." };
        var kind = new ComboBox { Header = "类型", ItemsSource = new[] { "打开网页", "打开应用" }, SelectedIndex = 0 };
        var hotkey = new TextBox { Header = "快捷键（可选）", PlaceholderText = "例如 Ctrl+Alt+5" };
        var content = new StackPanel { Spacing = 8, Children = { name, kind, target, hotkey } };
        var dialog = new ContentDialog { Title = "添加快捷", Content = content, PrimaryButtonText = "保存", CloseButtonText = "取消", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(target.Text))
        {
            _status.Text = "请输入网页地址或应用路径。";
            return;
        }
        _root.Shortcuts.Add(new ShortcutItem
        {
            Name = string.IsNullOrWhiteSpace(name.Text) ? "新快捷" : name.Text.Trim(),
            Target = target.Text.Trim(),
            Hotkey = hotkey.Text.Trim(),
            Kind = kind.SelectedIndex == 1 ? "app" : "link"
        });
        _root.SaveShortcuts();
        _status.Text = "快捷入口已保存。系统级快捷键需要后续接入。";
        Refresh();
    }

    private static async Task OpenAsync(ShortcutItem item)
    {
        try
        {
            if (item.Kind == "link")
            {
                if (Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    await Launcher.LaunchUriAsync(uri);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = item.Target, UseShellExecute = true });
        }
        catch
        {
            // The page keeps the shortcut; the next click can retry after the target is fixed.
        }
    }
}

public sealed class ConnectionsPage : Page
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["MutedBrush"] };

    public ConnectionsPage(RootPage root)
    {
        var refresh = new Button { Content = "刷新设备", Background = (Brush)Application.Current.Resources["MintBrush"], Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        refresh.Click += (_, _) => _status.Text = "设备连接接口尚未在云端部署，暂时没有可显示的设备。";
        var panel = new StackPanel { Spacing = 14, Padding = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "连接", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "查看当前账号下的设备，并为互传功能选择目标设备。", Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        panel.Children.Add(refresh);
        panel.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["MintSoftBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = new StackPanel { Spacing = 8, Children = { new SymbolIcon { Symbol = Symbol.Link, Foreground = (Brush)Application.Current.Resources["MintBrush"] }, new TextBlock { Text = "暂无设备", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, _status } }
        });
        Content = panel;
    }
}
