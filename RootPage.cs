using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace KuaiJian.WinUI;

public sealed class RootPage : Page
{
    public readonly MainWindow Window;
    public readonly AuthService Auth = new();
    public readonly LocalDataStore Data = new();
    public bool CloseToTrayEnabled => GetBoolSetting("closeToTrayEnabled", false);
    public readonly CloudSyncService Cloud = new();
    public List<ClipboardItem> Items { get; set; } = new();
    public List<string> DeletedClipboardIds { get; private set; } = new();
    public List<QaEntry> QaEntries { get; set; } = new();
    public List<ShortcutItem> Shortcuts { get; set; } = new();
    public List<string> CalculatorHistory { get; set; } = new();
    public List<DownloadTaskItem> DownloadTasks { get; set; } = new();
    public string? DownloadDirectory { get; set; }
    public string DownloadBrowser { get; set; } = "none";
    public int DownloadConcurrency { get; set; } = 2;
    public DownloadConcurrencyGate DownloadGate { get; } = new(2);
    public int ClipboardRetentionLimit { get; private set; } = ClipboardRetentionPolicy.DefaultLimit;
    public string TransferReceiveDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "KuaiJian Received");
    public UpdateInfo? AvailableUpdate { get; private set; }
    public string ClipboardStatus { get; private set; } = "";
    public string CloudSyncStatus { get; private set; } = "";
    public event EventHandler? UpdateAvailabilityChanged;
    public event EventHandler? ClipboardStatusChanged;

    private ClipboardMonitor? _clipboardMonitor;
    private NavigationView? _navigation;
    private NavigationViewItem? _settingsItem;
    private Frame? _frame;
    private ClipboardPage? _clipboardPage;
    private QuestionAnswersPage? _qaPage;
    private DownloadsPage? _downloadsPage;
    private bool _initialized;
    private bool _settingsDialogOpen;
    private bool _userLoadInProgress;
    private ContentDialog? _settingsDialog;
    private CancellationTokenSource? _cloudPushDelay;
    private CancellationTokenSource? _cloudSyncLoop;
    private int _sessionExpiryHandling;
    private readonly SemaphoreSlim _cloudSyncGate = new(1, 1);

    public RootPage(MainWindow window)
    {
        Window = window;
        Content = Loading("正在恢复登录状态…");
        Auth.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshAuthState);
    }

    public void StartInitialization()
    {
        if (_initialized) return;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await Auth.RestoreAsync();
        _ = CheckForUpdatesSilentlyAsync();
        RefreshAuthState();
    }

    private void RefreshAuthState()
    {
        if (Auth.IsRestoring)
        {
            Content = Loading("正在恢复登录状态…");
        }
        else if (Auth.CurrentUser is null)
        {
            StopClipboardMonitor();
            StopCloudSyncLoop();
            Window.SetLoginLayout(true);
            if (Content is not AuthPage)
                Content = new AuthPage(Auth, ShowMainInterface);
        }
        else if (_navigation is null)
        {
            _ = LoadUserAndShowMainAsync();
        }
    }

    private async Task LoadUserAndShowMainAsync()
    {
        if (Auth.CurrentUser is null || _navigation is not null || _userLoadInProgress) return;
        _userLoadInProgress = true;
        var userId = Auth.CurrentUser.Id;
        try
        {
            Data.SetUser(userId);
            MigrateLegacyUserSettings(userId);
            var configuredRetentionLimit = Setting("clipboardRetentionLimit", ClipboardRetentionPolicy.DefaultLimit.ToString());
            ClipboardRetentionLimit = int.TryParse(configuredRetentionLimit, out var retentionLimit)
                ? ClipboardRetentionPolicy.NormalizeLimit(retentionLimit)
                : ClipboardRetentionPolicy.DefaultLimit;
            if (!string.Equals(configuredRetentionLimit, ClipboardRetentionLimit.ToString(), StringComparison.Ordinal))
                SetSetting("clipboardRetentionLimit", ClipboardRetentionLimit.ToString());
            var items = await Data.LoadClipboardAsync(ClipboardRetentionLimit);
            var deletedIds = await Data.LoadDeletedClipboardIdsAsync();
            var qaEntries = await Data.LoadQaAsync();
            var calculatorHistory = await Data.LoadCalculatorHistoryAsync();
            var downloadTasks = await Data.LoadDownloadsAsync();
            var shortcuts = LoadShortcuts();
            if (Auth.CurrentUser?.Id != userId || Data.UserId != userId) return;

            DeletedClipboardIds = deletedIds;
            Items = ClipboardSyncMerge.ExcludeDeleted(items, deletedIds, item => item.Id);
            if (Items.Count != items.Count)
                await Data.SaveClipboardAsync(Items);
            QaEntries = qaEntries;
            Shortcuts = shortcuts;
            CalculatorHistory = calculatorHistory;
            DownloadTasks = downloadTasks;
            DownloadDirectory = Setting("downloadDirectory", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            TransferReceiveDirectory = Setting("transferReceiveDirectory", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "KuaiJian Received"));
            var configuredConcurrency = Setting("downloadConcurrency", "2");
            DownloadConcurrency = int.TryParse(configuredConcurrency, out var concurrency)
                ? Math.Clamp(concurrency, 1, 5)
                : 2;
            DownloadGate.SetLimit(DownloadConcurrency);
            if (!string.Equals(configuredConcurrency, DownloadConcurrency.ToString(), StringComparison.Ordinal))
                SetSetting("downloadConcurrency", DownloadConcurrency.ToString());
            var configuredBrowser = Setting("downloadBrowser", "none");
            DownloadBrowser = NormalizeDownloadBrowser(configuredBrowser);
            if (!string.Equals(configuredBrowser, DownloadBrowser, StringComparison.Ordinal))
                SetSetting("downloadBrowser", DownloadBrowser);
            var hotkeyEnabled = GetBoolSetting("globalHotkeyEnabled", true);
            var hotkeyRegistered = Window.ConfigureShell(
                GetBoolSetting("startupEnabled", false),
                hotkeyEnabled);
            if (hotkeyEnabled && !hotkeyRegistered)
                SetSetting("globalHotkeyEnabled", false);
            Window.ApplyAlwaysOnTop(GetBoolSetting("alwaysOnTop", false));
            StartClipboardMonitor();
            BuildMainInterface();
            if (Data.LoadWarnings.Count > 0)
            {
                ClipboardStatus = string.Join("\n", Data.LoadWarnings);
                ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
            }
            _ = PullClipboardAsync();
            StartCloudSyncLoop(userId, Auth.Token);
        }
        finally
        {
            _userLoadInProgress = false;
            if (Auth.CurrentUser is not null && _navigation is null)
                DispatcherQueue.TryEnqueue(() => _ = LoadUserAndShowMainAsync());
        }
    }

    public async Task CheckForUpdatesSilentlyAsync()
    {
        try
        {
            var info = await new UpdateService().CheckAsync();
            SetAvailableUpdate(info);
        }
        catch
        {
            // Startup checks are deliberately silent; the settings page exposes the detailed error path.
        }
    }

    public void SetAvailableUpdate(UpdateInfo? info)
    {
        AvailableUpdate = UpdateService.IsNewer(info) ? info : null;
        SetSetting("updateAvailable", AvailableUpdate is not null);
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyUpdateBadge();
            UpdateAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ShowMainInterface()
    {
        DispatcherQueue.TryEnqueue(() => _ = LoadUserAndShowMainAsync());
    }

    private void BuildMainInterface()
    {
        Window.SetLoginLayout(false);
        _frame = new Frame();
        _navigation = new NavigationView
        {
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            OpenPaneLength = 150,
            CompactModeThresholdWidth = 700,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Margin = new Thickness(10)
        };
        _navigation.DoubleTapped += (_, e) =>
        {
            var isCollapsed = Window.IsCollapsed;
            var source = e.OriginalSource as DependencyObject;
            var isInteractiveSource = IsNavigationActionSource(source);
            var isWithinPane = isCollapsed || e.GetPosition(_navigation).X < _navigation.OpenPaneLength;
            if (!NavigationCollapsePolicy.ShouldToggle(
                    GetBoolSetting("doubleClickCollapseEnabled", true), isCollapsed, isInteractiveSource, isWithinPane)) return;
            Window.ToggleCollapsed();
            _navigation.IsPaneOpen = !Window.IsCollapsed;
            ApplyCollapsedNavigation(Window.IsCollapsed);
        };

        AddNavigationItem("剪贴板", "clipboard", Symbol.Copy);
        AddNavigationItem("团队", "team", Symbol.People);
        AddNavigationItem("问答", "qa", Symbol.Comment);
        AddNavigationItem("计算机", "calculator", Symbol.Calculator);
        AddNavigationItem("下载", "downloads", Symbol.Download);
        AddNavigationItem("快捷", "shortcuts", Symbol.Keyboard);
        AddNavigationItem("翻译", "translation", Symbol.Edit);
        AddNavigationItem("连接", "connections", Symbol.Link);
        AddNavigationItem("收藏", "favorites", Symbol.Favorite);
        AddNavigationItem("互传", "transfer", Symbol.Share);
        _settingsItem = new NavigationViewItem
        {
            Content = "设置",
            Tag = "settings",
            Icon = new SymbolIcon(Symbol.Setting),
            InfoBadge = new InfoBadge
            {
                Value = -1,
                Width = 10,
                Height = 10,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Red),
                Visibility = Visibility.Collapsed
            }
        };
        _navigation.FooterMenuItems.Add(_settingsItem);
        ApplyUpdateBadge();
        UpdateAvailabilityChanged += (_, _) => ApplyUpdateBadge();
        _navigation.SelectionChanged += NavigationSelectionChanged;
        _navigation.Content = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(8, 0, 0, 0),
            Child = _frame
        };
        _navigation.SelectedItem = _navigation.MenuItems[0];
        Content = new Border
        {
            Background = (Brush)Application.Current.Resources["CanvasBrush"],
            CornerRadius = new CornerRadius(20),
            Child = _navigation
        };
        ApplyCollapsedNavigation(Window.IsCollapsed);
    }

    private void ApplyCollapsedNavigation(bool collapsed)
    {
        if (_navigation is null) return;
        var compactTags = new HashSet<string> { "clipboard", "team", "downloads", "favorites" };
        foreach (var item in _navigation.MenuItems.OfType<NavigationViewItem>())
        {
            item.Visibility = !collapsed || (item.Tag is string tag && compactTags.Contains(tag))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        foreach (var item in _navigation.FooterMenuItems.OfType<NavigationViewItem>())
        {
            item.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ApplyUpdateBadge()
    {
        if (_settingsItem?.InfoBadge is InfoBadge badge)
            badge.Visibility = AvailableUpdate is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddNavigationItem(string title, string tag, Symbol symbol)
    {
        _navigation!.MenuItems.Add(new NavigationViewItem { Content = title, Tag = tag, Icon = new SymbolIcon(symbol) });
    }

    private static bool IsNavigationActionSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is NavigationViewItem
                || source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;
        ApplyNavigationSelection(item);
        switch (tag)
        {
            case "clipboard":
                _clipboardPage = new ClipboardPage(this, false);
                _frame!.Content = _clipboardPage;
                break;
            case "favorites":
                _clipboardPage = new ClipboardPage(this, true);
                _frame!.Content = _clipboardPage;
                break;
            case "qa":
                _qaPage ??= new QuestionAnswersPage(this);
                _frame!.Content = _qaPage;
                _qaPage.ReadClipboardIfVisible();
                break;
            case "calculator": _frame!.Content = new CalculatorPage(this); break;
            case "downloads":
                _downloadsPage ??= new DownloadsPage(this);
                _frame!.Content = _downloadsPage;
                break;
            case "shortcuts": _frame!.Content = new ShortcutsPage(this); break;
            case "translation": _frame!.Content = new TranslationPage(this); break;
            case "connections": _frame!.Content = new ConnectionsPage(this); break;
            case "transfer": _frame!.Content = new TransferPage(this); break;
            case "team": _frame!.Content = Placeholder("团队", "团队空间将在原生版第二阶段接入"); break;
            case "settings": _ = ShowSettingsDialogAsync(); break;
        }
    }

    private void ApplyNavigationSelection(NavigationViewItem selected)
    {
        if (_navigation is null) return;
        var items = _navigation.MenuItems
            .Concat(_navigation.FooterMenuItems)
            .OfType<NavigationViewItem>();
        foreach (var item in items)
        {
            var isSelected = ReferenceEquals(item, selected);
            item.Background = isSelected ? (Brush)Application.Current.Resources["MintSoftBrush"] : null;
            item.Foreground = isSelected ? (Brush)Application.Current.Resources["MintBrush"] : (Brush)Application.Current.Resources["InkBrush"];
        }
    }

    private async Task ShowSettingsDialogAsync()
    {
        if (_settingsDialogOpen || _frame?.XamlRoot is null) return;
        _settingsDialogOpen = true;
        var dialog = new ContentDialog
        {
            Title = "设置",
            Content = new SettingsPage(this),
            CloseButtonText = "完成",
            XamlRoot = _frame.XamlRoot
        };
        _settingsDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally
        {
            _settingsDialog = null;
            _settingsDialogOpen = false;
            if (Auth.CurrentUser is not null) Navigate("clipboard");
        }
    }

    public void CloseSettingsDialog() => _settingsDialog?.Hide();

    public void Navigate(string tag)
    {
        if (_navigation is null) return;
        var all = _navigation.MenuItems.Concat(_navigation.FooterMenuItems).OfType<NavigationViewItem>();
        var item = all.FirstOrDefault(x => Equals(x.Tag, tag));
        if (item is not null) _navigation.SelectedItem = item;
    }

    public bool IsCurrentPage(Page page) => ReferenceEquals(_frame?.Content, page);

    public void RestoreNavigationAfterWindowExpand()
    {
        if (_navigation is null) return;
        _navigation.IsPaneOpen = true;
        ApplyCollapsedNavigation(false);
    }

    private void StartClipboardMonitor()
    {
        StopClipboardMonitor();
        _clipboardMonitor = new ClipboardMonitor(
            Data,
            () => GetBoolSetting("captureText", true),
            () => GetBoolSetting("captureImages", true),
            () => GetBoolSetting("captureURLs", true),
            () => GetBoolSetting("captureRichText", true));
        _clipboardMonitor.ItemCaptured += OnClipboardItemCaptured;
        _clipboardMonitor.CaptureFailed += OnClipboardCaptureFailed;
        _clipboardMonitor.TextObserved += OnClipboardTextObserved;
    }

    private void StopClipboardMonitor()
    {
        if (_clipboardMonitor is null) return;
        _clipboardMonitor.ItemCaptured -= OnClipboardItemCaptured;
        _clipboardMonitor.CaptureFailed -= OnClipboardCaptureFailed;
        _clipboardMonitor.TextObserved -= OnClipboardTextObserved;
        _clipboardMonitor.Dispose();
        _clipboardMonitor = null;
    }

    private async void OnClipboardTextObserved(object? sender, ClipboardTextObservedEventArgs observed)
    {
        try
        {
            await DispatcherQueue.EnqueueAsync(async () =>
            {
                var userId = Auth.CurrentUser?.Id;
                if (userId is null || observed.UserId != userId || Data.UserId != userId
                    || !GetBoolSetting("qaAssistantEnabled", false)) return;
                if (!IsQaSourceAllowed(observed.Source)) return;

                var match = QaMatcher.Find(observed.Text, QaEntries);
                if (match is null || !match.IsSafeForAutomaticCopy || string.IsNullOrWhiteSpace(match.Entry.Answer)) return;

                // Clipboard notifications are asynchronous. Before writing an answer,
                // verify that the clipboard still contains the question we matched.
                var current = Clipboard.GetContent();
                if (!current.Contains(StandardDataFormats.Text)) return;
                var currentText = (await current.GetTextAsync()).Trim();
                if (!string.Equals(currentText, observed.Text.Trim(), StringComparison.Ordinal)) return;
                if (string.Equals(QaMatcher.Normalize(currentText), QaMatcher.Normalize(match.Entry.Answer), StringComparison.Ordinal)) return;

                if (!TrySetClipboardText(match.Entry.Answer))
                {
                    _qaPage?.ReportAssistantResult("已匹配问题，但复制答案失败；系统剪贴板可能正被其他程序占用。");
                    return;
                }
                _qaPage?.ReportAssistantResult($"已匹配“{match.Entry.Question}”，答案已复制到剪贴板。");
            });
        }
        catch
        {
            // Global matching must never interrupt capture of the user's clipboard.
        }
    }

    private bool IsQaSourceAllowed(string source)
    {
        return QaSourceFilter.IsAllowed(Setting("qaAssistantSources", ""), source);
    }

    private void OnClipboardCaptureFailed(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ClipboardStatus = message.Trim();
            ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async void OnClipboardItemCaptured(object? sender, ClipboardItem item)
    {
        await DispatcherQueue.EnqueueAsync(async () =>
        {
            if (Auth.CurrentUser is null || Data.UserId != Auth.CurrentUser.Id) return;
            if (await HasDuplicateClipboardItemAsync(item))
            {
                CleanupDuplicateCapture(item);
                // A repeated question is still a new clipboard action for the Q&A page.
                _qaPage?.ReadClipboardIfVisible();
                return;
            }
            Items.Insert(0, item);
            TrimClipboardHistory();
            ClipboardStatus = Data.LoadWarnings.Count == 0 ? "" : string.Join("\n", Data.LoadWarnings);
            ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
            await SaveClipboardAsync();
            _clipboardPage?.Refresh();
            _qaPage?.ReadClipboardIfVisible();
        });
    }

    private async Task<bool> HasDuplicateClipboardItemAsync(ClipboardItem candidate)
    {
        var candidateKey = await ClipboardIdentityKeyAsync(candidate);
        foreach (var existing in Items)
        {
            if (existing.Kind != candidate.Kind) continue;
            if (!string.Equals(existing.Text, candidate.Text, StringComparison.Ordinal)
                || !string.Equals(existing.Html, candidate.Html, StringComparison.Ordinal)
                || !string.Equals(FileUrlsIdentity(existing.FileUrls), FileUrlsIdentity(candidate.FileUrls), StringComparison.Ordinal)
                || (candidate.Kind != ClipKind.Image
                    && string.IsNullOrWhiteSpace(existing.RtfPath) != string.IsNullOrWhiteSpace(candidate.RtfPath))) continue;
            if (string.Equals(await ClipboardIdentityKeyAsync(existing), candidateKey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string FileUrlsIdentity(IEnumerable<string> paths) =>
        string.Join("\u001F", paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

    private static Task<string> ClipboardIdentityKeyAsync(ClipboardItem item) =>
        ClipboardContentIdentity.CreateKeyAsync(item.Kind == ClipKind.Image, item.Kind.ToString(),
            item.Text, item.Html, item.ImagePath, item.RtfPath, item.FileUrls);

    private static void CleanupDuplicateCapture(ClipboardItem item)
    {
        // ClipboardMonitor materializes images and RTF into the current
        // account before the UI-level duplicate check. Remove only those
        // newly captured files when the logical item already exists.
        foreach (var path in new[] { item.ImagePath, item.RtfPath })
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Cleanup is best-effort and must not interrupt clipboard capture.
            }
        }
    }

    public async Task CopyItemAsync(ClipboardItem item)
    {
        if (item.Kind == ClipKind.Image && (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath)))
        {
            ClipboardStatus = "图片文件已不存在，无法复制。";
            ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (item.FileUrls.Count > 0 && item.FileUrls.Any(path => !File.Exists(path) && !Directory.Exists(path)))
        {
            ClipboardStatus = "文件或目录已不存在，无法复制。";
            ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var clipboardSet = false;
        try
        {
            var package = new DataPackage();
            if (item.Kind == ClipKind.Image && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
            {
                var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            }
            else
            {
                package.SetText(item.Text);
                if (!string.IsNullOrWhiteSpace(item.Html)) package.SetHtmlFormat(item.Html);
                if (!string.IsNullOrWhiteSpace(item.RtfPath) && File.Exists(item.RtfPath))
                    package.SetData(StandardDataFormats.Rtf, await File.ReadAllTextAsync(item.RtfPath));
                if (item.FileUrls.Count > 0)
                {
                    var files = new List<IStorageItem>();
                    foreach (var path in item.FileUrls)
                    {
                        if (File.Exists(path))
                            files.Add(await StorageFile.GetFileFromPathAsync(path));
                        else if (Directory.Exists(path))
                            files.Add(await StorageFolder.GetFolderFromPathAsync(path));
                    }
                    if (files.Count != item.FileUrls.Count)
                        throw new FileNotFoundException("文件或目录已不存在。 ");
                    package.SetStorageItems(files);
                }
            }
            _clipboardMonitor?.IgnoreNextChange();
            Clipboard.SetContent(package);
            clipboardSet = true;
            Clipboard.Flush();
            ClipboardStatus = "";
            ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            if (!clipboardSet) CancelClipboardChangeIgnore();
            ClipboardStatus = "复制失败，文件可能已不存在或系统剪贴板正被其他程序占用。";
            ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task CaptureFilePathsAsync(IEnumerable<string> paths, string source = "文件拖放")
    {
        if (!GetBoolSetting("captureText", true)) return;
        if (Auth.CurrentUser is null || Data.UserId != Auth.CurrentUser.Id) return;
        var normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) return;

        var item = new ClipboardItem
        {
            Kind = ClipKind.Text,
            Title = string.Join("、", normalized.Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))),
            Text = string.Join(Environment.NewLine, normalized),
            FileUrls = normalized,
            Source = source
        };
        if (await HasDuplicateClipboardItemAsync(item)) return;
        Items.Insert(0, item);
        TrimClipboardHistory();
        await SaveClipboardAsync();
        _clipboardPage?.Refresh();
        _qaPage?.ReadClipboardIfVisible();
    }

    public void IgnoreNextClipboardChange() => _clipboardMonitor?.IgnoreNextChange();

    public void CancelClipboardChangeIgnore() => _clipboardMonitor?.CancelIgnoreNextChange();

    public bool TrySetClipboardText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var package = new DataPackage();
        package.SetText(text);
        return ClipboardWritePolicy.TryWrite(
            () => _clipboardMonitor?.IgnoreNextChange(),
            () => Clipboard.SetContent(package),
            Clipboard.Flush,
            () => _clipboardMonitor?.CancelIgnoreNextChange());
    }

    public async Task SaveClipboardAsync()
    {
        await Data.SaveClipboardAsync(Items);
        SchedulePushClipboard();
    }

    public async Task SetClipboardRetentionLimitAsync(int limit)
    {
        ClipboardRetentionLimit = ClipboardRetentionPolicy.NormalizeLimit(limit);
        SetSetting("clipboardRetentionLimit", ClipboardRetentionLimit.ToString());
        var previousCount = Items.Count;
        TrimClipboardHistory();
        if (Items.Count != previousCount)
        {
            await SaveClipboardAsync();
            _clipboardPage?.Refresh();
        }
    }

    private void TrimClipboardHistory()
    {
        Items = ClipboardRetentionPolicy.Retain(
            Items, ClipboardRetentionLimit, item => item.Pinned, item => item.CreatedAt);
    }

    public async Task DeleteClipboardItemAsync(ClipboardItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id)) return;
        if (!DeletedClipboardIds.Contains(item.Id, StringComparer.Ordinal))
        {
            DeletedClipboardIds.Add(item.Id);
            await Data.SaveDeletedClipboardIdsAsync(DeletedClipboardIds);
        }
        Items.RemoveAll(existing => string.Equals(existing.Id, item.Id, StringComparison.Ordinal));
        await SaveClipboardAsync();
        ClipboardStatus = "已从本设备移除；服务端暂不支持删除同步，其他设备可能仍显示此记录。";
        ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task SaveQaAsync() => await Data.SaveQaAsync(QaEntries);
    public async Task SaveDownloadTasksAsync() => await Data.SaveDownloadsAsync(DownloadTasks);
    public async Task SaveDownloadTasksAsync(string userId, IReadOnlyCollection<DownloadTaskItem> tasks)
    {
        if (!UserSessionPolicy.OwnsData(userId, Auth.CurrentUser?.Id, Data.UserId)) return;
        await Data.SaveDownloadsAsync(userId, tasks);
    }

    public void NotifyDownloadTasksChanged()
    {
        _downloadsPage?.RefreshFromSharedTasks();
    }

    public async Task AddOrUpdateQaAsync(QaEntry entry)
    {
        var existing = QaEntries.FindIndex(x => x.Id == entry.Id);
        if (existing >= 0) QaEntries[existing] = entry;
        else QaEntries.Insert(0, entry);
        await SaveQaAsync();
        _qaPage?.Refresh();
    }

    public async Task<int> MergeQaEntriesAsync(IEnumerable<QaEntry> entries)
    {
        var importedCount = QaImportMergePolicy.MergeInto(QaEntries, entries);
        if (importedCount > 0)
        {
            await SaveQaAsync();
            _qaPage?.Refresh();
        }
        return importedCount;
    }

    public async Task RemoveQaAsync(QaEntry entry)
    {
        QaEntries.RemoveAll(x => x.Id == entry.Id);
        await SaveQaAsync();
        _qaPage?.Refresh();
    }

    public async Task PullClipboardAsync()
    {
        var userId = Auth.CurrentUser?.Id;
        var token = Auth.Token;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)) return;
        await _cloudSyncGate.WaitAsync();
        try
        {
            var remote = await Cloud.PullAsync(token);
            if (!IsCurrentSession(userId, token)) return;
            remote = ClipboardSyncMerge.ExcludeDeleted(remote, DeletedClipboardIds, item => item.Id);
            if (remote.Count == 0)
            {
                var snapshot = Items.ToList();
                if (IsCurrentSession(userId, token)) await Cloud.PushAsync(token, snapshot);
            }
            else
            {
                var remoteIds = remote
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                    .Select(item => item.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var localPendingSync = Items.Any(item =>
                    CloudSyncService.IsSyncable(item) && !remoteIds.Contains(item.Id));
                var localRichText = Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.RtfPath) && File.Exists(item.RtfPath))
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().RtfPath!, StringComparer.Ordinal);
                var localFiles = Items
                    .Where(item => item.FileUrls is { Count: > 0 })
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().FileUrls
                            .Where(path => File.Exists(path) || Directory.Exists(path))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        StringComparer.Ordinal);
                Items = ClipboardSyncMerge.MergeByContent(
                    Items,
                    remote,
                    item => item.Id,
                    item => CloudSyncService.IsSyncable(item)
                        ? ClipboardSyncPolicy.ContentKey(item.Text, item.Html)
                        : null,
                    (remoteItem, localItem) =>
                    {
                        // RTF is intentionally local-only. Keep the existing file when
                        // the same cloud record is refreshed, without uploading its path.
                        if (localRichText.TryGetValue(remoteItem.Id, out var rtfPath)) remoteItem.RtfPath = rtfPath;
                        // File and directory clipboard entries follow the same rule:
                        // their text can sync, but usable local StorageItems stay local.
                        if (localFiles.TryGetValue(remoteItem.Id, out var fileUrls)) remoteItem.FileUrls = fileUrls;
                        return remoteItem;
                    },
                    (preferred, duplicate) =>
                    {
                        // Different devices may assign different IDs to the same
                        // copied text/URL. Keep the cloud record's ID as canonical,
                        // while retaining resources and additive local flags.
                        if (string.IsNullOrWhiteSpace(preferred.Title)) preferred.Title = duplicate.Title;
                        if (string.IsNullOrWhiteSpace(preferred.Note)) preferred.Note = duplicate.Note;
                        preferred.Favorite |= duplicate.Favorite;
                        preferred.Pinned |= duplicate.Pinned;
                        if (string.IsNullOrWhiteSpace(preferred.RtfPath)) preferred.RtfPath = duplicate.RtfPath;
                        if (preferred.FileUrls.Count == 0) preferred.FileUrls = duplicate.FileUrls.ToList();
                        return preferred;
                    })
                    .ToList();
                TrimClipboardHistory();
                await Data.SaveClipboardAsync(Items);
                _clipboardPage?.Refresh();
                if (localPendingSync && IsCurrentSession(userId, token))
                    await Cloud.PushAsync(token, Items.ToList());
            }
            SetCloudSyncStatus("");
        }
        catch (CloudSyncService.CloudSyncUnauthorizedException)
        {
            await HandleCloudSessionExpiredAsync(userId, token);
        }
        catch { SetCloudSyncStatus("云端同步暂不可用，本机记录已保留。"); }
        finally { _cloudSyncGate.Release(); }
    }

    public async Task PushClipboardAsync()
    {
        var userId = Auth.CurrentUser?.Id;
        var token = Auth.Token;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)) return;
        await PushClipboardAsync(userId, token);
    }

    private async Task PushClipboardAsync(string userId, string token)
    {
        await _cloudSyncGate.WaitAsync();
        try
        {
            if (IsCurrentSession(userId, token)) await Cloud.PushAsync(token, Items.ToList());
        }
        catch (CloudSyncService.CloudSyncUnauthorizedException)
        {
            await HandleCloudSessionExpiredAsync(userId, token);
        }
        catch { SetCloudSyncStatus("云端同步暂不可用，本机记录已保留。"); }
        finally { _cloudSyncGate.Release(); }
    }

    private void SetCloudSyncStatus(string status)
    {
        if (string.Equals(CloudSyncStatus, status, StringComparison.Ordinal)) return;
        CloudSyncStatus = status;
        ClipboardStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartCloudSyncLoop(string userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        StopCloudSyncLoop();
        var cancellation = new CancellationTokenSource();
        _cloudSyncLoop = cancellation;
        _ = RunCloudSyncLoopAsync(userId, token, cancellation);
    }

    private async Task RunCloudSyncLoopAsync(string userId, string token, CancellationTokenSource cancellation)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(cancellation.Token))
            {
                if (!IsCurrentSession(userId, token)) break;
                await PullClipboardAsync();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.CompareExchange(ref _cloudSyncLoop, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void StopCloudSyncLoop()
    {
        Interlocked.Exchange(ref _cloudSyncLoop, null)?.Cancel();
    }

    private void SchedulePushClipboard()
    {
        var userId = Auth.CurrentUser?.Id;
        var token = Auth.Token;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)) return;

        var next = new CancellationTokenSource();
        Interlocked.Exchange(ref _cloudPushDelay, next)?.Cancel();
        _ = DebouncedPushClipboardAsync(next, userId, token);
    }

    private async Task DebouncedPushClipboardAsync(CancellationTokenSource delay, string userId, string token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(800), delay.Token);
            await PushClipboardAsync(userId, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.CompareExchange(ref _cloudPushDelay, null, delay);
            delay.Dispose();
        }
    }

    private async Task HandleCloudSessionExpiredAsync(string userId, string token)
    {
        if (!IsCurrentSession(userId, token) || Interlocked.Exchange(ref _sessionExpiryHandling, 1) != 0)
            return;

        async Task ExpireOnUiAsync()
        {
            if (!IsCurrentSession(userId, token)) return;
            Auth.ExpireSession();
            LogoutToAuth();
            await Task.CompletedTask;
        }

        if (DispatcherQueue.HasThreadAccess)
            await ExpireOnUiAsync();
        else
            await DispatcherQueue.EnqueueAsync(ExpireOnUiAsync);
    }

    private bool IsCurrentSession(string userId, string token) =>
        Auth.CurrentUser?.Id == userId && Data.UserId == userId && string.Equals(Auth.Token, token, StringComparison.Ordinal);

    public async Task ClearCalculatorHistoryAsync()
    {
        CalculatorHistory.Clear();
        await Data.SaveCalculatorHistoryAsync(CalculatorHistory);
    }

    public async Task AddCalculatorHistoryAsync(string value)
    {
        CalculatorHistory.Insert(0, value);
        if (CalculatorHistory.Count > 30) CalculatorHistory = CalculatorHistory.Take(30).ToList();
        await Data.SaveCalculatorHistoryAsync(CalculatorHistory);
    }

    private List<ShortcutItem> LoadShortcuts()
    {
        try
        {
            var raw = Setting("shortcuts", "[]");
            return JsonSerializer.Deserialize<List<ShortcutItem>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void SaveShortcuts()
    {
        SetSetting("shortcuts", JsonSerializer.Serialize(Shortcuts));
    }

    public void SetSetting(string key, object value)
    {
        var storageKey = UserSettingsPolicy.StorageKey(key, Auth.CurrentUser?.Id);
        if (storageKey is null) return;
        ApplicationData.Current.LocalSettings.Values[storageKey] = value;
    }

    private static void MigrateLegacyUserSettings(string userId)
    {
        UserSettingsPolicy.MigrateLegacyValues(ApplicationData.Current.LocalSettings.Values, userId);
    }

    public static string NormalizeDownloadBrowser(string? value) => DownloadBrowserPolicy.Normalize(value);

    public string Setting(string key, string fallback)
    {
        var storageKey = UserSettingsPolicy.StorageKey(key, Auth.CurrentUser?.Id);
        if (storageKey is null) return fallback;
        var value = ApplicationData.Current.LocalSettings.Values[storageKey]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public bool GetBoolSetting(string key, bool fallback)
    {
        var storageKey = UserSettingsPolicy.StorageKey(key, Auth.CurrentUser?.Id);
        if (storageKey is null) return fallback;
        var value = ApplicationData.Current.LocalSettings.Values[storageKey];
        return value is bool boolean ? boolean : fallback;
    }

    public void LogoutToAuth()
    {
        CloseSettingsDialog();
        Interlocked.Exchange(ref _sessionExpiryHandling, 0);
        Interlocked.Exchange(ref _cloudPushDelay, null)?.Cancel();
        StopCloudSyncLoop();
        StopClipboardMonitor();
        _navigation = null;
        _frame = null;
        _clipboardPage = null;
        _qaPage = null;
        _downloadsPage?.CancelAll();
        _downloadsPage = null;
        Items.Clear();
        DeletedClipboardIds.Clear();
        QaEntries.Clear();
        Shortcuts.Clear();
        CalculatorHistory.Clear();
        DownloadTasks.Clear();
        DownloadDirectory = null;
        DownloadBrowser = "none";
        DownloadConcurrency = 2;
        DownloadGate.SetLimit(DownloadConcurrency);
        TransferReceiveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "KuaiJian Received");
        ClipboardStatus = "";
        CloudSyncStatus = "";
        AvailableUpdate = null;
        _settingsDialogOpen = false;
        Data.SetUser(null);
        Content = new AuthPage(Auth, ShowMainInterface);
    }

    public void Shutdown()
    {
        Interlocked.Exchange(ref _cloudPushDelay, null)?.Cancel();
        StopCloudSyncLoop();
        StopClipboardMonitor();
        _downloadsPage?.CancelAll();
    }

    private static new StackPanel Loading(string text) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { new ProgressRing { IsActive = true }, new TextBlock { Text = text, Margin = new Thickness(0, 14, 0, 0) } }
    };

    public static StackPanel Placeholder(string title, string message, Symbol symbol = Symbol.People) => new()
    {
        Spacing = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new SymbolIcon { Symbol = symbol, Foreground = (Brush)Application.Current.Resources["MintBrush"] },
            new TextBlock { Text = title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = message, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center }
        }
    };
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue queue, Func<Task> callback)
    {
        var tcs = new TaskCompletionSource<object?>();
        queue.TryEnqueue(async () =>
        {
            try { await callback(); tcs.SetResult(null); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
