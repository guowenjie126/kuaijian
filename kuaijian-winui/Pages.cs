using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;
using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.System;
using WinRT.Interop;

namespace KuaiJian.WinUI;

public sealed class ClipboardPage : Page
{
    private readonly RootPage _root;
    private readonly bool _favoritesOnly;
    private readonly TextBox _search = new() { PlaceholderText = "搜索剪贴板内容" };
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly List<Button> _categoryButtons = new();
    private Button? _clearSearch;
    private Grid? _imageTools;
    private TextBlock? _imageScaleLabel;
    private double _imageScale = 1;
    private string _category = "全部";

    public ClipboardPage(RootPage root, bool favoritesOnly)
    {
        _root = root;
        _favoritesOnly = favoritesOnly;
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
        _root.ClipboardStatusChanged += OnClipboardStatusChanged;
        Unloaded += (_, _) => _root.ClipboardStatusChanged -= OnClipboardStatusChanged;
        Build();
        Refresh();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        await _root.CaptureFilePathsAsync(items
            .Where(item => item is StorageFile or StorageFolder)
            .Select(item => item.Path));
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private void Build()
    {
        var rootPanel = new Grid { RowSpacing = 10, Padding = new Thickness(22) };
        for (var i = 0; i < 6; i++) rootPanel.RowDefinitions.Add(new RowDefinition { Height = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var title = new TextBlock { Text = _favoritesOnly ? "我的收藏" : "我的剪贴板", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["InkBrush"] };
        Grid.SetRow(title, 0); rootPanel.Children.Add(title);
        var categories = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        foreach (var value in new[] { "全部", "文本", "图片", "网址" })
        {
            var button = new Button { Content = value, Tag = value };
            button.Click += (_, _) => { _category = value; Refresh(); };
            _categoryButtons.Add(button);
            categories.Children.Add(button);
        }
        Grid.SetRow(categories, 1); rootPanel.Children.Add(categories);
        _imageScaleLabel = new TextBlock { Text = "100%", Width = 52, VerticalAlignment = VerticalAlignment.Center };
        var imageScale = new Slider { Minimum = 0.7, Maximum = 2.2, StepFrequency = 0.1, Value = _imageScale, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Stretch };
        imageScale.ValueChanged += (_, _) =>
        {
            _imageScale = imageScale.Value;
            _imageScaleLabel.Text = $"{_imageScale:P0}";
            Refresh();
        };
        var resetImageScale = new Button { Content = "重置" };
        resetImageScale.Click += (_, _) => imageScale.Value = 1;
        _imageTools = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
        _imageTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _imageTools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _imageTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _imageTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var imageLabel = new TextBlock { Text = "图片大小", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(imageLabel, 0);
        Grid.SetColumn(imageScale, 1);
        Grid.SetColumn(_imageScaleLabel, 2);
        Grid.SetColumn(resetImageScale, 3);
        _imageTools.Children.Add(imageLabel);
        _imageTools.Children.Add(imageScale);
        _imageTools.Children.Add(_imageScaleLabel);
        _imageTools.Children.Add(resetImageScale);
        Grid.SetRow(_imageTools, 2); rootPanel.Children.Add(_imageTools);
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.TextChanged += (_, _) => Refresh();
        searchGrid.Children.Add(_search);
        _clearSearch = new Button { Content = "清空", Visibility = Visibility.Collapsed, Margin = new Thickness(6, 0, 0, 0) };
        _clearSearch.Click += (_, _) => _search.Text = "";
        Grid.SetColumn(_clearSearch, 1);
        searchGrid.Children.Add(_clearSearch);
        Grid.SetRow(searchGrid, 3); rootPanel.Children.Add(searchGrid);
        Grid.SetRow(_status, 4); rootPanel.Children.Add(_status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        Grid.SetRow(scroll, 5); rootPanel.Children.Add(scroll);
        Content = rootPanel;
        RefreshStatus();
    }

    private void OnClipboardStatusChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshStatus);

    private void RefreshStatus()
    {
        var messages = new[] { _root.ClipboardStatus, _root.CloudSyncStatus }
            .Where(message => !string.IsNullOrWhiteSpace(message));
        _status.Text = string.Join("\n", messages);
        _status.Visibility = string.IsNullOrWhiteSpace(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Refresh()
    {
        _list.Children.Clear();
        var query = _search.Text.Trim().ToLowerInvariant();
        if (_clearSearch is not null) _clearSearch.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in _categoryButtons)
        {
            var selected = Equals(button.Tag, _category);
            button.Background = selected ? (Brush)Application.Current.Resources["MintSoftBrush"] : null;
            button.Foreground = selected ? (Brush)Application.Current.Resources["MintBrush"] : (Brush)Application.Current.Resources["InkBrush"];
        }
        if (_imageTools is not null)
            _imageTools.Visibility = _category == "图片" ? Visibility.Visible : Visibility.Collapsed;
        var items = _root.Items.Where(item =>
        {
            if (_favoritesOnly && !item.Favorite) return false;
            // FileDropList entries use ClipKind.Text for compatibility with
            // the macOS data model, but they are not plain-text records and
            // must stay out of the 文本 filter.
            if (_category == "文本" && (item.Kind != ClipKind.Text || item.FileUrls.Count > 0)) return false;
            if (_category == "图片" && item.Kind != ClipKind.Image) return false;
            if (_category == "网址" && item.Kind != ClipKind.Url) return false;
            if (query.Length == 0) return true;
            return item.Title.ToLowerInvariant().Contains(query) || item.Text.ToLowerInvariant().Contains(query) || item.Source.ToLowerInvariant().Contains(query) || item.Note.ToLowerInvariant().Contains(query);
        }).OrderByDescending(x => x.Pinned).ThenByDescending(x => x.CreatedAt).ToList();

        if (items.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = _favoritesOnly ? "还没有收藏内容" : "复制内容后会显示在这里", Foreground = (Brush)Application.Current.Resources["MutedBrush"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 80, 0, 0) });
            return;
        }
        foreach (var item in items) _list.Children.Add(BuildRow(item));
    }

    private UIElement BuildRow(ClipboardItem item)
    {
        var body = new StackPanel { Spacing = 3 };
        var title = item.Title.Length > 120 ? item.Title[..120] + "…" : item.Title;
        body.Children.Add(new TextBlock { Text = title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)Application.Current.Resources["InkBrush"] });
        var kindLabel = item.FileUrls.Count > 0
            ? "文件"
            : item.Kind switch { ClipKind.Image => "图片", ClipKind.Url => "网址", _ => "文本" };
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        meta.Children.Add(new TextBlock
        {
            Text = $"{kindLabel}  ·  来源：{item.Source}  ·  {RelativeTime(item.CreatedAt)}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        if (item.Favorite)
        {
            var favorite = new SymbolIcon
            {
                Symbol = Symbol.Favorite,
                Width = 12,
                Height = 12,
                Foreground = (Brush)Application.Current.Resources["MintBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(favorite, "已收藏");
            ToolTipService.SetToolTip(favorite, "已收藏");
            meta.Children.Add(favorite);
        }
        if (item.Pinned)
        {
            var pinned = new SymbolIcon
            {
                Symbol = Symbol.Pin,
                Width = 12,
                Height = 12,
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pinned, "已置顶");
            ToolTipService.SetToolTip(pinned, "已置顶");
            meta.Children.Add(pinned);
        }
        body.Children.Add(meta);
        if (item.Kind == ClipKind.Image && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            body.Children.Add(new TextBlock { Text = $"图片大小：{new FileInfo(item.ImagePath).Length / 1024.0:0.#} KB", FontSize = 11, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        }
        if (!string.IsNullOrWhiteSpace(item.Note)) body.Children.Add(new TextBlock { Text = "备注：" + item.Note, FontSize = 11, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });

        var row = new Grid { Padding = new Thickness(10, 9, 10, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconSymbol = item.FileUrls.Count > 0
            ? Symbol.OpenFile
            : item.Kind switch
            {
                ClipKind.Image => Symbol.Pictures,
                ClipKind.Url => Symbol.Link,
                _ => Symbol.Document
            };
        var icon = new SymbolIcon
        {
            Symbol = iconSymbol,
            Width = 20,
            Height = 20,
            Foreground = (Brush)Application.Current.Resources["MintBrush"],
            VerticalAlignment = VerticalAlignment.Top
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icon, kindLabel + "记录");
        Grid.SetColumn(icon, 0); Grid.SetColumn(body, 1);
        row.Children.Add(icon); row.Children.Add(body);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var copy = new Button { Content = "复制", Padding = new Thickness(8, 4, 8, 4) };
        copy.Click += async (_, _) => await _root.CopyItemAsync(item);
        actions.Children.Add(copy);
        Grid.SetColumn(actions, 2); row.Children.Add(actions);
        var flyout = new MenuFlyout();
        if (item.Kind == ClipKind.Url)
            AddMenu(flyout, "用默认浏览器打开", async () => await OpenUrlAsync(item));
        AddMenu(flyout, item.Pinned ? "取消置顶" : "置顶", async () => { item.Pinned = !item.Pinned; await _root.SaveClipboardAsync(); Refresh(); });
        AddMenu(flyout, item.Favorite ? "取消收藏" : "收藏", async () => { item.Favorite = !item.Favorite; await _root.SaveClipboardAsync(); Refresh(); });
        AddMenu(flyout, "搜索", async () =>
        {
            var query = Uri.EscapeDataString(item.Text);
            await Launcher.LaunchUriAsync(new Uri("https://www.baidu.com/s?wd=" + query));
        });
        AddMenu(flyout, "添加标签", async () => await EditNoteAsync(item));
        AddMenu(flyout, "复制", async () => await _root.CopyItemAsync(item));
        AddMenu(flyout, "删除", async () => { await _root.DeleteClipboardItemAsync(item); Refresh(); });
        row.ContextFlyout = flyout;
        var border = new Border { Child = row, BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 0, 0, 0)), BorderThickness = new Thickness(0, 0, 0, 1) };
        if (item.Kind == ClipKind.Image && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            var image = new Image { Width = 34 * _imageScale, Height = 38 * _imageScale, Stretch = Stretch.UniformToFill, Source = new BitmapImage(new Uri(item.ImagePath)) };
            Grid.SetColumn(image, 0); row.Children.Remove(icon); row.Children.Add(image);
        }
        if (item.Kind != ClipKind.Image)
        {
            row.Tapped += async (_, e) =>
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
                await _root.CopyItemAsync(item);
            };
            if (item.Kind == ClipKind.Url)
            {
                row.DoubleTapped += async (_, e) =>
                {
                    if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
                    e.Handled = true;
                    await OpenUrlAsync(item);
                };
            }
        }
        else if (item.Kind == ClipKind.Image)
        {
            row.Tapped += async (_, e) =>
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
                e.Handled = true;
                await PreviewImageAsync(item);
            };
        }
        return border;
    }

    private static async Task OpenUrlAsync(ClipboardItem item)
    {
        if (ClipboardUrlPolicy.TryCreateWebUri(item.Text, out var uri) && uri is not null)
            await Launcher.LaunchUriAsync(uri);
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private async Task PreviewImageAsync(ClipboardItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath))
        {
            var missing = new ContentDialog
            {
                Title = "图片预览",
                Content = new TextBlock { Text = "图片无法读取", Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange) },
                PrimaryButtonText = "完成",
                XamlRoot = XamlRoot
            };
            await missing.ShowAsync();
            return;
        }
        var error = new TextBlock { Text = "", Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange), TextWrapping = TextWrapping.Wrap };
        var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        image.ImageFailed += (_, _) => error.Text = "图片无法读取";
        BitmapImage bitmap;
        try
        {
            bitmap = new BitmapImage(new Uri(item.ImagePath));
            image.Source = bitmap;
        }
        catch { error.Text = "图片无法读取"; }

        var percentage = new TextBlock { Text = "100%", Width = 52, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0.5, Maximum = 4, StepFrequency = 0.1, Value = 1, Width = 220 };
        var viewer = new ScrollViewer
        {
            MaxHeight = 500,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        double fittedWidth = 0;
        double fittedHeight = 0;
        void ApplyZoom()
        {
            if (fittedWidth <= 0 || fittedHeight <= 0) return;
            image.Width = fittedWidth * slider.Value;
            image.Height = fittedHeight * slider.Value;
        }
        void FitImage()
        {
            if (image.Source is not BitmapImage source) return;
            var viewportWidth = viewer.ActualWidth;
            var viewportHeight = viewer.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0) return;
            var fitted = ImagePreviewScalePolicy.FitToViewport(
                source.PixelWidth, source.PixelHeight, viewportWidth, viewportHeight);
            if (fitted.Width <= 0 || fitted.Height <= 0) return;
            fittedWidth = fitted.Width;
            fittedHeight = fitted.Height;
            ApplyZoom();
        }
        viewer.SizeChanged += (_, _) => FitImage();
        image.ImageOpened += (_, _) => FitImage();
        slider.ValueChanged += (_, _) =>
        {
            ApplyZoom();
            percentage.Text = $"{slider.Value:P0}";
        };
        var reset = new Button { Content = "重置" };
        reset.Click += (_, _) => slider.Value = 1;
        var controls = new Grid { ColumnSpacing = 8 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var imageSizeLabel = new TextBlock { Text = "图片大小", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(imageSizeLabel, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percentage, 2);
        Grid.SetColumn(reset, 3);
        controls.Children.Add(imageSizeLabel);
        controls.Children.Add(slider);
        controls.Children.Add(percentage);
        controls.Children.Add(reset);
        viewer.Content = image;
        var content = new StackPanel { Spacing = 10, Children = { controls, error, viewer } };
        var dialog = new ContentDialog { Title = "图片预览", Content = content, PrimaryButtonText = "完成", XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async Task EditNoteAsync(ClipboardItem item)
    {
        var input = new TextBox { Text = item.Note, PlaceholderText = "备注（可选）" };
        var dialog = new ContentDialog { Title = "添加标签或备注", Content = input, PrimaryButtonText = "保存", CloseButtonText = "取消", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            item.Note = input.Text;
            await _root.SaveClipboardAsync();
            Refresh();
        }
    }

    private static void AddMenu(MenuFlyout flyout, string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) => await action();
        flyout.Items.Add(item);
    }

    private static string RelativeTime(DateTimeOffset value)
        => RelativeTimePolicy.Format(value, DateTimeOffset.Now);
}

public sealed class QuestionAnswersPage : Page
{
    private readonly RootPage _root;
    private readonly TextBox _question = new() { PlaceholderText = "搜索或识别问题" };
    private readonly TextBox _answer = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
    private readonly TextBlock _matched = new() { Text = "还没有匹配的问题", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange) };
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBox _listSearch = new() { PlaceholderText = "搜索问题、答案、别名和关键词" };
    private Button? _copyAnswer;
    private int _clipboardReadVersion;
    private bool _hasManualQuestionEdit;
    private bool _applyingClipboardText;

    public QuestionAnswersPage(RootPage root)
    {
        _root = root;
        _question.TextChanged += (_, _) =>
        {
            if (!_applyingClipboardText) _hasManualQuestionEdit = true;
            UpdateMatch();
        };
        Build();
        Refresh();
    }

    private void Build()
    {
        var panel = new Grid { RowSpacing = 10, Padding = new Thickness(22) };
        for (var i = 0; i < 8; i++) panel.RowDefinitions.Add(new RowDefinition { Height = i == 7 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var title = new TextBlock { Text = "问答", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        Grid.SetRow(title, 0); panel.Children.Add(title);
        var description = new TextBlock { Text = "复制问题后会自动识别，答案会直接显示在这里", Foreground = (Brush)Application.Current.Resources["MutedBrush"] };
        Grid.SetRow(description, 1); panel.Children.Add(description);
        var read = new Button { Content = "读取剪贴板" }; read.Click += (_, _) => ReadClipboard();
        var questionRow = new Grid { ColumnSpacing = 8 };
        questionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        questionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _question.HorizontalAlignment = HorizontalAlignment.Stretch;
        questionRow.Children.Add(_question); Grid.SetColumn(read, 1); questionRow.Children.Add(read);
        Grid.SetRow(questionRow, 2); panel.Children.Add(questionRow);
        var answerBox = new StackPanel { Spacing = 8, Padding = new Thickness(14), Background = (Brush)Application.Current.Resources["MintSoftBrush"] };
        answerBox.Children.Add(_matched); answerBox.Children.Add(_answer);
        _copyAnswer = new Button { Content = "复制答案", HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = false }; _copyAnswer.Click += async (_, _) => await CopyAnswerAsync();
        answerBox.Children.Add(_copyAnswer); Grid.SetRow(answerBox, 3); panel.Children.Add(answerBox);
        var tools = new VariableSizedWrapGrid
        {
            ItemWidth = 96,
            ItemHeight = 40,
            MaximumRowsOrColumns = 4,
            Orientation = Orientation.Horizontal
        };
        var newButton = new Button { Content = "新建" }; newButton.Click += async (_, _) => await EditAsync(null);
        var template = new Button { Content = "下载模板" }; template.Click += async (_, _) => await ExportAsync(true);
        var import = new Button { Content = "导入" }; import.Click += async (_, _) => await ImportAsync();
        var export = new Button { Content = "导出" }; export.Click += async (_, _) => await ExportAsync(false);
        tools.Children.Add(newButton); tools.Children.Add(template); tools.Children.Add(import); tools.Children.Add(export); Grid.SetRow(tools, 4); panel.Children.Add(tools);
        Grid.SetRow(_status, 5); panel.Children.Add(_status);
        _listSearch.TextChanged += (_, _) => Refresh();
        Grid.SetRow(_listSearch, 6); panel.Children.Add(_listSearch);
        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        Grid.SetRow(listScroll, 7); panel.Children.Add(listScroll);
        Content = panel;
    }

    public void ReadClipboard()
    {
        _hasManualQuestionEdit = false;
        BeginClipboardRead();
    }

    private void BeginClipboardRead()
    {
        var version = Interlocked.Increment(ref _clipboardReadVersion);
        try
        {
            var view = Clipboard.GetContent();
            _ = ReadClipboardAsync(view, version);
        }
        catch
        {
            if (version == Volatile.Read(ref _clipboardReadVersion))
                _status.Text = "读取剪贴板失败，请稍后重试。";
        }
    }

    public void ReadClipboardIfVisible()
    {
        if (!_root.IsCurrentPage(this) || !QaClipboardReadPolicy.CanReplaceQuestion(_hasManualQuestionEdit)) return;
        BeginClipboardRead();
    }

    private async Task ReadClipboardAsync(DataPackageView view, int version)
    {
        try
        {
            if (!view.Contains(StandardDataFormats.Text)) return;
            var text = await view.GetTextAsync();
            if (version == Volatile.Read(ref _clipboardReadVersion)
                && QaClipboardReadPolicy.CanReplaceQuestion(_hasManualQuestionEdit))
            {
                _applyingClipboardText = true;
                try { _question.Text = text; }
                finally { _applyingClipboardText = false; }
            }
        }
        catch
        {
            if (version == Volatile.Read(ref _clipboardReadVersion))
                _status.Text = "读取剪贴板失败，请稍后重试。";
        }
    }

    private void UpdateMatch()
    {
        var match = QaMatcher.Find(_question.Text, _root.QaEntries);
        if (match is null)
        {
            _matched.Text = "还没有匹配的问题";
            _answer.Text = "";
            if (_copyAnswer is not null) _copyAnswer.IsEnabled = false;
            return;
        }
        var prefix = match.Kind == QaMatchKind.Candidate ? "候选问题（请确认）：" : "问题：";
        _matched.Text = prefix + match.Entry.Question;
        _answer.Text = match.Entry.Answer;
        if (_copyAnswer is not null)
            _copyAnswer.IsEnabled = match.IsSafeForAutomaticCopy && !string.IsNullOrWhiteSpace(_answer.Text);
    }

    public void ReportAssistantResult(string message) => _status.Text = message;

    private Task CopyAnswerAsync()
    {
        if (string.IsNullOrWhiteSpace(_answer.Text)) return Task.CompletedTask;
        _status.Text = _root.TrySetClipboardText(_answer.Text)
            ? "答案已复制。"
            : "复制答案失败，系统剪贴板可能正被其他程序占用。";
        return Task.CompletedTask;
    }

    public void Refresh()
    {
        _list.Children.Clear();
        var entries = _root.QaEntries
            .Where(entry => QaEntrySearchPolicy.Matches(entry, _listSearch.Text))
            .ToList();
        if (entries.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_listSearch.Text) ? "还没有问答，点击“新建”添加。" : "没有找到匹配的问答。",
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0)
            });
            UpdateMatch();
            return;
        }
        foreach (var entry in entries)
        {
            var row = new Grid { Padding = new Thickness(12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = string.IsNullOrWhiteSpace(entry.Question) ? "未填写问题" : entry.Question, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, new TextBlock { Text = entry.Answer, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis } } };
            row.Children.Add(texts);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            var edit = new Button { Content = "编辑" }; edit.Click += async (_, _) => await EditAsync(entry);
            var delete = new Button { Content = "删除" }; delete.Click += async (_, _) => await _root.RemoveQaAsync(entry);
            buttons.Children.Add(edit); buttons.Children.Add(delete); Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
            _list.Children.Add(new Border { Child = row, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), CornerRadius = new CornerRadius(8) });
        }
        UpdateMatch();
    }

    private async Task EditAsync(QaEntry? existing)
    {
        var entry = existing is null ? new QaEntry() : new QaEntry { Id = existing.Id, Question = existing.Question ?? "", Answer = existing.Answer ?? "", Aliases = existing.Aliases ?? "", Keywords = existing.Keywords ?? "", Category = existing.Category ?? "", Note = existing.Note ?? "", Enabled = existing.Enabled };
        var question = new TextBox { Header = "问题", Text = entry.Question }; var answer = new TextBox { Header = "答案", Text = entry.Answer, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 }; var aliases = new TextBox { Header = "别名（用 | 分隔）", Text = entry.Aliases }; var keywords = new TextBox { Header = "关键词（用 | 分隔）", Text = entry.Keywords }; var category = new TextBox { Header = "分类", Text = entry.Category }; var note = new TextBox { Header = "备注", Text = entry.Note }; var enabled = new CheckBox { Content = "启用", IsChecked = entry.Enabled };
        var content = new StackPanel { Spacing = 7, Children = { question, answer, aliases, keywords, category, note, enabled } };
        var editorScroll = new ScrollViewer
        {
            Content = content,
            MaxHeight = 430,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var dialog = new ContentDialog { Title = existing is null ? "新建问答" : "编辑问答", Content = editorScroll, PrimaryButtonText = "保存", CloseButtonText = "取消", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            entry.Question = question.Text; entry.Answer = answer.Text; entry.Aliases = aliases.Text; entry.Keywords = keywords.Text; entry.Category = category.Text; entry.Note = note.Text; entry.Enabled = enabled.IsChecked == true;
            await _root.AddOrUpdateQaAsync(entry);
        }
    }

    private async Task ExportAsync(bool template)
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, _root.Window.Hwnd);
            picker.SuggestedFileName = template ? "问答导入模板" : "快剪问答";
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
            if (!template) picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            if (!template && file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(_root.QaEntries, options));
                _status.Text = $"已导出 {_root.QaEntries.Count} 条问答 JSON。";
                return;
            }
            await FileIO.WriteTextAsync(file, QaCsvCodec.Serialize(template ? Array.Empty<QaEntry>() : _root.QaEntries));
            _status.Text = template ? "CSV 模板已保存。" : $"已导出 {_root.QaEntries.Count} 条问答。";
        }
        catch (Exception ex)
        {
            _status.Text = "导出失败：" + ex.Message;
        }
    }

