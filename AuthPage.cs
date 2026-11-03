using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KuaiJian.WinUI;

public sealed class AuthPage : Page
{
    private readonly AuthService _auth;
    private readonly Action _onSuccess;
    private readonly Grid _root = new();
    private TextBlock _message = new();
    private TextBox _phone = new() { PlaceholderText = "请输入手机号" };
    private TextBox _email = new() { PlaceholderText = "用于找回密码（可选）" };
    private PasswordBox _password = new() { PlaceholderText = "请输入密码", PasswordRevealMode = PasswordRevealMode.Peek };
    private PasswordBox _confirmation = new() { PlaceholderText = "再次输入密码", PasswordRevealMode = PasswordRevealMode.Peek };
    private TextBox _code = new() { PlaceholderText = "请输入 6 位验证码" };
    private TextBox _recoveryPhone = new() { PlaceholderText = "注册时使用的手机号" };
    private TextBox _recoveryCode = new() { PlaceholderText = "请输入 6 位验证码" };
    private PasswordBox _recoveryPassword = new() { PlaceholderText = "至少 6 位", PasswordRevealMode = PasswordRevealMode.Peek };
    private PasswordBox _recoveryConfirmation = new() { PlaceholderText = "再次输入新密码", PasswordRevealMode = PasswordRevealMode.Peek };
    private Button? _primary;
    private Button? _sendCode;
    private Button? _recoverySendCode;
    private string _primaryLabel = "登录";
    private bool _register;
    private bool _passwordLogin;
    private bool _passwordVisible;

    public AuthPage(AuthService auth, Action onSuccess)
    {
        _auth = auth;
        _onSuccess = onSuccess;
        _auth.StateChanged += AuthChanged;
        Unloaded += (_, _) => _auth.StateChanged -= AuthChanged;
        Build();
    }

    private void Build()
    {
        var background = new Border { Background = (Brush)Application.Current.Resources["CanvasBrush"] };
        _root.HorizontalAlignment = HorizontalAlignment.Center;
        _root.VerticalAlignment = VerticalAlignment.Top;
        background.Child = _root;
        Content = background;
        ShowLogin();
    }

    private void ShowLogin()
    {
        _register = false;
        RenderAuth("登录快剪", "同步和管理你的剪贴板", BuildLoginForm());
    }

    private void ShowRegister()
    {
        _register = true;
        RenderAuth("注册快剪", "创建账号，开始保存你的数据", BuildRegisterForm());
    }

    private void ShowRecovery()
    {
        RenderAuth("找回快剪密码", "使用手机号短信验证码验证身份", BuildRecoveryForm());
    }

