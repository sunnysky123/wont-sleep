using Microsoft.Windows.ApplicationModel.Resources;

namespace wont_sleep.Services;

/// <summary>
/// 本地化字符串访问入口（Strings/{language}/Resources.resw）。
/// 语言由系统首选语言自动匹配，无需手动设置。
/// </summary>
internal static class Strings
{
    private static ResourceLoader? _loader;

    private static ResourceLoader? Loader
    {
        get
        {
            try
            {
                return _loader ??= new ResourceLoader();
            }
            catch
            {
                // 资源不可用时退化为显示键名，避免整个界面无法加载。
                return null;
            }
        }
    }

    /// <summary>取字符串；缺失时返回键名，便于定位漏翻的文案。</summary>
    public static string Get(string key)
    {
        try
        {
            var value = Loader?.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>取带占位符的格式字符串并填充参数。</summary>
    public static string Format(string key, params object?[] args)
    {
        var format = Get(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
