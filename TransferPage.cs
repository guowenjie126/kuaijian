using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KuaiJian.WinUI;

public sealed class TransferPage : Page
{
    private readonly RootPage _root;
    private readonly TextBlock _receivePath = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["MutedBrush"]
    };
    private readonly TextBlock _status = new()
    {
        Text = "互传中继服务尚未接入。设备绑定、文件发送和接收目前不可用。",
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange)
    };

    public TransferPage(RootPage root)
    {
        _root = root;
        Build();
    }

    private void Build()
    {
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = "互传", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        content.Children.Add(new TextBlock
        {
            Text = "在已绑定的电脑之间安全传送文件。",
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        });
        content.Children.Add(new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 248, 232)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = _status
        });

        var binding = Section("绑定电脑", "使用另一台设备上显示的 6 位绑定码完成配对。");
        var bindingRow = new Grid { ColumnSpacing = 8 };
        bindingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bindingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bindingRow.Children.Add(new TextBox
        {
            PlaceholderText = "6 位绑定码",
            MaxLength = 6,
            IsEnabled = false
        });
        var bind = new Button { Content = "绑定", IsEnabled = false };
        Grid.SetColumn(bind, 1); bindingRow.Children.Add(bind);
        binding.Children.Add(bindingRow);
        content.Children.Add(Card(binding));

        var receive = Section("接收文件夹", "接收到的文件将保存到此目录；成功写入后才会确认接收。");
        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _receivePath.Text = _root.TransferReceiveDirectory;
        pathRow.Children.Add(_receivePath);
        var choose = new Button { Content = "选择文件夹" };
        choose.Click += async (_, _) => await ChooseReceiveFolderAsync();
        Grid.SetColumn(choose, 1); pathRow.Children.Add(choose);
        receive.Children.Add(pathRow);
        content.Children.Add(Card(receive));

        var devices = Section("已绑定设备", "连接互传服务后，此处显示真实设备、在线状态和最后在线时间。");
        devices.Children.Add(EmptyState("暂无设备信息"));
        content.Children.Add(Card(devices));

        var transfers = Section("传输队列", "文件会分块传输并在完成后校验 SHA-256；接口接入前不会显示模拟任务。");
        transfers.Children.Add(EmptyState("暂无传输任务"));
        content.Children.Add(Card(transfers));

        var inbox = Section("接收箱", "下载并成功保存文件后才确认接收，避免失败时清除服务端文件。");
        inbox.Children.Add(EmptyState("暂无待接收文件"));
        content.Children.Add(Card(inbox));

        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private async Task ChooseReceiveFolderAsync()
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, _root.Window.Hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            _root.TransferReceiveDirectory = folder.Path;
            _root.SetSetting("transferReceiveDirectory", folder.Path);
            _receivePath.Text = folder.Path;
            _status.Text = "接收文件夹已保存。互传服务接口接入后即可用于接收文件。";
        }
        catch (Exception ex)
        {
            _status.Text = "选择接收文件夹失败：" + ex.Message;
        }
    }

    private static StackPanel Section(string title, string description)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        return panel;
    }

    private static Border Card(UIElement content) => new()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.White),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16),
        Child = content
    };

    private static Border EmptyState(string text) => new()
    {
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 246, 249, 247)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14),
        Child = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"]
        }
    };
}
