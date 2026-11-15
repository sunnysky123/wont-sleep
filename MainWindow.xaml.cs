using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using wont_sleep.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace wont_sleep
{
    /// <summary>
    /// 应用主窗口。关闭窗口时不会退出进程，而是最小化到通知区域继续阻止休眠；
    /// 真正退出需要通过托盘菜单。
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly TrayIcon _trayIcon;

        private MainPage? _mainPage;
        private bool _allowClose;
        private bool _trayHintPending;
        private bool _trayAvailable;

        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            AppWindow.SetIcon("Assets/AppIcon.ico");
            AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(820, 820));

            // 主题需要在页面创建前应用，避免启动时先闪一下系统主题。
            ThemeService.Initialize(this, (FrameworkElement)Content, AppSettings.Load().Theme);

            _trayIcon = new TrayIcon(DispatcherQueue, Strings.Get("TrayTipInactive"));
            _trayIcon.ShowRequested += (_, _) => ShowFromTray();
            _trayIcon.DurationRequested += (_, index) => _mainPage?.StartWithDuration(index);
            _trayIcon.StopRequested += (_, _) => _mainPage?.StopWork();
            _trayIcon.ExitRequested += (_, _) => ExitApplication();

            try
            {
                _trayIcon.Create();
                _trayAvailable = true;
            }
            catch (Exception)
            {
                // 托盘不可用时保持普通窗口行为，避免应用被"藏"进无法访问的通知区域。
                _trayAvailable = false;
            }

            AppWindow.Closing += OnAppWindowClosing;
            Closed += OnWindowClosed;

            // Navigate the root frame to the main page on startup.
            RootFrame.Navigate(typeof(MainPage));

            _mainPage = RootFrame.Content as MainPage;
            if (_mainPage is not null)
            {
                _mainPage.StatusChanged += (_, status) =>
                {
                    _trayIcon.UpdateToolTip(status.ToolTip);
                    _trayIcon.SetState(status.IsActive, status.DurationIndex);
                };
            }
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowClose || !_trayAvailable)
            {
                return;
            }

            // 关闭窗口只是最小化到托盘，避免用户误操作中断正在运行的任务。
            args.Cancel = true;
            sender.Hide();
            _trayHintPending = true;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _allowClose = true;
            _mainPage?.Shutdown();
            _trayIcon.Dispose();
        }

        private void ShowFromTray()
        {
            if (!AppWindow.IsVisible)
            {
                AppWindow.Show();
            }

            Activate();

            if (_trayHintPending)
            {
                _trayHintPending = false;
                _mainPage?.ShowTrayHint();
            }
        }

        private void ExitApplication()
        {
            _allowClose = true;
            _mainPage?.Shutdown();
            _trayIcon.Dispose();
            Application.Current.Exit();
        }
    }
}