    private void RenderAuth(string title, string subtitle, UIElement form)
    {
        _root.Children.Clear();
        var column = new StackPanel { Width = 420, Padding = new Thickness(30, 38, 30, 30), Spacing = 12 };
        var iconFallback = new TextBlock
        {
            Text = "剪",
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        try
        {
            icon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/KuaiJian.ico"));
            icon.ImageFailed += (_, _) => icon.Visibility = Visibility.Collapsed;
        }
        catch
        {
            icon.Visibility = Visibility.Collapsed;
        }
        column.Children.Add(new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(32),
            Background = (Brush)Application.Current.Resources["MintBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Grid { Children = { iconFallback, icon } }
        });
        column.Children.Add(new TextBlock { Text = "快剪", FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["InkBrush"], HorizontalAlignment = HorizontalAlignment.Center });
        column.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["InkBrush"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        column.Children.Add(new TextBlock { Text = subtitle, Foreground = (Brush)Application.Current.Resources["MutedBrush"], HorizontalAlignment = HorizontalAlignment.Center });
        column.Children.Add(form);
        _root.Children.Add(new ScrollViewer
        {
            Content = column,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        });
    }

    private UIElement BuildLoginForm()
    {
        var phone = _phone.Text;
        var password = _password.Password;
        var code = _code.Text;
        _phone = new TextBox { PlaceholderText = "请输入手机号", Text = phone };
        _password = new PasswordBox
        {
            PlaceholderText = "请输入密码",
            PasswordRevealMode = _passwordVisible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _password.Password = password;
        _code = new TextBox { PlaceholderText = "请输入 6 位验证码", Text = code };
        _message = CreateMessage();
        _sendCode = null;
        _recoverySendCode = null;
        var column = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var modes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
        modes.Children.Add(Button("登录", (_, _) => ShowLogin(), _register ? null : "selected"));
        modes.Children.Add(Button("注册", (_, _) => { _register = true; ShowRegister(); }, _register ? "selected" : null));
        column.Children.Add(modes);
        column.Children.Add(Label("手机号"));
        column.Children.Add(_phone);

        var methods = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        methods.Children.Add(Button("验证码登录", (_, _) => { _passwordLogin = false; ShowLogin(); }, !_passwordLogin ? "selected" : null));
        methods.Children.Add(Button("密码登录", (_, _) => { _passwordLogin = true; ShowLogin(); }, _passwordLogin ? "selected" : null));
        column.Children.Add(methods);

        if (_passwordLogin)
        {
            column.Children.Add(Label("密码"));
            var passwordRow = new Grid { ColumnSpacing = 6 };
            passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            passwordRow.Children.Add(_password);
            var visibility = new Button
            {
                Content = new SymbolIcon(_passwordVisible ? Symbol.HideBcc : Symbol.View),
                Width = 40,
                MinWidth = 40,
                Padding = new Thickness(0)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                visibility, _passwordVisible ? "隐藏密码" : "显示密码");
            ToolTipService.SetToolTip(visibility, _passwordVisible ? "隐藏密码" : "显示密码");
            visibility.Click += (_, _) =>
            {
                _passwordVisible = PasswordVisibilityPolicy.Toggle(_passwordVisible);
                _password.PasswordRevealMode = _passwordVisible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
                visibility.Content = new SymbolIcon(_passwordVisible ? Symbol.HideBcc : Symbol.View);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                    visibility, _passwordVisible ? "隐藏密码" : "显示密码");
                ToolTipService.SetToolTip(visibility, _passwordVisible ? "隐藏密码" : "显示密码");
            };
            Grid.SetColumn(visibility, 1);
            passwordRow.Children.Add(visibility);
            column.Children.Add(passwordRow);
            var forgot = Button("忘记密码?", (_, _) => ShowRecovery());
            forgot.HorizontalAlignment = HorizontalAlignment.Right;
            column.Children.Add(forgot);
        }
        else
        {
            column.Children.Add(Label("验证码"));
            _sendCode = new Button { Content = "获取验证码", Width = 110 };
            _sendCode.Click += async (_, _) => await SendCodeAsync("login");
            column.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _code, _sendCode } });
        }
        _primaryLabel = "登录";
        _primary = Button(_primaryLabel, async (_, _) => await LoginAsync());
        column.Children.Add(_primary);
        column.Children.Add(_message);
        column.Children.Add(new TextBlock { Text = "邮箱为可选项；登录支持短信验证码或手机号密码。", FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
        return column;
    }

    private UIElement BuildRegisterForm()
    {
        var phone = _phone.Text;
        var email = _email.Text;
        var code = _code.Text;
        var password = _password.Password;
        var confirmation = _confirmation.Password;
        _phone = new TextBox { PlaceholderText = "请输入手机号", Text = phone };
        _email = new TextBox { PlaceholderText = "用于找回密码（可选）", Text = email };
        _code = new TextBox { PlaceholderText = "请输入 6 位验证码", Text = code };
        _password = new PasswordBox { PlaceholderText = "请输入密码", PasswordRevealMode = PasswordRevealMode.Peek };
        _password.Password = password;
        _confirmation = new PasswordBox { PlaceholderText = "再次输入密码", PasswordRevealMode = PasswordRevealMode.Peek };
        _confirmation.Password = confirmation;
        _message = CreateMessage();
        _sendCode = null;
        _recoverySendCode = null;
        var column = new StackPanel { Spacing = 9, Margin = new Thickness(0, 16, 0, 0) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(Button("登录", (_, _) => ShowLogin(), !_register ? "selected" : null));
        top.Children.Add(Button("注册", (_, _) => ShowRegister(), _register ? "selected" : null));
        column.Children.Add(top);
        column.Children.Add(Label("手机号")); column.Children.Add(_phone);
        column.Children.Add(Label("邮箱（可选）")); column.Children.Add(_email);
        column.Children.Add(Label("短信验证码"));
        _sendCode = new Button { Content = "获取验证码", Width = 110 };
        _sendCode.Click += async (_, _) => await SendCodeAsync("register");
        column.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _code, _sendCode } });
        column.Children.Add(Label("密码")); column.Children.Add(_password);
        column.Children.Add(Label("确认密码")); column.Children.Add(_confirmation);
        _primaryLabel = "注册";
        _primary = Button(_primaryLabel, async (_, _) => await RegisterAsync());
        column.Children.Add(_primary);
        column.Children.Add(_message);
        return column;
    }

    private UIElement BuildRecoveryForm()
    {
        var recoveryPhone = _phone.Text;
        var recoveryCode = _recoveryCode.Text;
        var recoveryPassword = _recoveryPassword.Password;
        var recoveryConfirmation = _recoveryConfirmation.Password;
        _recoveryPhone = new TextBox { PlaceholderText = "注册时使用的手机号", Text = recoveryPhone };
        _recoveryCode = new TextBox { PlaceholderText = "请输入 6 位验证码", Text = recoveryCode };
        _recoveryPassword = new PasswordBox { PlaceholderText = "至少 6 位", PasswordRevealMode = PasswordRevealMode.Peek };
        _recoveryPassword.Password = recoveryPassword;
        _recoveryConfirmation = new PasswordBox { PlaceholderText = "再次输入新密码", PasswordRevealMode = PasswordRevealMode.Peek };
        _recoveryConfirmation.Password = recoveryConfirmation;
        _message = CreateMessage();
        _sendCode = null;
        _recoverySendCode = null;
        var column = new StackPanel { Spacing = 9, Margin = new Thickness(0, 16, 0, 0) };
        column.Children.Add(Label("手机号")); column.Children.Add(_recoveryPhone);
        column.Children.Add(Label("短信验证码"));
        _recoverySendCode = new Button { Content = "获取验证码", Width = 110 };
        _recoverySendCode.Click += async (_, _) => await SendCodeAsync("reset-password");
        column.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _recoveryCode, _recoverySendCode } });
        column.Children.Add(Label("新密码")); column.Children.Add(_recoveryPassword);
        column.Children.Add(Label("确认新密码")); column.Children.Add(_recoveryConfirmation);
        _primaryLabel = "重置密码";
        _primary = Button(_primaryLabel, async (_, _) =>
        {
            var success = await _auth.ResetPasswordAsync(_recoveryPhone.Text, _recoveryCode.Text, _recoveryPassword.Password, _recoveryConfirmation.Password);
            if (success)
            {
                _code.Text = "";
                _recoveryCode.Text = "";
                _password.Password = "";
                _confirmation.Password = "";
                _recoveryPassword.Password = "";
                _recoveryConfirmation.Password = "";
                _phone.Text = _recoveryPhone.Text;
                _passwordLogin = true;
                ShowLogin();
            }
        });
        column.Children.Add(_primary);
        var back = Button("返回登录", (_, _) => ShowLogin());
        back.HorizontalAlignment = HorizontalAlignment.Right;
        column.Children.Add(back);
        column.Children.Add(_message);
        return column;
    }

    private async Task LoginAsync()
    {
        var success = await _auth.LoginAsync(_phone.Text, _passwordLogin ? _password.Password : null, _passwordLogin ? null : _code.Text);
        if (success) _onSuccess();
    }

    private async Task RegisterAsync()
    {
        var success = await _auth.RegisterAsync(_phone.Text, _email.Text, _code.Text, _password.Password, _confirmation.Password);
        if (success)
        {
            _code.Text = "";
            _password.Password = "";
            _confirmation.Password = "";
            _email.Text = "";
            _passwordLogin = false;
            ShowLogin();
        }
    }

    private async Task SendCodeAsync(string purpose)
    {
        var phone = purpose == "reset-password" ? _recoveryPhone.Text : _phone.Text;
        await _auth.SendCodeAsync(phone, purpose);
    }

    private void AuthChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _message.Text = _auth.Message;
            _message.Foreground = _auth.Message.Contains("成功") ? (Brush)Application.Current.Resources["MintBrush"] : new SolidColorBrush(Microsoft.UI.Colors.DarkOrange);
            if (_primary is not null)
            {
                _primary.IsEnabled = !_auth.IsBusy;
                _primary.Content = _auth.IsBusy ? "处理中…" : _primaryLabel;
            }
            if (_sendCode is not null)
            {
                _sendCode.IsEnabled = !_auth.IsBusy && _auth.SmsCooldownRemaining == 0;
                _sendCode.Content = _auth.IsBusy ? "处理中…" : _auth.SmsCooldownRemaining > 0 ? $"{_auth.SmsCooldownRemaining}s" : "获取验证码";
            }
            if (_recoverySendCode is not null)
            {
                _recoverySendCode.IsEnabled = !_auth.IsBusy && _auth.SmsCooldownRemaining == 0;
                _recoverySendCode.Content = _auth.IsBusy ? "处理中…" : _auth.SmsCooldownRemaining > 0 ? $"{_auth.SmsCooldownRemaining}s" : "获取验证码";
            }
        });
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["InkBrush"], Margin = new Thickness(0, 5, 0, -4) };

    private TextBlock CreateMessage() => new()
    {
        Text = _auth.Message,
        Foreground = _auth.Message.Contains("成功")
            ? (Brush)Application.Current.Resources["MintBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.DarkOrange),
        TextWrapping = TextWrapping.Wrap
    };

    private static Button Button(string text, RoutedEventHandler handler, string? state = null)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 90 };
        if (state == "selected") button.Background = (Brush)Application.Current.Resources["MintBrush"];
        button.Click += handler;
        return button;
    }
}
