using System;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using wont_sleep.Services;

namespace wont_sleep
{
    /// <summary>
    /// 主页面：控制「阻止系统休眠」的开关、屏幕常亮选项、持续时长与实现方式。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly AppSettings _settings;
        private readonly SolidColorBrush _activeBrush = new(Color.FromArgb(255, 0x3F, 0xB9, 0x50));
        private readonly SolidColorBrush _inactiveBrush = new(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
        private readonly DispatcherQueueTimer _ticker;

        private ISleepInhibitor _inhibitor = new PowerRequestInhibitor();

        private DateTimeOffset _startedAt;
        private DateTimeOffset? _deadline;
        private bool _suppressToggleEvents;
        private bool _uiReady;

        public MainPage()
        {
            _settings = AppSettings.Load();

            InitializeComponent();

            StatusDot.Fill = _inactiveBrush;

            _ticker = DispatcherQueue.CreateTimer();
            _ticker.Interval = TimeSpan.FromSeconds(1);
            _ticker.Tick += (_, _) => UpdateStatusDisplay();

            DurationComboBox.ItemsSource = DurationOptions.Labels;

            ApplySettingsToUi();
            SwitchInhibitor(_settings.Mode, restartIfActive: false);
            UpdateModeDescription();
            UpdateStatusDisplay();

            _uiReady = true;

            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        /// <summary>状态变化时通知外部（用于托盘提示文字与菜单勾选状态）。</summary>
        internal event EventHandler<TrayStatus>? StatusChanged;

        private bool IsKeepDisplayAwake => KeepDisplayCheckBox.IsChecked == true;

        private AppTheme SelectedTheme => ThemeComboBox.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System,
        };

        private InhibitionMode SelectedMode =>
            ModeComboBox.SelectedIndex == (int)InhibitionMode.ThreadExecutionState
                ? InhibitionMode.ThreadExecutionState
                : InhibitionMode.PowerRequest;

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_inhibitor.IsActive)
            {
                _ticker.Start();
                UpdateStatusDisplay();
            }
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e) => _ticker.Stop();

        /// <summary>应用真正退出前调用，释放休眠阻止请求。</summary>
        internal void Shutdown()
        {
            _ticker.Stop();
            _inhibitor.Dispose();
        }

        /// <summary>托盘菜单选择了某个时长：按该时长开始（或重启计时）阻止休眠。</summary>
        internal void StartWithDuration(int durationIndex)
        {
            if (!_uiReady)
            {
                return;
            }

            DurationComboBox.SelectedIndex = Math.Clamp(durationIndex, 0, DurationOptions.MaxIndex);

            if (PreventSleepToggle.IsOn)
            {
                // 已在工作中：切换时长会同步更新结束时间。
                UpdateStatusDisplay();
                return;
            }

            // 触发 Toggled → StartPrevention。
            PreventSleepToggle.IsOn = true;
        }

        /// <summary>托盘菜单选择「停止工作」。</summary>
        internal void StopWork()
        {
            if (!_uiReady)
            {
                return;
            }

            StopPrevention(Strings.Get("MessageStoppedFromTray"), InfoBarSeverity.Informational);
        }

        /// <summary>从托盘点「显示主窗口」后回到前台时给出的提示。</summary>
        internal void ShowTrayHint()
        {
            ShowMessage(InfoBarSeverity.Informational, Strings.Get("MessageTrayHint"));
        }

        private void PreventSleepToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_uiReady || _suppressToggleEvents)
            {
                return;
            }

            if (PreventSleepToggle.IsOn)
            {
                StartPrevention();
            }
            else
            {
                StopPrevention(null, InfoBarSeverity.Informational);
            }
        }

        private void KeepDisplayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_uiReady)
            {
                return;
            }

            _settings.KeepDisplayAwake = IsKeepDisplayAwake;
            _settings.Save();

            if (!_inhibitor.IsActive)
            {
                return;
            }

            // 标志位只在发起请求时生效，因此需要按新参数重新请求，但保留已运行时长。
            try
            {
                _inhibitor.Start(IsKeepDisplayAwake);
                UpdateStatusDisplay();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                ShowMessage(InfoBarSeverity.Error, Strings.Format("ErrorKeepDisplayFailedFormat", ex.Message));
            }
        }

        private void DurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady)
            {
                return;
            }

            _settings.DurationIndex = Math.Max(DurationComboBox.SelectedIndex, 0);
            _settings.Save();

            if (!_inhibitor.IsActive)
            {
                return;
            }

            ApplySelectedDuration();
            UpdateStatusDisplay();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady)
            {
                return;
            }

            var theme = SelectedTheme;
            _settings.Theme = theme;
            _settings.Save();

            ThemeService.Apply(theme);
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady)
            {
                return;
            }

            var mode = SelectedMode;
            _settings.Mode = mode;
            _settings.Save();

            UpdateModeDescription();
            SwitchInhibitor(mode, restartIfActive: true);
            UpdateStatusDisplay();
        }

        private void ApplySettingsToUi()
        {
            DurationComboBox.SelectedIndex = Math.Clamp(_settings.DurationIndex, 0, DurationOptions.MaxIndex);
            KeepDisplayCheckBox.IsChecked = _settings.KeepDisplayAwake;
            ModeComboBox.SelectedIndex = (int)_settings.Mode;
            ThemeComboBox.SelectedIndex = (int)_settings.Theme;
        }

        private void UpdateModeDescription()
        {
            if (ModeDescriptionText is null)
            {
                return;
            }

            ModeDescriptionText.Text = SelectedMode == InhibitionMode.ThreadExecutionState
                ? Strings.Get("ModeDescriptionThreadState")
                : Strings.Get("ModeDescriptionPowerRequest");
        }

        private void SwitchInhibitor(InhibitionMode mode, bool restartIfActive)
        {
            var wasActive = _inhibitor.IsActive;
            var keepDisplayAwake = IsKeepDisplayAwake;

            _inhibitor.Dispose();
            _inhibitor = SleepInhibitorFactory.Create(mode);

            if (!wasActive || !restartIfActive)
            {
                return;
            }

            try
            {
                _inhibitor.Start(keepDisplayAwake);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                _ticker.Stop();
                SetToggleWithoutRaisingEvents(false);
                ShowMessage(InfoBarSeverity.Error, Strings.Format("ErrorSwitchModeFailedFormat", ex.Message));
            }
        }

        private void StartPrevention()
        {
            try
            {
                _inhibitor.Start(IsKeepDisplayAwake);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                SetToggleWithoutRaisingEvents(false);
                UpdateStatusDisplay();
                ShowMessage(InfoBarSeverity.Error, Strings.Format("ErrorStartFailedFormat", ex.Message));
                return;
            }

            _startedAt = DateTimeOffset.Now;
            ApplySelectedDuration();
            _ticker.Start();
            UpdateStatusDisplay();
        }

        /// <param name="message">非 null 时会在信息栏显示提示。</param>
        private void StopPrevention(string? message, InfoBarSeverity severity)
        {
            _ticker.Stop();
            _inhibitor.Stop();
            SetToggleWithoutRaisingEvents(false);
            UpdateStatusDisplay();

            if (message is not null)
            {
                ShowMessage(severity, message);
            }
        }

        private void ApplySelectedDuration()
        {
            var duration = DurationOptions.Resolve(DurationComboBox.SelectedIndex);
            _deadline = duration == TimeSpan.Zero ? null : _startedAt + duration;
        }

        private void UpdateStatusDisplay()
        {
            if (!_inhibitor.IsActive)
            {
                StatusDot.Fill = _inactiveBrush;
                StatusText.Text = Strings.Get("StatusInactive");
                ElapsedText.Text = string.Empty;
                RemainingProgressBar.Visibility = Visibility.Collapsed;
                RaiseStatusChanged();
                return;
            }

            StatusDot.Fill = _activeBrush;
            StatusText.Text = Strings.Get(IsKeepDisplayAwake ? "StatusActiveDisplay" : "StatusActive");

            var elapsed = DateTimeOffset.Now - _startedAt;

            if (_deadline is null)
            {
                ElapsedText.Text = Strings.Format("StatusElapsedFormat", FormatDuration(elapsed));
                RemainingProgressBar.Visibility = Visibility.Collapsed;
                RaiseStatusChanged();
                return;
            }

            var remaining = _deadline.Value - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                StopPrevention(Strings.Get("MessageTimerFinished"), InfoBarSeverity.Informational);
                return;
            }

            var total = _deadline.Value - _startedAt;
            ElapsedText.Text = Strings.Format(
                "StatusElapsedRemainingFormat",
                FormatDuration(elapsed),
                FormatDuration(remaining));
            RemainingProgressBar.Visibility = Visibility.Visible;
            RemainingProgressBar.Value = total > TimeSpan.Zero ? remaining / total : 0;

            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            string text;

            if (!_inhibitor.IsActive)
            {
                text = Strings.Get("TrayTipInactive");
            }
            else
            {
                var prefix = Strings.Get(IsKeepDisplayAwake ? "TrayTipActiveDisplay" : "TrayTipActive");

                text = _deadline is null
                    ? Strings.Format("TrayTipUnlimitedFormat", prefix)
                    : Strings.Format("TrayTipEndsAtFormat", prefix, FormatEndTime(_deadline.Value));
            }

            var durationIndex = Math.Max(DurationComboBox.SelectedIndex, 0);

            StatusChanged?.Invoke(this, new TrayStatus(text, _inhibitor.IsActive, durationIndex));
        }

        /// <summary>结束时间：当天只显示时分，跨天时补上日期。</summary>
        private static string FormatEndTime(DateTimeOffset end)
        {
            return end.Date == DateTimeOffset.Now.Date
                ? end.ToString("HH:mm")
                : end.ToString("MM-dd HH:mm");
        }

        private void SetToggleWithoutRaisingEvents(bool isOn)
        {
            if (PreventSleepToggle.IsOn == isOn)
            {
                return;
            }

            _suppressToggleEvents = true;
            PreventSleepToggle.IsOn = isOn;
            _suppressToggleEvents = false;
        }

        private void ShowMessage(InfoBarSeverity severity, string message)
        {
            MessageBar.Severity = severity;
            MessageBar.Message = message;
            MessageBar.IsOpen = true;
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            return value.TotalHours >= 1
                ? $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}"
                : $"{value.Minutes:D2}:{value.Seconds:D2}";
        }
    }
}
