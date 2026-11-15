using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace wont_sleep
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>单实例标识：同一个 key 对应同一个应用实例。</summary>
        private const string InstanceKey = "wont-sleep-main-instance";

        private Window? _window;
        private AppInstance? _mainInstance;

        /// <summary>
        /// 初始化应用对象。构造函数是应用执行的第一行代码，等价于 main() / WinMain()。
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 已有实例在运行时，把本次激活交给它并结束当前进程，避免出现第二个托盘图标。
            if (!EnsureSingleInstance())
            {
                return;
            }

            _window = new MainWindow();
            _window.Activate();
        }

        /// <summary>
        /// 通过 <see cref="AppInstance"/> 找到或注册主实例。
        /// </summary>
        /// <returns>当前进程是否为主实例（需要继续启动）。</returns>
        private bool EnsureSingleInstance()
        {
            AppInstance mainInstance;

            try
            {
                mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            }
            catch (Exception)
            {
                // 注册失败（例如某些非标准启动场景）时退化为允许多实例，保证应用仍能启动。
                return true;
            }

            if (mainInstance.IsCurrent)
            {
                _mainInstance = mainInstance;
                mainInstance.Activated += OnMainInstanceActivated;
                return true;
            }

            RedirectActivation(mainInstance);
            return false;
        }

        /// <summary>
        /// 把本次激活参数重定向给主实例，然后退出当前进程。
        /// </summary>
        private static void RedirectActivation(AppInstance mainInstance)
        {
            try
            {
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                using var completed = new ManualResetEventSlim(false);

                // 重定向会用到 COM 与线程池，放到后台线程执行；在 UI 线程直接等待可能死锁。
                _ = Task.Run(() =>
                {
                    try
                    {
                        mainInstance.RedirectActivationToAsync(activationArgs)
                            .AsTask()
                            .Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception)
                    {
                        // 主实例尚未就绪时重定向可能失败，忽略即可。
                    }
                    finally
                    {
                        completed.Set();
                    }
                });

                completed.Wait(TimeSpan.FromSeconds(6));
            }
            catch (Exception)
            {
                // 拿不到激活参数时直接退出。
            }

            Environment.Exit(0);
        }

        /// <summary>主实例收到其他实例的激活请求时，把窗口恢复到前台。</summary>
        private void OnMainInstanceActivated(object? sender, AppActivationArguments args)
        {
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                if (_window is MainWindow mainWindow)
                {
                    mainWindow.BringToFront();
                }
            });
        }
    }
}
