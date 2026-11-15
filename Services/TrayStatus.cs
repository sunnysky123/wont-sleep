namespace wont_sleep.Services;

/// <summary>需要同步到托盘图标的状态：提示文字与当前的阻止状态。</summary>
/// <param name="ToolTip">托盘提示文字。</param>
/// <param name="IsActive">当前是否正在阻止休眠。</param>
/// <param name="DurationIndex">当前选择的时长索引（用于菜单勾选状态）。</param>
internal readonly record struct TrayStatus(string ToolTip, bool IsActive, int DurationIndex);
