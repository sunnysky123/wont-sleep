using System.ComponentModel;
using System.Runtime.InteropServices;

namespace wont_sleep.Services;

/// <summary>
/// 通过电源请求对象（<c>PowerCreateRequest</c> / <c>PowerSetRequest</c>）阻止系统进入睡眠。
/// <para>
/// 相比 <see cref="ThreadExecutionStateInhibitor"/>，电源请求不依赖线程，也不再是「线程状态」，
/// 而是由内核对象持有，因此可以按类型（系统 / 屏幕 / 离开模式 / 执行）分别开关，控制粒度更细。
/// 句柄使用完毕后需要 <c>CloseHandle</c>，请求随之失效。
/// </para>
/// </summary>
internal sealed class PowerRequestInhibitor : ISleepInhibitor
{
    private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x0000_0001;

    private readonly object _sync = new();

    private IntPtr _requestHandle = IntPtr.Zero;
    private IntPtr _reasonString = IntPtr.Zero;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _requestHandle != IntPtr.Zero;
            }
        }
    }

    public void Start(bool keepDisplayAwake)
    {
        lock (_sync)
        {
            StopCore();

            _reasonString = Marshal.StringToHGlobalUni("wont-sleep 正在阻止系统休眠");

            var context = new ReasonContext
            {
                Version = POWER_REQUEST_CONTEXT_VERSION,
                Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                SimpleReasonString = _reasonString,
            };

            var handle = PowerCreateRequest(ref context);
            if (handle == IntPtr.Zero)
            {
                ReleaseReasonString();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建电源请求失败。");
            }

            try
            {
                EnsureSet(handle, PowerRequestType.SystemRequired);

                if (keepDisplayAwake)
                {
                    EnsureSet(handle, PowerRequestType.DisplayRequired);
                }
            }
            catch
            {
                PowerClearRequest(handle, PowerRequestType.DisplayRequired);
                PowerClearRequest(handle, PowerRequestType.SystemRequired);
                CloseHandle(handle);
                ReleaseReasonString();
                throw;
            }

            _requestHandle = handle;
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
        var handle = _requestHandle;
        _requestHandle = IntPtr.Zero;

        if (handle != IntPtr.Zero)
        {
            PowerClearRequest(handle, PowerRequestType.DisplayRequired);
            PowerClearRequest(handle, PowerRequestType.SystemRequired);
            CloseHandle(handle);
        }

        ReleaseReasonString();
    }

    private void ReleaseReasonString()
    {
        if (_reasonString == IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(_reasonString);
        _reasonString = IntPtr.Zero;
    }

    private static void EnsureSet(IntPtr handle, PowerRequestType type)
    {
        if (!PowerSetRequest(handle, type))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"设置电源请求（{type}）失败。");
        }
    }

    public void Dispose() => Stop();

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr powerRequest, PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
