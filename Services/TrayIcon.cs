using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace wont_sleep.Services;

/// <summary>
/// 基于 <c>Shell_NotifyIcon</c> 的通知区域图标。
/// <para>
/// 图标回调需要一个窗口句柄，因此这里在独立线程上创建一个隐藏窗口并运行自己的消息循环；
/// 菜单与鼠标事件通过 <see cref="DispatcherQueue"/> 回到 UI 线程再触发。
/// </para>
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_NULL = 0x0000;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_REFRESH = WM_APP + 2;

    // 睁眼图标 = 正在阻止休眠；闭眼图标 = 未启用。
    private const string IconFileActive = "AppIcon.ico";
    private const string IconFileIdle = "AppIconIdle.ico";

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;

    private const int NIF_MESSAGE = 0x0001;
    private const int NIF_ICON = 0x0002;
    private const int NIF_TIP = 0x0004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;
    private const int IDI_APPLICATION = 32512;

    private const uint MF_STRING = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;

    private const int MenuShow = 1;
    private const int MenuStop = 2;
    private const int MenuExit = 3;
    private const int MenuDurationBase = 100;
    private const int TrayIconId = 1;
    private const int MaxToolTipLength = 127;

    private readonly DispatcherQueue _dispatcher;
    private readonly object _sync = new();

    // 菜单文案在构造时（UI 线程）从资源读取，避免在托盘线程访问资源。
    private readonly string _menuShowText;
    private readonly string _menuPreventText;
    private readonly string _menuStopText;
    private readonly string _menuExitText;
    private readonly string[] _durationLabels;

    private string _toolTip;
    private bool _isActive;
    private int _durationIndex;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool? _loadedIconIsActive;
    private string _className = string.Empty;
    private WndProcDelegate? _windowProc;
    private bool _iconAdded;
    private bool _disposed;

    public TrayIcon(DispatcherQueue dispatcher, string toolTip)
    {
        _dispatcher = dispatcher;
        _toolTip = NormalizeToolTip(toolTip);

        _menuShowText = Strings.Get("TrayShowWindow");
        _menuPreventText = Strings.Get("TrayPreventSleep");
        _menuStopText = Strings.Get("TrayStop");
        _menuExitText = Strings.Get("TrayExit");
        _durationLabels = DurationOptions.Labels;
    }

    /// <summary>用户要求显示主窗口（左键单击或菜单项）。</summary>
    public event EventHandler? ShowRequested;

    /// <summary>用户在菜单中选择某个时长，事件参数为 <see cref="DurationOptions.Labels"/> 的索引。</summary>
    public event EventHandler<int>? DurationRequested;

    /// <summary>用户要求停止阻止休眠。</summary>
    public event EventHandler? StopRequested;

    /// <summary>用户要求退出应用。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>创建隐藏窗口并添加托盘图标。需在 UI 线程调用（会阻塞等待初始化完成）。</summary>
    public void Create()
    {
        if (_thread is not null)
        {
            return;
        }

        var ready = new ManualResetEventSlim(false);
        Exception? startupError = null;

        var thread = new Thread(() =>
        {
            try
            {
                CreateWindowCore();
            }
            catch (Exception ex)
            {
                startupError = ex;
                DestroyWindowCore();
                ready.Set();
                return;
            }

            try
            {
                AddIconCore();
            }
            catch (Exception ex)
            {
                startupError = ex;
                DestroyWindowCore();
                ready.Set();
                return;
            }

            ready.Set();
            RunMessageLoop();
            RemoveIconCore();
            DestroyWindowCore();
        })
        {
            IsBackground = true,
            Name = "wont-sleep tray icon",
        };

        _thread = thread;
        thread.Start();
        ready.Wait();
        ready.Dispose();

        if (startupError is not null)
        {
            _thread = null;
            throw new InvalidOperationException("无法创建托盘图标。", startupError);
        }
    }

    /// <summary>同步状态：是否正在工作（决定图标睁眼/闭眼）与当前选中的时长索引。</summary>
    public void SetState(bool isActive, int durationIndex)
    {
        bool changed;

        lock (_sync)
        {
            changed = _isActive != isActive;
            _isActive = isActive;
            _durationIndex = durationIndex;

            if (!changed)
            {
                return;
            }
        }

        // 状态变化需要换图标，交给托盘线程处理。
        PostRefresh();
    }

    /// <summary>更新托盘图标的提示文字（可从任意线程调用）。</summary>
    public void UpdateToolTip(string text)
    {
        var normalized = NormalizeToolTip(text);

        lock (_sync)
        {
            if (_toolTip == normalized)
            {
                return;
            }

            _toolTip = normalized;
        }

        PostRefresh();
    }

    private void PostRefresh()
    {
        lock (_sync)
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            // 保证 Shell_NotifyIcon 总在创建图标的线程上调用。
            PostMessage(_windowHandle, WM_REFRESH, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private void CreateWindowCore()
    {
        _threadId = GetCurrentThreadId();
        _className = "wont-sleep-tray-" + Guid.NewGuid().ToString("N");

        _windowProc = WindowProc;

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = _className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册托盘窗口类失败。");
        }

        _windowHandle = CreateWindowEx(
            0,
            _className,
            "wont-sleep",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            windowClass.hInstance,
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建托盘窗口失败。");
        }
    }

    private void DestroyWindowCore()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        if (!string.IsNullOrEmpty(_className))
        {
            UnregisterClass(_className, GetModuleHandle(null));
            _className = string.Empty;
        }
    }

    private void AddIconCore()
    {
        bool isActive;
        lock (_sync)
        {
            isActive = _isActive;
        }

        _iconHandle = LoadIconCore(isActive);
        _loadedIconIsActive = isActive;

        var data = CreateIconData();
        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "添加托盘图标失败。");
        }

        _iconAdded = true;
    }

    /// <summary>按当前状态刷新提示文字与图标（仅在托盘线程上调用）。</summary>
    private void RefreshIconCore()
    {
        bool isActive;
        lock (_sync)
        {
            isActive = _isActive;
        }

        var reload = _loadedIconIsActive != isActive || _iconHandle == IntPtr.Zero;
        var previous = _iconHandle;

        if (reload)
        {
            _iconHandle = LoadIconCore(isActive);
            _loadedIconIsActive = isActive;
        }

        if (_iconAdded)
        {
            var data = CreateIconData();
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        if (reload && previous != IntPtr.Zero && previous != _iconHandle)
        {
            DestroyIcon(previous);
        }
    }

    private void RemoveIconCore()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateIconData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _iconAdded = false;
    }

    private void RunMessageLoop()
    {
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAYICON:
                HandleTrayMessage(hWnd, lParam);
                return IntPtr.Zero;

            case WM_REFRESH:
                RefreshIconCore();
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleTrayMessage(IntPtr hWnd, IntPtr lParam)
    {
        var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);

        switch (mouseMessage)
        {
            case WM_LBUTTONUP:
                Raise(ShowRequested);
                break;

            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowContextMenu(hWnd);
                break;
        }
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        var menu = CreatePopupMenu();
        var durationMenu = CreatePopupMenu();

        if (menu == IntPtr.Zero || durationMenu == IntPtr.Zero)
        {
            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (durationMenu != IntPtr.Zero)
            {
                DestroyMenu(durationMenu);
            }

            return;
        }

        bool isActive;
        int durationIndex;
        lock (_sync)
        {
            isActive = _isActive;
            durationIndex = _durationIndex;
        }

        try
        {
            AppendMenu(menu, MF_STRING, (IntPtr)MenuShow, _menuShowText);
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);

            for (var i = 0; i < _durationLabels.Length; i++)
            {
                var flags = MF_STRING;
                if (isActive && i == durationIndex)
                {
                    flags |= MF_CHECKED;
                }

                AppendMenu(durationMenu, flags, (IntPtr)(MenuDurationBase + i), _durationLabels[i]);
            }

            // 子菜单加入父菜单后归属于父菜单，会随父菜单一起释放。
            AppendMenu(menu, MF_POPUP, durationMenu, _menuPreventText);

            AppendMenu(
                menu,
                isActive ? MF_STRING : MF_STRING | MF_GRAYED,
                (IntPtr)MenuStop,
                _menuStopText);

            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (IntPtr)MenuExit, _menuExitText);

            if (!GetCursorPos(out var cursor))
            {
                return;
            }

            // 先取得前台权限，否则点击菜单外部时菜单可能不会关闭。
            SetForegroundWindow(hWnd);

            var command = TrackPopupMenu(
                menu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
                cursor.X,
                cursor.Y,
                0,
                hWnd,
                IntPtr.Zero);

            PostMessage(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case MenuShow:
                    Raise(ShowRequested);
                    break;

                case MenuStop:
                    Raise(StopRequested);
                    break;

                case MenuExit:
                    Raise(ExitRequested);
                    break;

                default:
                    RaiseDurationRequested(command);
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void RaiseDurationRequested(int command)
    {
        var index = command - MenuDurationBase;
        if (index < 0 || index >= _durationLabels.Length)
        {
            return;
        }

        var handler = DurationRequested;
        if (handler is null)
        {
            return;
        }

        _dispatcher.TryEnqueue(() => handler(this, index));
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        _dispatcher.TryEnqueue(() => handler(this, EventArgs.Empty));
    }

    private static IntPtr LoadIconCore(bool isActive)
    {
        foreach (var fileName in GetIconFileNames(isActive))
        {
            foreach (var iconPath in GetIconPathCandidates(fileName))
            {
                if (!File.Exists(iconPath))
                {
                    continue;
                }

                var handle = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (handle != IntPtr.Zero)
                {
                    return handle;
                }
            }
        }

        // 退而求其次：直接从可执行文件里取出第一个图标。
        if (Environment.ProcessPath is { Length: > 0 } executablePath)
        {
            var smallIcons = new IntPtr[1];
            if (ExtractIconEx(executablePath, 0, null, smallIcons, 1) > 0 && smallIcons[0] != IntPtr.Zero)
            {
                return smallIcons[0];
            }
        }

        return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    /// <summary>首选与状态匹配的文件，另一个作为兜底。</summary>
    private static IEnumerable<string> GetIconFileNames(bool isActive)
    {
        yield return isActive ? IconFileActive : IconFileIdle;
        yield return isActive ? IconFileIdle : IconFileActive;
    }

    /// <summary>
    /// 打包运行时资源位于包目录；直接运行 exe 时资源可能在 AppX 布局子目录中。
    /// </summary>
    private static IEnumerable<string> GetIconPathCandidates(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "Assets", fileName);
        yield return Path.Combine(baseDirectory, "AppX", "Assets", fileName);
    }

    private NOTIFYICONDATA CreateIconData()
    {
        string toolTip;
        lock (_sync)
        {
            toolTip = _toolTip;
        }

        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (int)WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = toolTip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private static string NormalizeToolTip(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "wont-sleep";
        }

        return text.Length <= MaxToolTipLength ? text : text[..MaxToolTipLength];
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DispatchMessage(ref MSG lpmsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        IntPtr hWnd,
        IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
}
