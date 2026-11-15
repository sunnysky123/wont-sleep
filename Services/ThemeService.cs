using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace wont_sleep.Services;

/// <summary>
/// 统一应用外观主题。
/// <para>
/// 设计原则：只使用受官方支持、且不涉及自绘 Composition 材质的操作——
/// 内容用 <see cref="FrameworkElement.RequestedTheme"/> 切换；标题栏按钮颜色与窗口背景显式设置，
/// 全部窗口级操作延后到消息循环空闲时执行，避免在 XAML 主题变更的同步回调里重入窗口系统。
/// </para>
/// </summary>
internal static class ThemeService
{
    private static Window? _window;
    private static FrameworkElement? _root;
    private static Panel? _backgroundHost;
    private static DispatcherQueue? _dispatcher;

    private static AppTheme _appTheme = AppTheme.System;
    private static AppTheme? _appliedWindowTheme;
    private static bool _refreshQueued;

    private static MicaBackdrop? _micaBackdrop;
    private static SolidColorBrush? _darkBackgroundBrush;
    private static SolidColorBrush? _lightBackgroundBrush;

    /// <summary>在窗口内容创建后调用一次。</summary>
    public static void Initialize(Window window, FrameworkElement root, AppTheme theme)
    {
        _window = window;
        _root = root;
        _backgroundHost = root as Panel;
        _dispatcher = root.DispatcherQueue;

        // 首次同步应用：此时没有用户交互，也没有打开的弹层。
        ApplyCore(theme, defer: false);
    }

    /// <summary>切换外观主题。可在事件处理函数中安全调用。</summary>
    public static void Apply(AppTheme theme) => ApplyCore(theme, defer: true);

    private static void ApplyCore(AppTheme theme, bool defer)
    {
        _appTheme = theme;

        if (_root is not null)
        {
            var requested = ToElementTheme(theme);
            if (_root.RequestedTheme != requested)
            {
                _root.RequestedTheme = requested;
            }
        }

        if (defer)
        {
            QueueRefresh();
        }
        else
        {
            RefreshVisuals();
        }
    }

    /// <summary>合并刷新请求，并推迟到消息循环空闲时执行。</summary>
    private static void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        if (_dispatcher is null)
        {
            RefreshVisuals();
            return;
        }

        _refreshQueued = true;

        var queued = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _refreshQueued = false;
            RefreshVisuals();
        });

        if (!queued)
        {
            _refreshQueued = false;
        }
    }

    private static void RefreshVisuals()
    {
        if (_window is null || _root is null)
        {
            return;
        }

        // 同一主题只应用一次，避免重复写窗口属性带来的重绘与重入。
        if (_appliedWindowTheme == _appTheme)
        {
            return;
        }

        Debug.WriteLine($"[Theme] applying window visuals: {_appTheme}");

        var isDark = _appTheme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => _root.ActualTheme == ElementTheme.Dark,
        };

        ApplyCaptionButtonColors(isDark);
        ApplyBackdrop(isDark);

        _appliedWindowTheme = _appTheme;
        Debug.WriteLine("[Theme] window visuals applied");
    }

    /// <summary>
    /// 标题栏最小化 / 最大化 / 关闭按钮的颜色。
    /// 跟随系统时全部置空，交回系统按当前系统主题绘制。
    /// </summary>
    private static void ApplyCaptionButtonColors(bool isDark)
    {
        if (_window?.AppWindow?.TitleBar is not { } titleBar || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        try
        {
            if (_appTheme == AppTheme.System)
            {
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonInactiveBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonInactiveForegroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
                return;
            }

            var foreground = isDark ? Colors.White : Colors.Black;
            var hover = isDark
                ? Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x20, 0x00, 0x00, 0x00);
            var pressed = isDark
                ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x30, 0x00, 0x00, 0x00);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = isDark
                ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x80, 0x00, 0x00, 0x00);
        }
        catch (Exception)
        {
            // 窗口尚未就绪等情况下可能失败，忽略。
        }
    }

    /// <summary>
    /// 窗口背景。
    /// 跟随系统时使用内置 Mica（与系统的深浅色天然一致）；
    /// 强制深浅色时内置 Mica 仍跟随系统、会与内容色不一致，因此改用与主题匹配的纯色背景。
    /// </summary>
    private static void ApplyBackdrop(bool isDark)
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            if (_appTheme == AppTheme.System)
            {
                _micaBackdrop ??= new MicaBackdrop();

                if (!ReferenceEquals(_window.SystemBackdrop, _micaBackdrop))
                {
                    _window.SystemBackdrop = _micaBackdrop;
                }

                if (_backgroundHost is not null && _backgroundHost.Background is not null)
                {
                    _backgroundHost.Background = null;
                }

                return;
            }

            if (_window.SystemBackdrop is not null)
            {
                _window.SystemBackdrop = null;
            }

            if (_backgroundHost is not null)
            {
                _backgroundHost.Background = isDark
                    ? _darkBackgroundBrush ??= new SolidColorBrush(Color.FromArgb(255, 0x20, 0x20, 0x20))
                    : _lightBackgroundBrush ??= new SolidColorBrush(Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
            }
        }
        catch (Exception)
        {
            // 材质设置失败时保持现状，不影响其余功能。
        }
    }

    private static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
