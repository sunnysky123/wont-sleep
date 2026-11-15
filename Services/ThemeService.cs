using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace wont_sleep.Services;

/// <summary>
/// 统一应用外观主题：窗口内容、标题栏按钮与 Mica 材质。
/// </summary>
internal static class ThemeService
{
    private static Window? _window;
    private static FrameworkElement? _root;
    private static Panel? _backgroundHost;
    private static ThemedMicaBackdrop? _backdrop;
    private static AppTheme _appTheme = AppTheme.System;

    /// <summary>在窗口内容创建后调用一次。</summary>
    public static void Initialize(Window window, FrameworkElement root, AppTheme theme)
    {
        _window = window;
        _root = root;
        _backgroundHost = root as Panel;

        // 跟随系统模式下，系统主题变化时也要刷新材质与标题栏。
        root.ActualThemeChanged += (_, _) => RefreshVisuals();

        Apply(theme);
    }

    public static void Apply(AppTheme theme)
    {
        _appTheme = theme;

        if (_root is not null)
        {
            _root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        RefreshVisuals();
    }

    private static void RefreshVisuals()
    {
        if (_root is null || _window is null)
        {
            return;
        }

        ApplyTitleBarTheme();
        ApplyBackdrop(_root.ActualTheme == ElementTheme.Dark);
    }

    private static void ApplyTitleBarTheme()
    {
        if (_window?.AppWindow?.TitleBar is not { } titleBar || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        titleBar.PreferredTheme = _appTheme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private static void ApplyBackdrop(bool isDark)
    {
        if (_window is null)
        {
            return;
        }

        if (!MicaController.IsSupported())
        {
            // 不支持 Mica 时退回纯色背景，避免内容区透明。
            _window.SystemBackdrop = null;
            if (_backgroundHost is not null)
            {
                _backgroundHost.Background = new SolidColorBrush(isDark
                    ? Color.FromArgb(255, 0x20, 0x20, 0x20)
                    : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
            }

            return;
        }

        if (_backgroundHost is not null && _backgroundHost.Background is not null)
        {
            _backgroundHost.Background = null;
        }

        var backdropTheme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

        if (_backdrop is null)
        {
            _backdrop = new ThemedMicaBackdrop(backdropTheme);
            _window.SystemBackdrop = _backdrop;
        }
        else
        {
            _backdrop.SetTheme(backdropTheme);
        }
    }
}
