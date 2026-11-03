using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace KuaiJian.WinUI;

public sealed class DownloadsPage : Page
{
    private readonly RootPage _root;
    private readonly string _userId;
    private readonly TextBox _url = new() { PlaceholderText = "粘贴抖音、小红书、B站等链接" };
    private Button? _start;
    private readonly StackPanel _tasksPanel = new() { Spacing = 9 };
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange), TextWrapping = TextWrapping.Wrap };
    private readonly List<DownloadTaskItem> _tasks;
    private readonly Dictionary<string, CancellationTokenSource> _cancellations = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly YtDlpService _ytDlp = new();

    public DownloadsPage(RootPage root)
    {
        _root = root;
        _userId = root.Data.UserId ?? throw new InvalidOperationException("下载页必须在账号数据加载后创建。");
        _tasks = root.DownloadTasks;
        Build();
    }

    public void CancelAll()
    {
        foreach (var cancellation in _cancellations.Values.ToArray())
            cancellation.Cancel();
    }

    public void RefreshFromSharedTasks() => RenderTasks();

    private void Build()
    {
        var panel = new Grid { RowSpacing = 16, Padding = new Thickness(22) };
        for (var i = 0; i < 4; i++) panel.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var heading = new Grid(); heading.Children.Add(new StackPanel { Children = { new TextBlock { Text = "下载", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold }, new TextBlock { Text = "粘贴国内热门网站的视频或图片链接，下载到电脑。", Foreground = (Brush)Application.Current.Resources["MutedBrush"] } } }); heading.Children.Add(new Border { Child = new TextBlock { Text = "支持多站点", Foreground = (Brush)Application.Current.Resources["MintBrush"], Padding = new Thickness(10, 6, 10, 6) }, Background = (Brush)Application.Current.Resources["MintSoftBrush"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(14) }); Grid.SetRow(heading, 0); panel.Children.Add(heading);
        _start = new Button { Content = "开始下载", IsEnabled = false };
        _start.Click += async (_, _) => await StartAsync();
        _url.TextChanged += (_, _) => UpdateStartButton();
        _url.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter && _start.IsEnabled) await StartAsync(); };
        var input = new Grid { ColumnSpacing = 8 };
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _url.HorizontalAlignment = HorizontalAlignment.Stretch;
        input.Children.Add(_url); Grid.SetColumn(_start, 1); input.Children.Add(_start);
        Grid.SetRow(input, 1); panel.Children.Add(input);
        Grid.SetRow(_error, 2); panel.Children.Add(_error);
        var tasksScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tasksPanel };
        Grid.SetRow(tasksScroll, 3); panel.Children.Add(tasksScroll);
        Content = panel;
        RenderTasks();
    }

    private async Task StartAsync(DownloadTaskItem? existingTask = null)
    {
        var value = existingTask?.SourceUrl ?? _url.Text.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _error.Text = "请输入有效的 http 或 https 链接。";
            return;
        }
        _error.Text = "";
        if (existingTask is null) _url.Text = "";
        var task = existingTask ?? new DownloadTaskItem { SourceUrl = value, Title = uri.Host, State = DownloadState.Queued };
        if (!DownloadTaskOperationPolicy.CanStart(task.Id, _cancellations.Keys))
        {
            _error.Text = "该下载任务已在处理中。";
            return;
        }
        if (existingTask is null) _tasks.Insert(0, task);
        task.State = DownloadState.Queued;
        task.Message = existingTask is null ? "排队中" : "正在继续下载…";
        _root.NotifyDownloadTasksChanged();
        var cancellation = new CancellationTokenSource(); _cancellations[task.Id] = cancellation;
        try
        {
            await _root.SaveDownloadTasksAsync(_userId, _tasks);
            cancellation.Token.ThrowIfCancellationRequested();
            task.State = DownloadState.Queued; task.Message = "排队中"; _root.NotifyDownloadTasksChanged();
            using var downloadSlot = await _root.DownloadGate.AcquireAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            task.State = DownloadState.Running; task.Message = "正在连接…"; _root.NotifyDownloadTasksChanged();
            Directory.CreateDirectory(_root.DownloadDirectory!);
            if (NeedsYtDlp(uri))
            {
                if (!_ytDlp.IsAvailable)
                    throw new InvalidOperationException("该网站需要 yt-dlp.exe。请将官方 Windows 版放入程序目录的 runtime-tools 文件夹后重试。");
                var stagingDirectory = GetYtDlpStagingDirectory(task.Id);
                var path = await _ytDlp.DownloadAsync(uri, _root.DownloadDirectory!, stagingDirectory, _root.DownloadBrowser, cancellation.Token,
                    (progress, message) => DispatcherQueue.TryEnqueue(() => { task.Progress = progress; task.Message = message; _root.NotifyDownloadTasksChanged(); }),
                    message => DispatcherQueue.TryEnqueue(() => { task.Message = message; _root.NotifyDownloadTasksChanged(); }));
                if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("下载完成，但未找到输出文件。");
                task.State = DownloadState.Finished;
                task.Progress = 1;
                task.OutputPath = path;
                task.Message = "已保存到下载目录";
                ApplyDownloadCompletionBehavior(task);
            }
            else
            {
                var partialPath = GetPartialPath(task.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
                DownloadResumeMetadata? checkpoint = null;
                if (CanResume(task))
                {
                    checkpoint = new DownloadResumeMetadata(task.ResumeFileName!, task.ResumeETag,
                        task.ResumeLastModified, task.ResumeTotalBytes);
                }
                var result = await ResumableHttpDownload.DownloadAsync(
                    _http, uri, _root.DownloadDirectory!, partialPath, checkpoint,
                    async (metadata, bytes) =>
                    {
                        task.ResumeFileName = metadata.FileName;
                        task.ResumeETag = metadata.ETag;
                        task.ResumeLastModified = metadata.LastModified;
                        task.ResumeTotalBytes = metadata.TotalBytes;
                        task.ResumeBytes = bytes;
                        await _root.SaveDownloadTasksAsync(_userId, _tasks);
                    },
                    (bytes, total) =>
                    {
                        task.ResumeBytes = bytes;
                        task.Progress = total is > 0 ? Math.Clamp((double)bytes / total.Value, 0, 1) : 0;
                        task.Message = total is > 0 ? $"下载中 {task.Progress:P0}" : $"已接收 {bytes:N0} 字节";
                        _root.NotifyDownloadTasksChanged();
                    },
                    cancellation.Token);
                task.State = DownloadState.Finished; task.Progress = 1; task.OutputPath = result.OutputPath; task.Message = "已保存到下载目录";
                ClearResumeMetadata(task);
                ApplyDownloadCompletionBehavior(task);
            }
        }
        catch (OperationCanceledException)
        {
            PreserveOrDeletePartial(task);
            task.State = DownloadState.Cancelled;
            task.Message = CanResume(task) ? "已暂停，可继续下载" : "已取消";
            _root.NotifyDownloadTasksChanged();
        }
        catch (Exception ex)
        {
            PreserveOrDeletePartial(task);
            task.State = DownloadState.Failed;
            task.Message = CanResume(task) ? "下载中断，可继续：" + ex.Message : "下载失败：" + ex.Message;
            _root.NotifyDownloadTasksChanged();
        }
        finally
        {
            _cancellations.Remove(task.Id);
            cancellation.Dispose();
            await _root.SaveDownloadTasksAsync(_userId, _tasks);
            _root.NotifyDownloadTasksChanged();
        }
    }

    private void UpdateStartButton()
    {
        if (_start is not null) _start.IsEnabled = !string.IsNullOrWhiteSpace(_url.Text);
    }

    private void ApplyDownloadCompletionBehavior(DownloadTaskItem task)
    {
        if (!DownloadCompletionPolicy.ShouldRevealInFolder(
                _root.Setting("downloadCompletionBehavior", DownloadCompletionPolicy.None))
            || string.IsNullOrWhiteSpace(task.OutputPath)) return;

        try
        {
            var outputPath = Path.GetFullPath(task.OutputPath);
            if (File.Exists(outputPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"/select,\"{outputPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _error.Text = "下载已完成，但无法在文件夹中显示该文件：" + ex.Message;
        }
    }

    private void RenderTasks()
    {
        _tasksPanel.Children.Clear();
        if (_tasks.Count == 0)
        {
            _tasksPanel.Children.Add(new TextBlock { Text = "还没有下载任务\n复制一个公开链接，粘贴到上方即可开始。", TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["MutedBrush"], Margin = new Thickness(0, 60, 0, 0) });
            return;
        }
        foreach (var task in _tasks)
        {
            var row = new Grid { Padding = new Thickness(12) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 4 };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var stateIcon = new SymbolIcon(StateSymbol(task.State))
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = task.State == DownloadState.Failed
                    ? new SolidColorBrush(Microsoft.UI.Colors.DarkOrange)
                    : (Brush)Application.Current.Resources["MutedBrush"]
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(stateIcon, StateLabel(task.State));
            titleRow.Children.Add(stateIcon);
            titleRow.Children.Add(new TextBlock { Text = task.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            text.Children.Add(titleRow);
            text.Children.Add(new TextBlock { Text = task.SourceUrl, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
            text.Children.Add(new TextBlock { Text = task.State == DownloadState.Running ? $"下载中 {task.Progress:P0}" : task.Message, FontSize = 11, Foreground = task.State == DownloadState.Failed ? new SolidColorBrush(Microsoft.UI.Colors.DarkOrange) : (Brush)Application.Current.Resources["MutedBrush"] });
            if (task.State == DownloadState.Running) text.Children.Add(new ProgressBar { Value = task.Progress * 100, Maximum = 100, Height = 5 });
            row.Children.Add(text);
            var canResume = CanResume(task);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            if (task.State is DownloadState.Queued or DownloadState.Running)
            {
                var cancel = new Button { Content = "取消" };
                cancel.Click += async (_, _) => await HandleTaskAsync(task);
                actions.Children.Add(cancel);
            }
            else if (task.State == DownloadState.Finished)
            {
                var open = new Button { Content = "在文件夹中显示" };
                open.Click += async (_, _) => await HandleTaskAsync(task);
                actions.Children.Add(open);
            }
            else if (canResume)
            {
                var resume = new Button { Content = "继续" };
                resume.Click += async (_, _) => await StartAsync(task);
                var remove = new Button { Content = "移除" };
                remove.Click += async (_, _) => await RemoveTaskAsync(task);
                actions.Children.Add(resume);
                actions.Children.Add(remove);
            }
            else
            {
                var remove = new Button { Content = "移除" };
                remove.Click += async (_, _) => await HandleTaskAsync(task);
                actions.Children.Add(remove);
            }
            Grid.SetColumn(actions, 1); row.Children.Add(actions);
            _tasksPanel.Children.Add(new Border { Child = row, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), CornerRadius = new CornerRadius(10) });
        }
    }

    private async Task HandleTaskAsync(DownloadTaskItem task)
    {
        if (task.State is DownloadState.Queued or DownloadState.Running) { if (_cancellations.TryGetValue(task.Id, out var token)) token.Cancel(); return; }
        if (CanResume(task)) { await StartAsync(task); return; }
        if (task.State == DownloadState.Finished && !string.IsNullOrWhiteSpace(task.OutputPath))
        {
            var outputPath = Path.GetFullPath(task.OutputPath);
            if (File.Exists(outputPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"/select,\"{outputPath}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(Path.GetDirectoryName(outputPath)))
            {
                await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(outputPath)!);
            }
        }
        else await RemoveTaskAsync(task);
    }

    private async Task RemoveTaskAsync(DownloadTaskItem task)
    {
        if (task.State is DownloadState.Queued or DownloadState.Running) return;
        TryDelete(GetPartialPath(task.Id));
        TryDeleteYtDlpStaging(task.Id);
        _tasks.Remove(task);
        await _root.SaveDownloadTasksAsync(_userId, _tasks);
        _root.NotifyDownloadTasksChanged();
    }

    private static string Sanitize(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }

    private static void TryDelete(string? path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    private bool NeedsYtDlp(Uri uri)
    {
        if (_root.DownloadBrowser != "none") return true;
        return YtDlpService.IsSupportedSite(uri);
    }

    private string GetPartialPath(string taskId) => AppPaths.DownloadPartialFile(_userId, taskId);

    private bool CanResume(DownloadTaskItem task)
    {
        if (!Uri.TryCreate(task.SourceUrl, UriKind.Absolute, out var uri)) return false;
        if (NeedsYtDlp(uri))
            return YtDlpResumePolicy.HasResumableArtifacts(GetYtDlpStagingDirectory(task.Id));
        if (!DownloadResumePolicy.HasStableValidator(new DownloadResumeMetadata(
                task.ResumeFileName ?? "", task.ResumeETag, task.ResumeLastModified, task.ResumeTotalBytes))) return false;
        try { return File.Exists(GetPartialPath(task.Id)) && new FileInfo(GetPartialPath(task.Id)).Length > 0; }
        catch { return false; }
    }

    private void PreserveOrDeletePartial(DownloadTaskItem task)
    {
        var partialPath = GetPartialPath(task.Id);
        try
        {
            var length = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            var metadata = new DownloadResumeMetadata(task.ResumeFileName ?? "", task.ResumeETag,
                task.ResumeLastModified, task.ResumeTotalBytes);
            if (length > 0 && DownloadResumePolicy.HasStableValidator(metadata))
            {
                task.ResumeBytes = length;
                return;
            }
        }
        catch { }
        TryDelete(partialPath);
        ClearResumeMetadata(task);
    }

    private static void ClearResumeMetadata(DownloadTaskItem task)
    {
        task.ResumeBytes = 0;
        task.ResumeTotalBytes = null;
        task.ResumeFileName = null;
        task.ResumeETag = null;
        task.ResumeLastModified = null;
    }

    private string GetYtDlpStagingDirectory(string taskId) => AppPaths.YtDlpStagingDirectory(_userId, taskId);

    private void TryDeleteYtDlpStaging(string taskId)
    {
        try
        {
            var directory = GetYtDlpStagingDirectory(taskId);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch { }
    }

    private static Symbol StateSymbol(DownloadState state) => state switch
    {
        DownloadState.Queued => Symbol.Clock,
        DownloadState.Running => Symbol.Download,
        DownloadState.Finished => Symbol.Accept,
        DownloadState.Failed => Symbol.Important,
        _ => Symbol.Cancel
    };

    private static string StateLabel(DownloadState state) => state switch
    {
        DownloadState.Queued => "排队中",
        DownloadState.Running => "下载中",
        DownloadState.Finished => "已完成",
        DownloadState.Failed => "下载失败",
        _ => "已取消"
    };
}

public sealed class SettingsPage : Page
{
    private readonly RootPage _root;
    private readonly UpdateService _updates = new();
    private readonly StackPanel _content = new() { Spacing = 9 };
    private readonly Dictionary<string, Button> _sectionButtons = new(StringComparer.Ordinal);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["MutedBrush"] };
    private Button? _downloadUpdate;
    private UpdateInfo? _latestUpdate;
    private string _section = "保存";

    public SettingsPage(RootPage root)
    {
        _root = root;
        Build();
        ShowSection("保存");
    }

    private void Build()
    {
        var shell = new Grid
        {
            MaxWidth = 650,
            Height = 480,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = new StackPanel { Padding = new Thickness(12), Spacing = 6, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 242, 249, 245)) };
        nav.Children.Add(new TextBlock { Text = "设置", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 10) });
        foreach (var section in SettingsSectionPolicy.NavigationSections)
        {
            var button = new Button { Content = section, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = section };
            button.Click += (_, _) => ShowSection(section);
            _sectionButtons[section] = button;
            nav.Children.Add(button);
        }
        Grid.SetColumn(nav, 0); shell.Children.Add(nav);
        var right = new StackPanel { Padding = new Thickness(22), Spacing = 8 };
        right.Children.Add(new ScrollViewer
        {
            Content = _content,
            MaxHeight = 390,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        right.Children.Add(_status);
        Grid.SetColumn(right, 1); shell.Children.Add(right); Content = shell;
    }

    private void ShowSection(string section)
    {
        _section = section; _content.Children.Clear(); _status.Text = "";
        foreach (var pair in _sectionButtons)
        {
            var selected = pair.Key == section;
            pair.Value.Background = selected ? (Brush)Application.Current.Resources["MintSoftBrush"] : null;
            pair.Value.Foreground = selected ? (Brush)Application.Current.Resources["MintBrush"] : (Brush)Application.Current.Resources["InkBrush"];
        }
        switch (section)
        {
            case "账号": Account(); break;
            case "保存": Save(); break;
            case "下载": Download(); break;
            case "快捷键": Shortcut(); break;
            case "隐藏": Hidden(); break;
            case "更新": Update(); break;
        }
    }

    private void Account()
    {
        _content.Children.Add(new TextBlock { Text = "账号", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _content.Children.Add(new TextBlock { Text = "已登录账号\n" + _root.Auth.CurrentUser?.Phone + (string.IsNullOrWhiteSpace(_root.Auth.CurrentUser?.Email) ? "" : "\n" + _root.Auth.CurrentUser!.Email), Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        _content.Children.Add(new TextBlock { Text = "剪贴板、问答和图片数据已按当前账号独立保存。退出后需要重新登录才能访问。", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        var logout = new Button { Content = "退出登录" }; logout.Click += async (_, _) =>
        {
            await _root.Auth.LogoutAsync();
            _root.CloseSettingsDialog();
            _root.LogoutToAuth();
        }; _content.Children.Add(logout);
    }

    private void Save()
    {
        _content.Children.Add(new TextBlock { Text = "保存", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        AddToggle("保存文本", "captureText", true); AddToggle("保存图片", "captureImages", true); AddToggle("保存网址", "captureURLs", true); AddToggle("保存富文本格式（HTML / RTF）", "captureRichText", true);
        var limitOptions = new[] { 50, 100, 200, 500 };
        var limitPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = limitOptions.Select(value => $"{value} 条").ToArray(),
            SelectedIndex = Array.IndexOf(limitOptions, _root.ClipboardRetentionLimit)
        };
        if (limitPicker.SelectedIndex < 0) limitPicker.SelectedIndex = 2;
        limitPicker.SelectionChanged += async (_, _) =>
        {
            if (limitPicker.SelectedIndex < 0) return;
            await _root.SetClipboardRetentionLimitAsync(limitOptions[limitPicker.SelectedIndex]);
            _status.Text = $"剪贴板历史上限已设为 {limitOptions[limitPicker.SelectedIndex]} 条。";
        };
        var limitRow = new Grid { ColumnSpacing = 8 };
        limitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        limitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limitRow.Children.Add(new TextBlock { Text = "保存数量上限", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(limitPicker, 1); limitRow.Children.Add(limitPicker); _content.Children.Add(limitRow);
        var userDirectory = _root.Auth.CurrentUser is { } user ? AppPaths.UserRoot(user.Id) : AppPaths.Root;
        var directoryText = new TextBlock
        {
            Text = userDirectory,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        };
        var openDirectory = new Button { Content = "打开数据目录" };
        openDirectory.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(userDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = userDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { _status.Text = "无法打开数据目录：" + ex.Message; }
        };
        _content.Children.Add(directoryText);
        _content.Children.Add(openDirectory);
        _content.Children.Add(new TextBlock
        {
            Text = "支持多选 JSON、CSV、TXT 和 GZip 导出文件。若 .dat 来自 CopyQ，请先在 CopyQ 中用 exportJSON() 导出为 JSON；其他剪贴板软件的私有二进制 .dat 需要格式说明或样本才能安全导入。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        });
        var import = new Button { Content = "导入旧版快剪历史（可多选）" }; import.Click += async (_, _) => await ImportClipboardHistoryAsync(); _content.Children.Add(import);
        AddQaAssistantSettings();
    }

    private void AddQaAssistantSettings()
    {
        _content.Children.Add(new TextBlock { Text = "问答助手", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        AddToggle("全局问答助手（复制问题后自动复制匹配答案）", "qaAssistantEnabled", false);
        _content.Children.Add(new TextBlock
        {
            Text = "仅完整问题、别名或全部关键词匹配时自动复制；模糊候选不会自动改写剪贴板。助手只复制答案，不会自动发送。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        });

        var sources = new TextBox
        {
            Header = "允许生效的来源应用（用 | 分隔）",
            PlaceholderText = "留空表示所有来源，例如：微信|Chrome",
            Text = _root.Setting("qaAssistantSources", "")
        };
        _content.Children.Add(sources);
        var saveSources = new Button { Content = "保存来源限制" };
        saveSources.Click += (_, _) =>
        {
            var normalized = string.Join("|", sources.Text
                .Split(new[] { '|', ';', '；', ',', '，', '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            _root.SetSetting("qaAssistantSources", normalized);
            sources.Text = normalized;
            _status.Text = normalized.Length == 0 ? "问答助手将对所有来源应用生效。" : "已保存问答助手的来源应用限制。";
        };
        _content.Children.Add(saveSources);
    }

    private void Download()
    {
        _content.Children.Add(new TextBlock { Text = "下载", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var path = new TextBlock { Text = _root.DownloadDirectory, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = (Brush)Application.Current.Resources["MutedBrush"] };
        var choose = new Button { Content = "选择" };
        choose.Click += async (_, _) =>
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, _root.Window.Hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                _root.DownloadDirectory = folder.Path;
                _root.SetSetting("downloadDirectory", folder.Path);
                path.Text = folder.Path;
            }
        };
        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.Children.Add(new TextBlock { Text = "默认下载地址", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(path, 1); pathRow.Children.Add(path);
        Grid.SetColumn(choose, 2); pathRow.Children.Add(choose);
        _content.Children.Add(pathRow);
        var browserOptions = new[] { "不使用登录状态", "Chrome", "Edge", "Firefox" };
        var browserValues = new[] { "none", "Chrome", "Edge", "Firefox" };
        var normalizedBrowser = RootPage.NormalizeDownloadBrowser(_root.DownloadBrowser);
        var browser = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = browserOptions, SelectedIndex = Array.IndexOf(browserValues, normalizedBrowser) };
        browser.SelectionChanged += (_, _) =>
        {
            if (browser.SelectedIndex < 0) return;
            var value = browserValues[browser.SelectedIndex];
            _root.DownloadBrowser = value;
            _root.SetSetting("downloadBrowser", value);
        };
        var browserRow = new Grid { ColumnSpacing = 8 };
        browserRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        browserRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        browserRow.Children.Add(new TextBlock { Text = "默认下载身份", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(browser, 1); browserRow.Children.Add(browser);
        _content.Children.Add(browserRow);
        var concurrencyOptions = Enumerable.Range(1, 5).Select(value => $"{value} 个任务").ToArray();
        var concurrency = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = concurrencyOptions,
            SelectedIndex = Math.Clamp(_root.DownloadConcurrency, 1, 5) - 1
        };
        concurrency.SelectionChanged += (_, _) =>
        {
            if (concurrency.SelectedIndex < 0) return;
            _root.DownloadConcurrency = concurrency.SelectedIndex + 1;
            _root.DownloadGate.SetLimit(_root.DownloadConcurrency);
            _root.SetSetting("downloadConcurrency", _root.DownloadConcurrency.ToString());
        };
        var concurrencyRow = new Grid { ColumnSpacing = 8 };
        concurrencyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        concurrencyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        concurrencyRow.Children.Add(new TextBlock { Text = "同时下载数量", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(concurrency, 1); concurrencyRow.Children.Add(concurrency);
        _content.Children.Add(concurrencyRow);
        var completionOptions = new[] { "不执行操作", "在文件夹中显示" };
        var completionValues = new[] { DownloadCompletionPolicy.None, DownloadCompletionPolicy.RevealInFolder };
        var configuredCompletion = DownloadCompletionPolicy.Normalize(
            _root.Setting("downloadCompletionBehavior", DownloadCompletionPolicy.None));
        var completion = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = completionOptions,
            SelectedIndex = Array.IndexOf(completionValues, configuredCompletion)
        };
        completion.SelectionChanged += (_, _) =>
        {
            if (completion.SelectedIndex >= 0)
                _root.SetSetting("downloadCompletionBehavior", completionValues[completion.SelectedIndex]);
        };
        var completionRow = new Grid { ColumnSpacing = 8 };
        completionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        completionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        completionRow.Children.Add(new TextBlock { Text = "完成后操作", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(completion, 1); completionRow.Children.Add(completion);
        _content.Children.Add(completionRow);
        _content.Children.Add(new TextBlock
        {
            Text = "选择“在文件夹中显示”时只会打开资源管理器并选中下载文件，不会自动打开或运行文件。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        });
        _content.Children.Add(new TextBlock { Text = "选择浏览器身份后，仅多站点下载会尝试读取该浏览器的本机登录状态；普通 HTTP/HTTPS 直链不使用浏览器登录状态。Cookie 不会上传，也不会写入快剪数据文件。Windows 版支持 Chrome、Edge 和 Firefox。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        var ytDlp = new YtDlpService();
        _content.Children.Add(new TextBlock
        {
            Text = ytDlp.IsAvailable
                ? (ytDlp.IsFfmpegAvailable ? "多站点下载组件：yt-dlp 和 ffmpeg 已就绪。" : "多站点下载组件：yt-dlp 已就绪；未找到 ffmpeg，部分合并格式可能不可用。")
                : "多站点下载组件：未找到 yt-dlp.exe；基础 HTTP/HTTPS 下载仍可用。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        });
    }

    private void Shortcut()
    {
        _content.Children.Add(new TextBlock { Text = "快捷键", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var hotkey = new ToggleSwitch { Header = "快速显示到桌面（双击 Alt）", IsOn = _root.GetBoolSetting("globalHotkeyEnabled", true) };
        var changingHotkey = false;
        hotkey.Toggled += (_, _) =>
        {
            if (changingHotkey) return;
            if (hotkey.IsOn && !_root.Window.SetGlobalHotkey(true))
            {
                changingHotkey = true;
                hotkey.IsOn = false;
                changingHotkey = false;
                _status.Text = "双击 Alt 快捷键启用失败，请检查系统权限后重试。";
            }
            else
            {
                _root.SetSetting("globalHotkeyEnabled", hotkey.IsOn);
                if (!hotkey.IsOn) _root.Window.SetGlobalHotkey(false);
            }
        };
        _content.Children.Add(hotkey);
        _content.Children.Add(new TextBlock { Text = "Windows 默认使用双击 Alt；按两次 Alt 会显示并激活快剪窗口。使用 Alt+Tab、Alt+F4 等组合键不会触发。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
    }

    private void Hidden()
    {
        _content.Children.Add(new TextBlock { Text = "隐藏", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        AddToggle("双击左侧导航空白处缩小到右侧", "doubleClickCollapseEnabled", true);
        AddToggle("窗口置顶", "alwaysOnTop", false, value => _root.Window.ApplyAlwaysOnTop(value));
        var startup = new ToggleSwitch { Header = "开机启动", IsOn = _root.GetBoolSetting("startupEnabled", WindowsShellService.IsStartupEnabled()) };
        var changingStartup = false;
        startup.Toggled += (_, _) =>
        {
            if (changingStartup) return;
            try { WindowsShellService.SetStartupEnabled(startup.IsOn); _root.SetSetting("startupEnabled", startup.IsOn); }
            catch (Exception ex)
            {
                changingStartup = true;
                startup.IsOn = !startup.IsOn;
                changingStartup = false;
                _status.Text = "开机启动设置失败：" + ex.Message;
            }
        };
        _content.Children.Add(startup);
        AddToggle("点击关闭按钮时最小化到系统托盘", "closeToTrayEnabled", false);
        _content.Children.Add(new TextBlock { Text = "收缩后双击右侧图标栏恢复原位置和大小；启用关闭到托盘后，关闭按钮只隐藏窗口，选择托盘菜单中的“退出”才会结束程序。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
    }

    private void Update()
    {
        _content.Children.Add(new TextBlock { Text = "更新", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _content.Children.Add(new TextBlock { Text = $"当前版本 v{AppInfo.CurrentVersion}", Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        var check = new Button { Content = "检查更新" };
        check.Click += async (_, _) => await CheckForUpdatesAsync(check);
        _content.Children.Add(check);
        _downloadUpdate = new Button { Content = "更新到最新版本", Visibility = Visibility.Collapsed };
        _downloadUpdate.Click += async (_, _) => await DownloadAndLaunchUpdateAsync();
        _content.Children.Add(_downloadUpdate);
        var updateButton = _downloadUpdate;
        var availableUpdate = _root.AvailableUpdate;
        if (updateButton is not null && availableUpdate is not null && UpdateService.IsNewer(availableUpdate))
        {
            _latestUpdate = availableUpdate;
            _status.Text = $"发现新版本 v{availableUpdate.Version}\n{availableUpdate.Notes}";
            updateButton.Visibility = Visibility.Visible;
        }
    }

    private async Task CheckForUpdatesAsync(Button check)
    {
        check.IsEnabled = false;
        _downloadUpdate!.Visibility = Visibility.Collapsed;
        _latestUpdate = null;
        _status.Text = "正在检查 Windows 更新…";
        try
        {
            var info = await _updates.CheckAsync();
            _root.SetAvailableUpdate(info);
            if (info is null)
            {
                _status.Text = "当前没有适用于 Windows x64 的更新。";
                return;
            }
            if (!Version.TryParse(info.Version, out var remote) || !Version.TryParse(AppInfo.CurrentVersion, out var current) || remote <= current)
            {
                _status.Text = $"当前已是最新版本（v{AppInfo.CurrentVersion}）。";
                return;
            }
            _latestUpdate = info;
            _status.Text = $"发现新版本 v{info.Version}\n{info.Notes}";
            _downloadUpdate.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _status.Text = "检查更新失败：" + ex.Message;
        }
        finally
        {
            check.IsEnabled = true;
        }
    }

    private async Task DownloadAndLaunchUpdateAsync()
    {
        if (_latestUpdate is null || _downloadUpdate is null) return;
        _downloadUpdate.IsEnabled = false;
        _status.Text = "正在下载 Windows 安装包…";
        try
        {
            var path = await _updates.DownloadInstallerAsync(_latestUpdate);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            _status.Text = "安装包已启动，请按安装向导完成更新。";
        }
        catch (Exception ex)
        {
            _status.Text = "下载更新失败：" + ex.Message;
            _downloadUpdate.IsEnabled = true;
        }
    }

    private void AddToggle(string title, string key, bool fallback, Action<bool>? changed = null)
    {
        var toggle = new ToggleSwitch { Header = title, IsOn = _root.GetBoolSetting(key, fallback) };
        toggle.Toggled += (_, _) =>
        {
            _root.SetSetting(key, toggle.IsOn);
            changed?.Invoke(toggle.IsOn);
        };
        _content.Children.Add(toggle);
    }

    private async Task ImportClipboardHistoryAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _root.Window.Hwnd);
        foreach (var extension in new[] { ".dat", ".json", ".csv", ".txt", ".gz" }) picker.FileTypeFilter.Add(extension);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;

        var generatedImportResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var budget = new LegacyClipboardImportBudget();
            var imported = new List<ClipboardItem>();
            foreach (var file in files)
                imported.AddRange(await ReadLegacyClipboardAsync(file.Path, _root.Data.UserId, generatedImportResources, budget));
            var merge = await ClipboardImportMergePolicy.SelectUniqueAsync(
                _root.Items,
                imported,
                HasClipboardPayload,
                CreateImportShape,
                ClipboardImportIdentityKeyAsync);
            foreach (var item in merge.Skipped)
                DeleteGeneratedImportResources(item, generatedImportResources);
            foreach (var item in merge.Unique)
                TransferGeneratedImportResources(item, generatedImportResources);
            _root.Items.InsertRange(0, merge.Unique);
            await _root.SetClipboardRetentionLimitAsync(_root.ClipboardRetentionLimit);
            await _root.SaveClipboardAsync();
            _status.Text = $"已导入 {merge.Unique.Count} 条旧版记录（跳过重复 {merge.Skipped.Count} 条）。";
        }
        catch (Exception ex)
        {
            foreach (var path in generatedImportResources)
                ClipboardCaptureCleanup.DeleteOrphanedFile(path);
            _status.Text = "导入失败：" + ex.Message;
        }
    }

    private static void DeleteGeneratedImportResources(ClipboardItem item, HashSet<string> generatedResources)
    {
        foreach (var path in new[] { item.ImagePath, item.RtfPath })
        {
            if (!string.IsNullOrWhiteSpace(path) && generatedResources.Remove(path))
                ClipboardCaptureCleanup.DeleteOrphanedFile(path);
        }
    }

    private static void TransferGeneratedImportResources(ClipboardItem item, HashSet<string> generatedResources)
    {
        if (!string.IsNullOrWhiteSpace(item.ImagePath)) generatedResources.Remove(item.ImagePath);
        if (!string.IsNullOrWhiteSpace(item.RtfPath)) generatedResources.Remove(item.RtfPath);
    }

    private static string FileUrlsIdentity(IEnumerable<string>? paths) =>
        string.Join("\u001F", (paths ?? Array.Empty<string>()).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

    private static (ClipKind Kind, string Text, string? Html, string FileUrls, bool HasRtf) CreateImportShape(ClipboardItem item) =>
        (item.Kind, item.Text, item.Html, FileUrlsIdentity(item.FileUrls),
            item.Kind != ClipKind.Image && !string.IsNullOrWhiteSpace(item.RtfPath));

    private static Task<string> ClipboardImportIdentityKeyAsync(ClipboardItem item) =>
        ClipboardContentIdentity.CreateKeyAsync(item.Kind == ClipKind.Image, item.Kind.ToString(),
            item.Text, item.Html, item.ImagePath, item.RtfPath, item.FileUrls);

    private static bool HasClipboardPayload(ClipboardItem item) =>
        !string.IsNullOrWhiteSpace(item.Text)
        || !string.IsNullOrWhiteSpace(item.Html)
        || !string.IsNullOrWhiteSpace(item.RtfPath)
        || !string.IsNullOrWhiteSpace(item.ImagePath)
        || item.FileUrls?.Count > 0;

    private static async Task<List<ClipboardItem>> ReadLegacyClipboardAsync(
        string path, string? userId, HashSet<string> generatedResources, LegacyClipboardImportBudget budget)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > budget.InputRemaining)
            throw new InvalidDataException($"所选历史文件总量超过 {LegacyClipboardImportBudget.MaximumBytes / (1024 * 1024)} MB。");
        if (budget.InputRemaining <= 0)
            throw new InvalidDataException($"所选历史文件总量超过 {LegacyClipboardImportBudget.MaximumBytes / (1024 * 1024)} MB。");
        await using var inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await BoundedStreamReader.ReadBytesAsync(inputStream, checked((int)budget.InputRemaining));
        budget.ReserveInput(bytes.Length);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            await using var input = new MemoryStream(bytes);
            await using var gzip = new GZipStream(input, CompressionMode.Decompress);
            if (budget.ExpandedRemaining <= 0)
                throw new InvalidDataException($"解压后的历史文件总量超过 {LegacyClipboardImportBudget.MaximumBytes / (1024 * 1024)} MB。");
            bytes = await BoundedStreamReader.ReadBytesAsync(gzip, checked((int)budget.ExpandedRemaining));
        }
        budget.ReserveExpanded(bytes.Length);

        var isDat = Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase);
        var hasUtf16Bom = bytes.Length >= 2
            && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
        if (isDat && !hasUtf16Bom && LooksLikeBinaryPayload(bytes))
            throw new InvalidDataException("无法安全识别此 .dat 私有二进制格式，未导入任何内容。若文件来自 CopyQ，请先在 CopyQ 中用 exportJSON() 导出为 JSON；若来自其他剪贴板软件，需要提供格式说明或样本才能实现兼容。");

        var encoding = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode
            : bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
                ? Encoding.BigEndianUnicode
                : Encoding.UTF8;
        var text = encoding.GetString(bytes).Trim('\uFEFF', '\0', ' ', '\r', '\n', '\t');
        if (string.IsNullOrWhiteSpace(text)) return new();

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
            var direct = JsonSerializer.Deserialize<List<ClipboardItem>>(text, options);
            if (direct is not null && direct.Any(HasClipboardPayload)) return direct;
        }
        catch (JsonException)
        {
            // Some older exports use a different field shape; try the compatible adapter below.
        }

        var copyQExport = await ClipboardImportFormatPolicy.TryParseCopyQJsonAsync(
            text, content => TryReadCopyQJsonExportAsync(content, userId, generatedResources));
        if (copyQExport is not null) return copyQExport;

        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var rows = ClipboardHistoryCsvCodec.Parse(text);
            if (rows.Count > 0)
            {
                var converted = rows.Select(row => new ClipboardItem
                {
                    Text = row.Text,
                    Title = row.Title,
                    Kind = row.IsUrl ? ClipKind.Url : ClipKind.Text,
                    Source = row.Source,
                    Note = row.Note
                }).ToList();
                return converted;
            }
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var array = FindLegacyItemsArray(document.RootElement);
            if (array is not null)
            {
                var converted = new List<ClipboardItem>();
                foreach (var element in array.Value.EnumerateArray())
                {
                    var value = FirstString(element, "text", "content", "value", "title");
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var kind = FirstString(element, "kind", "type");
                    var isUrl = kind?.Contains("url", StringComparison.OrdinalIgnoreCase) == true
                        || Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
                    converted.Add(new ClipboardItem
                    {
                        Text = value,
                        Title = FirstString(element, "title") ?? value,
                        Kind = isUrl ? ClipKind.Url : ClipKind.Text,
                        Source = FirstString(element, "source", "app") ?? "来源未知",
                        Note = FirstString(element, "note", "remark") ?? ""
                    });
                }
                if (converted.Count > 0) return converted;
            }
        }
        catch (JsonException) { }

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Select(line => line.Trim()).Where(line => line.Length > 0).Select(value => new ClipboardItem
        {
            Text = value,
            Title = value,
            Kind = Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") ? ClipKind.Url : ClipKind.Text,
            Source = "旧版历史"
        }).ToList();
    }

    private static bool LooksLikeBinaryPayload(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        var length = Math.Min(bytes.Length, 8192);
        var control = 0;
        for (var index = 0; index < length; index++)
        {
            var value = bytes[index];
            if (value == 0 || (value < 0x09) || (value > 0x0D && value < 0x20)) control++;
        }
        if (control > length / 40) return true;
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return false;
        }
        catch (DecoderFallbackException)
        {
            return true;
        }
    }

    private static async Task<List<ClipboardItem>?> TryReadCopyQJsonExportAsync(
        string text, string? userId, HashSet<string> generatedResources)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var items = new List<ClipboardItem>();
            foreach (var element in root.EnumerateArray())
            {
                var item = await ConvertCopyQJsonItemAsync(element, userId, generatedResources);
                if (item is not null) items.Add(item);
            }
            return items.Count == 0 ? null : items;
        }

        if (root.ValueKind != JsonValueKind.Object) return null;

        JsonElement copyQItems = default;
        var hasCopyQItems = false;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals("copyq_items", StringComparison.OrdinalIgnoreCase)) continue;
            copyQItems = property.Value;
            hasCopyQItems = true;
            break;
        }

        if (hasCopyQItems && copyQItems.ValueKind == JsonValueKind.Array)
        {
            var items = new List<ClipboardItem>();
            foreach (var element in copyQItems.EnumerateArray())
            {
                var item = await ConvertCopyQJsonItemAsync(element, userId, generatedResources);
                if (item is not null) items.Add(item);
            }
            return items.Count == 0 ? null : items;
        }

        var single = await ConvertCopyQJsonItemAsync(root, userId, generatedResources);
        return single is null ? null : new List<ClipboardItem> { single };
    }

    private static async Task<ClipboardItem?> ConvertCopyQJsonItemAsync(
        JsonElement element, string? userId, HashSet<string> generatedResources)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        string? plainText = null;
        string? html = null;
        string? rtf = null;
        string? note = null;
        byte[]? imageBytes = null;
        var imageExtension = ".png";
        var recognized = false;

        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name.Trim();
            if (name.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
                || name.Equals("text", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("text/plain;", StringComparison.OrdinalIgnoreCase))
            {
                plainText = CopyQTextValue(property.Value);
                recognized = true;
            }
            else if (name.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("text/html;", StringComparison.OrdinalIgnoreCase))
            {
                html = CopyQTextValue(property.Value);
                recognized = true;
            }
            else if (name.Equals("text/rtf", StringComparison.OrdinalIgnoreCase)
                || name.Equals("application/rtf", StringComparison.OrdinalIgnoreCase))
            {
                rtf = CopyQTextValue(property.Value);
                recognized = true;
            }
            else if (name.Equals("text/uri-list", StringComparison.OrdinalIgnoreCase))
            {
                plainText ??= CopyQTextValue(property.Value);
                recognized = true;
            }
            else if (name.Equals("application/x-copyq-tags", StringComparison.OrdinalIgnoreCase))
            {
                note = CopyQTextValue(property.Value);
                recognized = true;
            }
            else if (name.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = CopyQBase64Value(property.Value);
                if (bytes is not null)
                {
                    imageBytes = bytes;
                    imageExtension = name switch
                    {
                        "image/jpeg" or "image/jpg" => ".jpg",
                        "image/gif" => ".gif",
                        "image/bmp" => ".bmp",
                        "image/webp" => ".webp",
                        _ => ".png"
                    };
                }
                recognized = true;
            }
        }

        if (!recognized || (string.IsNullOrWhiteSpace(plainText)
            && imageBytes is null
            && string.IsNullOrWhiteSpace(html)
            && string.IsNullOrWhiteSpace(rtf)))
            return null;

        var item = new ClipboardItem
        {
            Kind = imageBytes is not null
                ? ClipKind.Image
                : IsHttpUrl(plainText) ? ClipKind.Url : ClipKind.Text,
            Title = imageBytes is not null && string.IsNullOrWhiteSpace(plainText)
                ? "图片"
                : plainText ?? "富文本记录",
            Text = plainText ?? "",
            Html = html,
            Note = note ?? "",
            Source = "CopyQ 导出"
        };

        if (imageBytes is not null && !string.IsNullOrWhiteSpace(userId))
        {
            var folder = AppPaths.Images(userId);
            Directory.CreateDirectory(folder);
            var imagePath = Path.Combine(folder, $"legacy-{Guid.NewGuid():N}{imageExtension}");
            generatedResources.Add(imagePath);
            await File.WriteAllBytesAsync(imagePath, imageBytes);
            item.ImagePath = imagePath;
        }

        if (!string.IsNullOrWhiteSpace(rtf) && !string.IsNullOrWhiteSpace(userId))
        {
            var folder = AppPaths.RichData(userId);
            Directory.CreateDirectory(folder);
            var rtfPath = Path.Combine(folder, $"legacy-{Guid.NewGuid():N}.rtf");
            generatedResources.Add(rtfPath);
            await File.WriteAllTextAsync(rtfPath, rtf, Encoding.UTF8);
            item.RtfPath = rtfPath;
        }

        return item;
    }

    private static string? CopyQTextValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (value.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
            return string.Join("\n", lines.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        if (value.TryGetProperty("base64", out var base64) && base64.ValueKind == JsonValueKind.String)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64.GetString() ?? "")); }
            catch (FormatException) { return null; }
        }
        return null;
    }

    private static byte[]? CopyQBase64Value(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("base64", out var base64)
            || base64.ValueKind != JsonValueKind.String)
            return null;
        try { return Convert.FromBase64String(base64.GetString() ?? ""); }
        catch (FormatException) { return null; }
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static JsonElement? FindLegacyItemsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { "items", "history", "records", "data", "clips", "clipboard", "copyq_items" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }
        return null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

}