    private async Task ImportAsync()
    {
        try
        {
            var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, _root.Window.Hwnd); picker.FileTypeFilter.Add(".csv"); picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync(); if (file is null) return;
            var imported = new List<QaEntry>();
            if (file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await FileIO.ReadTextAsync(file);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<List<QaEntry>>(json, options)
                    ?? throw new InvalidDataException("JSON 文件必须包含问答数组。");
                imported.AddRange(parsed.OfType<QaEntry>());
            }
            else
            {
                var text = await FileIO.ReadTextAsync(file);
                foreach (var fields in QaCsvCodec.ParseRows(text).Skip(1).Where(x => x.Length >= 2 && x.Any(value => !string.IsNullOrWhiteSpace(value))))
                {
                    imported.Add(new QaEntry
                    {
                        Question = fields[0],
                        Answer = fields[1],
                        Aliases = fields.ElementAtOrDefault(2) ?? "",
                        Keywords = fields.ElementAtOrDefault(3) ?? "",
                        Category = fields.ElementAtOrDefault(4) ?? "",
                        Note = fields.ElementAtOrDefault(5) ?? "",
                        Enabled = !string.Equals(fields.ElementAtOrDefault(6), "否", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            var importedCount = await _root.MergeQaEntriesAsync(imported);
            Refresh();
            _status.Text = importedCount == imported.Count
                ? $"已导入 {importedCount} 条问答。"
                : $"已导入 {importedCount} 条问答，跳过 {imported.Count - importedCount} 条空记录。";
        }
        catch (Exception ex)
        {
            _status.Text = "导入失败：" + ex.Message;
        }
    }

}

public sealed class CalculatorPage : Page
{
    private readonly RootPage _root;
    private readonly TextBlock _display = new() { Text = "0", FontSize = 34, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _expression = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel _history = new() { Spacing = 5 };
    private string _inputExpression = "";
    private bool _newEntry = true;
    private bool _isResult;

    public CalculatorPage(RootPage root)
    {
        _root = root;
        IsTabStop = true;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => Focus(FocusState.Programmatic);
        Build();
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var shiftPressed = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        var token = CalculatorKeyboardPolicy.TokenFor((int)e.Key, shiftPressed);
        if (token is null) return;
        Input(token);
        e.Handled = true;
    }

    private void Build()
    {
        var panel = new StackPanel { Spacing = 9, Padding = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "计算机", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var screen = new StackPanel { Spacing = 2, Padding = new Thickness(12), Background = new SolidColorBrush(Microsoft.UI.Colors.White) }; screen.Children.Add(_expression); screen.Children.Add(_display); panel.Children.Add(screen);
        var grid = new Grid();
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var _ in CalculatorButtonLayoutPolicy.ButtonRows) grid.RowDefinitions.Add(new RowDefinition());
        for (var row = 0; row < CalculatorButtonLayoutPolicy.ButtonRows.Count; row++)
        for (var column = 0; column < CalculatorButtonLayoutPolicy.ButtonRows[row].Count; column++)
        {
            var token = CalculatorButtonLayoutPolicy.ButtonRows[row][column];
            var button = new Button { Content = token, FontSize = 18, Margin = new Thickness(3) };
            button.Click += (_, _) => Input(token);
            Grid.SetColumn(button, column);
            Grid.SetRow(button, row);
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);
        var historyHeader = new Grid(); historyHeader.Children.Add(new TextBlock { Text = "计算机历史记录", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }); var clear = new Button { Content = "清空", HorizontalAlignment = HorizontalAlignment.Right }; clear.Click += async (_, _) => { await _root.ClearCalculatorHistoryAsync(); RefreshHistory(); }; historyHeader.Children.Add(clear); panel.Children.Add(historyHeader);
        panel.Children.Add(new ScrollViewer { Height = 180, Content = _history });
        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        RefreshHistory();
    }

    private void Input(string token)
    {
        switch (token)
        {
            case "AC": _inputExpression = ""; _display.Text = "0"; _expression.Text = ""; _newEntry = true; _isResult = false; break;
            case "⌫": Backspace(); break;
            case "%":
                if (EndsWithValue(_inputExpression)) { _inputExpression += "%"; _newEntry = true; _isResult = false; UpdateDisplay(); }
                break;
            case "±": ToggleSign(); break;
            case "+": case "−": case "×": case "÷": InputOperator(token); break;
            case "(":
                if (_isResult) { _inputExpression = ""; _isResult = false; }
                if (EndsWithValue(_inputExpression)) _inputExpression += "×";
                if (_inputExpression.Length == 0 || IsOperator(_inputExpression[^1]) || _inputExpression[^1] == '(')
                { _inputExpression += "("; _newEntry = true; UpdateDisplay(); }
                break;
            case ")":
                if (CanCloseParenthesis()) { _inputExpression += ")"; _newEntry = true; UpdateDisplay(); }
                break;
            case "=": Calculate(); break;
            case ".": InputDigit("."); break;
            default: InputDigit(token); break;
        }
    }

    private async void Calculate()
    {
        if (string.IsNullOrWhiteSpace(_inputExpression)) return;
        try
        {
            var original = _inputExpression;
            var result = CalculatorEngine.Evaluate(original);
            var formatted = Format(result);
            var historyItem = $"{original} = {formatted}";
            _expression.Text = $"{original} ="; _display.Text = formatted; _inputExpression = formatted;
            _newEntry = true; _isResult = true;
            await _root.AddCalculatorHistoryAsync(historyItem);
            RefreshHistory();
        }
        catch (Exception ex) when (ex is FormatException or ArithmeticException)
        {
            _expression.Text = ex.Message;
        }
    }

    private static string Format(double value) => value % 1 == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("G10", CultureInfo.InvariantCulture);
    private static bool IsOperator(char value) => value is '+' or '-' or '−' or '*' or '×' or '/' or '÷';
    private static bool EndsWithValue(string value) => value.Length > 0 && (char.IsAsciiDigit(value[^1]) || value[^1] is ')' or '%');

    private void InputOperator(string token)
    {
        if (_isResult) _isResult = false;
        if (_inputExpression.Length == 0)
        {
            if (token == "−") _inputExpression = "−";
            else return;
        }
        else if (IsOperator(_inputExpression[^1]))
        {
            if (_inputExpression.Length == 1 && _inputExpression[0] == '−' && token != "−") return;
            _inputExpression = _inputExpression[..^1] + token;
        }
        else if (EndsWithValue(_inputExpression)) _inputExpression += token;
        else return;
        _newEntry = true; _expression.Text = _inputExpression; UpdateDisplay();
    }

    private void InputDigit(string token)
    {
        if (_isResult) { _inputExpression = ""; _isResult = false; _expression.Text = ""; }
        if (_newEntry)
        {
            if (EndsWithValue(_inputExpression)) _inputExpression += "×";
            if (token == ".") _inputExpression += "0.";
            else _inputExpression += token;
            _newEntry = false;
        }
        else
        {
            if (token == ".")
            {
                var operand = GetTrailingNumber(_inputExpression);
                if (operand.Contains('.')) return;
                _inputExpression += ".";
            }
            else
            {
                var operand = GetTrailingNumber(_inputExpression);
                if (operand.Length >= 24) return;
                _inputExpression += token;
            }
        }
        _expression.Text = _inputExpression; UpdateDisplay();
    }

    private void ToggleSign()
    {
        if (_isResult) { _inputExpression = _display.Text; _isResult = false; }
        var end = _inputExpression.Length;
        var start = end;
        while (start > 0 && (char.IsAsciiDigit(_inputExpression[start - 1]) || _inputExpression[start - 1] == '.')) start--;
        if (start == end)
        {
            if (_inputExpression.Length == 0) _inputExpression = "−";
            else if (_inputExpression[^1] == ')') _inputExpression += "×(−1)";
            else if (IsOperator(_inputExpression[^1]) || _inputExpression[^1] == '(') _inputExpression += "−";
            else return;
        }
        else if (start > 0 && _inputExpression[start - 1] is '-' or '−' &&
                 (start == 1 || IsOperator(_inputExpression[start - 2]) || _inputExpression[start - 2] == '('))
            _inputExpression = _inputExpression[..(start - 1)] + _inputExpression[start..];
        else _inputExpression = _inputExpression[..start] + "−" + _inputExpression[start..];
        _newEntry = false; _expression.Text = _inputExpression; UpdateDisplay();
    }

    private void Backspace()
    {
        if (_isResult) _isResult = false;
        if (_inputExpression.Length == 0) return;
        _inputExpression = _inputExpression[..^1];
        _newEntry = _inputExpression.Length == 0 || IsOperator(_inputExpression[^1]) || _inputExpression[^1] == '(';
        _expression.Text = _inputExpression; UpdateDisplay();
    }

    private bool CanCloseParenthesis()
    {
        if (!EndsWithValue(_inputExpression)) return false;
        var open = _inputExpression.Count(ch => ch == '(');
        var close = _inputExpression.Count(ch => ch == ')');
        return open > close;
    }

    private static string GetTrailingNumber(string value)
    {
        var start = value.Length;
        while (start > 0 && (char.IsAsciiDigit(value[start - 1]) || value[start - 1] == '.')) start--;
        return value[start..];
    }

    private void UpdateDisplay()
    {
        var number = GetTrailingNumber(_inputExpression);
        if (number.Length > 0 && double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            var start = _inputExpression.Length - number.Length;
            if (start > 0 && _inputExpression[start - 1] is '-' or '−' &&
                (start == 1 || IsOperator(_inputExpression[start - 2]) || _inputExpression[start - 2] == '(')) parsed = -parsed;
            _display.Text = Format(parsed);
            return;
        }
        if (EndsWithValue(_inputExpression))
        {
            try { _display.Text = Format(CalculatorEngine.Evaluate(_inputExpression)); return; }
            catch (Exception ex) when (ex is FormatException or ArithmeticException) { }
        }
        if (_inputExpression.Length == 0) _display.Text = "0";
    }

    private void RefreshHistory()
    {
        _history.Children.Clear();
        foreach (var value in _root.CalculatorHistory)
        {
            var item = new Button { Content = value, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            item.Click += (_, _) =>
            {
                var separator = value.LastIndexOf('=');
                if (separator < 0 || separator == value.Length - 1) return;
                _expression.Text = _root.TrySetClipboardText(value[(separator + 1)..].Trim())
                    ? "已复制历史结果"
                    : "复制历史结果失败，系统剪贴板可能正被其他程序占用。";
            };
            _history.Children.Add(item);
        }
    }
}
