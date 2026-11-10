using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Diagnostics;

namespace Yuhui;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly LocationWeatherService _locationWeather = new();
    private readonly MediaPlayer _localPlayer = new();
    private readonly Dictionary<string, (double Latitude, double Longitude)> _cities = new()
    {
        ["杭州"] = (30.2741, 120.1551), ["上海"] = (31.2304, 121.4737),
        ["北京"] = (39.9042, 116.4074), ["成都"] = (30.5728, 104.0668)
    };
    private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "Yuhui";
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20L;
    private (double Latitude, double Longitude)? _automaticPosition;
    private string? _automaticCity;
    private DateTime? _weatherSunset;
    private string _weatherNote = "离线计算今日的太阳时间";
    private bool _edgeHidden;
    private DateTime _lastHourlyPeek = DateTime.MinValue;
    private string? _localAudioPath;
    private string? _builtInAudioPath;
    private BuiltInLofiPreset? _builtInPreset = BuiltInLofiLibrary.Presets.First();
    private bool _isPlayingLocal;
    private bool _restoringPreferences;

    public bool IsMouseThrough { get; private set; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateSunState();
        _hideTimer.Tick += (_, _) => CollapseToEdge();
        _peekTimer.Tick += (_, _) => CollapseToEdge();
        _localPlayer.MediaEnded += (_, _) =>
        {
            _localPlayer.Position = TimeSpan.Zero;
            _localPlayer.Play();
        };
        _localPlayer.MediaFailed += (_, _) =>
        {
            _isPlayingLocal = false;
            LofiButton.Content = "▶";
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RestorePreferences();
        StartupCheck.IsChecked = HasStartupRegistration();
        Opacity = OpacitySlider.Value / 100d;
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        PinToEdge();
        UpdateSunState();
        _timer.Start();
        if (AutoLocationCheck.IsChecked == true) _ = RefreshAutomaticLocationAsync();
    }

    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); PinToEdge(); }
    }

    public void PinToEdge()
    {
        _edgeHidden = false;
        EdgeProgressRail.Visibility = Visibility.Collapsed;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth + 8;
        var preferredTop = workArea.Top + Math.Max(88, workArea.Height * .19);
        Top = Math.Min(preferredTop, workArea.Bottom - ActualHeight - 8);
    }

    public void SetMouseThrough(bool enabled)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetMouseThrough(enabled)); return; }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var updated = enabled ? current | WsExTransparent : current & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(updated));
        IsMouseThrough = enabled;
        if (MouseThroughCheck.IsChecked != enabled) MouseThroughCheck.IsChecked = enabled;
        if (!_restoringPreferences) SavePreferences();
    }

    private void UpdateSunState()
    {
        var selectedCity = ((ComboBoxItem)CitySelector.SelectedItem).Content.ToString()!;
        var city = _automaticCity ?? selectedCity;
        var point = _automaticPosition ?? _cities[selectedCity];
        var now = DateTime.Now;
        var (sunrise, calculatedSunset) = SolarCalculator.GetSunTimes(now.Date, point.Latitude, point.Longitude, TimeZoneInfo.Local);
        var sunset = _weatherSunset?.Date == now.Date ? _weatherSunset.Value : calculatedSunset;
        var fraction = Math.Clamp((now - sunrise).TotalMinutes / (sunset - sunrise).TotalMinutes, 0d, 1d);
        var stage = fraction switch { < .18 => "清晨", < .64 => "下午", < .89 => "黄金时刻", _ => "蓝调时刻" };
        var remaining = sunset - now;
        ClockText.Text = now.ToString("HH:mm");
        CityText.Text = $"余晖 · {city}";
        StageText.Text = stage;
        WeatherText.Text = _weatherNote;
        RemainingText.Text = remaining > TimeSpan.Zero ? $"距日落 {remaining.Hours}小时{remaining.Minutes:D2}分" : "夜晚已经开始";
        SunriseText.Text = sunrise.ToString("HH:mm");
        SunsetText.Text = sunset.ToString("HH:mm");
        ProgressText.Text = $"白昼 {fraction:P0}";
        UpdateArc(fraction);
        RootCard.Background = CreateBackground(fraction);
        CheckHourlyPeek(now);
    }

    private void UpdateArc(double t)
    {
        var total = 315d;
        ArcProgress.StrokeDashArray = new DoubleCollection { total, total };
        ArcProgress.StrokeDashOffset = total * (1 - t);
        var x = (1-t)*(1-t)*12 + 2*(1-t)*t*135 + t*t*258;
        var y = (1-t)*(1-t)*97 + 2*(1-t)*t*(-12) + t*t*97;
        Canvas.SetLeft(SunMarker, x - 15);
        Canvas.SetTop(SunMarker, y - 15);
        EdgeProgressFill.Height = 92 * t;
    }

    private static System.Windows.Media.Brush CreateBackground(double fraction)
    {
        var late = Math.Clamp((fraction - .55) / .45, 0d, 1d);
        byte Mix(byte a, byte b) => (byte)(a + (b - a) * late);
        return new LinearGradientBrush(System.Windows.Media.Color.FromRgb(Mix(245, 252), Mix(255, 236), Mix(252, 220)), System.Windows.Media.Color.FromRgb(Mix(232, 235), Mix(245, 224), Mix(237, 202)), 35);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandFromEdge();
        SettingsPanel.Visibility = Visibility.Visible;
        Height = Math.Min(520, SystemParameters.WorkArea.Height - 24);
        PinToEdge();
    }

    private async void LofiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlayingLocal)
        {
            _localPlayer.Pause();
            _isPlayingLocal = false;
            LofiButton.Content = "▶";
            return;
        }

        if (string.IsNullOrWhiteSpace(_localAudioPath) && string.IsNullOrWhiteSpace(_builtInAudioPath) && _builtInPreset is not null)
        {
            LofiButton.Content = "…";
            _builtInAudioPath = await Task.Run(() => BuiltInLofiLibrary.EnsureTrack(_builtInPreset));
            _localPlayer.Open(new Uri(_builtInAudioPath));
        }

        if (string.IsNullOrWhiteSpace(_localAudioPath) && string.IsNullOrWhiteSpace(_builtInAudioPath))
        {
            Process.Start(new ProcessStartInfo("https://www.lofi.cafe/") { UseShellExecute = true });
            return;
        }

        _localPlayer.Play();
        _isPlayingLocal = true;
        LofiButton.Content = "Ⅱ";
    }

    private void LofiButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var presets = BuiltInLofiLibrary.Presets;
        var current = _builtInPreset is null ? -1 : presets.ToList().FindIndex(preset => preset.Id == _builtInPreset.Id);
        var step = e.Delta > 0 ? -1 : 1;
        var next = current < 0 ? (step > 0 ? 0 : presets.Count - 1) : (current + step + presets.Count) % presets.Count;
        var wasPlaying = _isPlayingLocal;
        ChannelSelector.SelectedItem = ChannelSelector.Items.OfType<ComboBoxItem>()
            .First(item => item.Tag?.ToString() == presets[next].Id);
        if (wasPlaying) LofiButton_Click(LofiButton, new RoutedEventArgs());
    }

    private void ChooseLocalAudio_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择一段本地 Lofi 音频",
            Filter = "音频文件|*.mp3;*.wav;*.wma;*.m4a;*.aac|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;

        _localPlayer.Stop();
        _localPlayer.Open(new Uri(picker.FileName));
        _localAudioPath = picker.FileName;
        _builtInAudioPath = null;
        _builtInPreset = null;
        _isPlayingLocal = false;
        LofiButton.Content = "▶";
        ChannelSelector.SelectedItem = ChannelSelector.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "local");
        SavePreferences();
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        Height = 306;
        PinToEdge();
    }

    private void CitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SavePreferences();
        UpdateSunState();
    }

    private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _restoringPreferences) return;
        var channel = SelectedChannelId();
        _localPlayer.Stop();
        _isPlayingLocal = false;
        LofiButton.Content = "▶";
        _builtInAudioPath = null;

        if (channel == "online")
        {
            _localAudioPath = null;
            _builtInPreset = null;
        }
        else if (channel == "local")
        {
            _builtInPreset = string.IsNullOrWhiteSpace(_localAudioPath) ? null : _builtInPreset;
        }
        else if (channel != "local")
        {
            _localAudioPath = null;
            _builtInPreset = BuiltInLofiLibrary.Presets.First(preset => preset.Id == channel);
        }
        SavePreferences();
    }

    private void TopmostCheck_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostCheck.IsChecked == true;
        SavePreferences();
    }

    private void MouseThroughCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_restoringPreferences) return;
        SetMouseThrough(MouseThroughCheck.IsChecked == true);
    }
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        Opacity = OpacitySlider.Value / 100d;
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        SavePreferences();
    }
    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hideTimer.Stop();
        if (_edgeHidden) ExpandFromEdge();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (AutoHideCheck.IsChecked == true && SettingsPanel.Visibility != Visibility.Visible) _hideTimer.Start();
    }

    private void AutoHideCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (AutoHideCheck.IsChecked == true) _hideTimer.Start();
        else { _hideTimer.Stop(); ExpandFromEdge(); }
        SavePreferences();
    }

    private void HourlyPeekCheck_Changed(object sender, RoutedEventArgs e) => SavePreferences();

    private async void AutoLocationCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _restoringPreferences) return;
        SavePreferences();
        if (AutoLocationCheck.IsChecked == true) await RefreshAutomaticLocationAsync();
        else
        {
            _automaticPosition = null;
            _automaticCity = null;
            _weatherSunset = null;
            _weatherNote = "离线计算今日的太阳时间";
            CitySelector.IsEnabled = true;
            LocationHintText.Text = "已使用手动城市与离线太阳计算。";
            UpdateSunState();
        }
    }

    private async Task RefreshAutomaticLocationAsync()
    {
        CitySelector.IsEnabled = false;
        LocationHintText.Text = "正在读取位置与天气…";
        try
        {
            var outdoor = await _locationWeather.GetOutdoorContextAsync(CancellationToken.None);
            if (AutoLocationCheck.IsChecked != true) return;
            _automaticPosition = (outdoor.Latitude, outdoor.Longitude);
            _automaticCity = outdoor.City;
            _weatherSunset = outdoor.Sunset;
            _weatherNote = outdoor.WeatherNote;
            LocationHintText.Text = $"已定位到 {outdoor.City}，并已同步今日天气。";
        }
        catch
        {
            _automaticPosition = null;
            _automaticCity = null;
            _weatherSunset = null;
            _weatherNote = "天气暂不可用，使用离线太阳计算";
            LocationHintText.Text = "自动位置暂不可用，已临时使用手动城市。";
            CitySelector.IsEnabled = true;
        }
        UpdateSunState();
    }

    private void RestorePreferences()
    {
        var preferences = PreferencesStore.Load();
        _restoringPreferences = true;
        AutoLocationCheck.IsChecked = preferences.AutoLocation;
        CitySelector.SelectedItem = CitySelector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Content?.ToString() == preferences.City) ?? CitySelector.Items[0];
        ChannelSelector.SelectedItem = ChannelSelector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == preferences.Channel)
            ?? ChannelSelector.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "chillhop");
        TopmostCheck.IsChecked = preferences.Topmost;
        AutoHideCheck.IsChecked = preferences.AutoHide;
        HourlyPeekCheck.IsChecked = preferences.HourlyPeek;
        MouseThroughCheck.IsChecked = preferences.MouseThrough;
        OpacitySlider.Value = Math.Clamp(preferences.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        _restoringPreferences = false;

        var channel = SelectedChannelId();
        if (channel == "local" && !string.IsNullOrWhiteSpace(preferences.LocalAudioPath) && System.IO.File.Exists(preferences.LocalAudioPath))
        {
            _localAudioPath = preferences.LocalAudioPath;
            _localPlayer.Open(new Uri(_localAudioPath));
        }
        else
        {
            _builtInPreset = BuiltInLofiLibrary.Presets.FirstOrDefault(preset => preset.Id == channel)
                ?? BuiltInLofiLibrary.Presets.First();
            if (channel == "local") ChannelSelector.SelectedItem = ChannelSelector.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "chillhop");
        }
        Topmost = preferences.Topmost;
        CitySelector.IsEnabled = !preferences.AutoLocation;
        SetMouseThrough(preferences.MouseThrough);
    }

    private void SavePreferences()
    {
        if (!IsLoaded || _restoringPreferences) return;
        var city = ((ComboBoxItem)CitySelector.SelectedItem).Content?.ToString() ?? "杭州";
        new PreferencesStore
        {
            AutoLocation = AutoLocationCheck.IsChecked == true,
            City = city,
            Topmost = TopmostCheck.IsChecked == true,
            AutoHide = AutoHideCheck.IsChecked == true,
            HourlyPeek = HourlyPeekCheck.IsChecked == true,
            MouseThrough = MouseThroughCheck.IsChecked == true,
            Opacity = OpacitySlider.Value,
            Channel = SelectedChannelId(),
            LocalAudioPath = _localAudioPath
        }.Save();
    }

    private string SelectedChannelId() => (ChannelSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "chillhop";

    private void ExpandFromEdge()
    {
        _hideTimer.Stop();
        _peekTimer.Stop();
        if (!_edgeHidden) return;
        _edgeHidden = false;
        EdgeProgressRail.Visibility = Visibility.Collapsed;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth + 8;
    }

    private void CollapseToEdge()
    {
        _hideTimer.Stop();
        _peekTimer.Stop();
        if (AutoHideCheck.IsChecked != true || SettingsPanel.Visibility == Visibility.Visible) return;
        _edgeHidden = true;
        EdgeProgressRail.Visibility = Visibility.Visible;
        Left = SystemParameters.WorkArea.Right - 28;
    }

    private void CheckHourlyPeek(DateTime now)
    {
        if (AutoHideCheck.IsChecked != true || HourlyPeekCheck.IsChecked != true || !_edgeHidden || now.Minute != 0) return;
        if (_lastHourlyPeek.Date == now.Date && _lastHourlyPeek.Hour == now.Hour) return;
        _lastHourlyPeek = now;
        ExpandFromEdge();
        _peekTimer.Start();
    }
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed && SettingsPanel.Visibility != Visibility.Visible) DragMove(); }

    private void StartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, writable: true)!;
        if (StartupCheck.IsChecked == true) key.SetValue(StartupValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(StartupValueName, throwOnMissingValue: false);
    }

    private static bool HasStartupRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, writable: false);
        return key?.GetValue(StartupValueName) is not null;
    }
}
