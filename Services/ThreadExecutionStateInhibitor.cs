using System.ComponentModel;
using System.Runtime.InteropServices;

namespace wont_sleep.Services;

/// <summary>
/// 通过 <c>SetThreadExecutionState</c> 阻止系统进入睡眠。
/// <para>
/// 该 API 的状态是<strong>线程级</strong>的：一旦调用线程退出，请求即自动失效，
/// 因此这里用一个专用的后台线程持有请求，并通过停止信号让它在需要时优雅地清除状态。
/// </para>
/// </summary>
internal sealed class ThreadExecutionStateInhibitor : ISleepInhibitor
{
    private const uint ES_SYSTEM_REQUIRED = 0x0000_0001;
    private const uint ES_DISPLAY_REQUIRED = 0x0000_0002;
    private const uint ES_CONTINUOUS = 0x8000_0000;

    private readonly object _sync = new();

    private ManualResetEventSlim? _stopSignal;
    private Thread? _workerThread;

    public bool IsActive { get; private set; }

    public void Start(bool keepDisplayAwake)
    {
        lock (_sync)
        {
            StopCore();

            var stopSignal = new ManualResetEventSlim(false);
            var initialized = new ManualResetEventSlim(false);
            Win32Exception? startupError = null;

            var thread = new Thread(() =>
            {
                var flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED;
                if (keepDisplayAwake)
                {
                    flags |= ES_DISPLAY_REQUIRED;
                }

                bool applied;

                try
                {
                    applied = SetThreadExecutionState(flags) != 0;
                    if (!applied)
                    {
                        startupError = new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "系统拒绝了保持唤醒的请求。");
                    }
                }
                finally
                {
                    initialized.Set();
                }

                if (!applied)
                {
                    return;
                }

                try
                {
                    stopSignal.Wait();
                }
                catch (ObjectDisposedException)
                {
                    // 停止信号已被释放，直接退出即可。
                }
                finally
                {
                    // 清除本线程的持续状态请求，让系统恢复到默认电源策略。
                    SetThreadExecutionState(ES_CONTINUOUS);
                }
            })
            {
                IsBackground = true,
                Name = "wont-sleep thread execution state",
            };

            thread.Start();
            initialized.Wait();
            initialized.Dispose();

            if (startupError is not null)
            {
                stopSignal.Dispose();
                throw startupError;
            }

            _workerThread = thread;
            _stopSignal = stopSignal;
            IsActive = true;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopCore();
        }
    }

    private void StopCore()
    {
        var stopSignal = _stopSignal;
        var workerThread = _workerThread;

        _stopSignal = null;
        _workerThread = null;
        IsActive = false;

        if (stopSignal is null)
        {
            return;
        }

        stopSignal.Set();
        workerThread?.Join(TimeSpan.FromSeconds(2));
        stopSignal.Dispose();
    }

    public void Dispose() => Stop();

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
