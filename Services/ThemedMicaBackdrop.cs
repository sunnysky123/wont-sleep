using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace wont_sleep.Services;

/// <summary>
/// 可以指定主题的 Mica 背景。
/// <para>
/// 框架自带的 <see cref="MicaBackdrop"/> 始终跟随系统主题，应用内强制深色 / 浅色时会与内容不一致；
/// 这里直接用 <see cref="MicaController"/> 并通过 <see cref="SystemBackdropConfiguration.Theme"/> 指定主题。
/// </para>
/// </summary>
internal sealed partial class ThemedMicaBackdrop : SystemBackdrop
{
    private MicaController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public ThemedMicaBackdrop(SystemBackdropTheme theme) => Theme = theme;

    public SystemBackdropTheme Theme { get; private set; }

    /// <summary>运行时切换材质主题，无需重建背景对象。</summary>
    public void SetTheme(SystemBackdropTheme theme)
    {
        Theme = theme;

        if (_configuration is not null)
        {
            _configuration.Theme = theme;
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _controller = new MicaController();
        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = Theme,
        };

        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }
}
