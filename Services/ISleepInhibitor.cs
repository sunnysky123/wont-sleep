namespace wont_sleep.Services;

/// <summary>阻止系统休眠的实现方式。</summary>
internal enum InhibitionMode
{
    /// <summary>电源请求对象（PowerCreateRequest / PowerSetRequest）。</summary>
    PowerRequest = 0,

    /// <summary>线程执行状态（SetThreadExecutionState）。</summary>
    ThreadExecutionState = 1,
}

/// <summary>阻止系统休眠的实现抽象。</summary>
internal interface ISleepInhibitor : IDisposable
{
    /// <summary>当前是否持有阻止休眠的请求。</summary>
    bool IsActive { get; }

    /// <summary>开始阻止系统休眠；重复调用会以新参数重启请求。</summary>
    void Start(bool keepDisplayAwake);

    /// <summary>停止阻止系统休眠，恢复系统默认电源策略。</summary>
    void Stop();
}

internal static class SleepInhibitorFactory
{
    public static ISleepInhibitor Create(InhibitionMode mode) => mode switch
    {
        InhibitionMode.ThreadExecutionState => new ThreadExecutionStateInhibitor(),
        _ => new PowerRequestInhibitor(),
    };
}
