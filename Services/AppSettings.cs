namespace wont_sleep.Services;

/// <summary>需要跨会话记住的用户选项。</summary>
internal sealed class AppSettings
{
    private const string KeyMode = "InhibitionMode";
    private const string KeyKeepDisplayAwake = "KeepDisplayAwake";
    private const string KeyDurationIndex = "DurationIndex";
    private const string KeyTheme = "Theme";

    private readonly SettingsStore _store;

    private AppSettings(SettingsStore store) => _store = store;

    public InhibitionMode Mode { get; set; } = InhibitionMode.PowerRequest;

    public bool KeepDisplayAwake { get; set; }

    public int DurationIndex { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    public static AppSettings Load()
    {
        var store = SettingsStore.Create();

        var modeValue = store.GetInt(KeyMode, (int)InhibitionMode.PowerRequest);
        var mode = Enum.IsDefined(typeof(InhibitionMode), modeValue)
            ? (InhibitionMode)modeValue
            : InhibitionMode.PowerRequest;

        var themeValue = store.GetInt(KeyTheme, (int)AppTheme.System);
        var theme = Enum.IsDefined(typeof(AppTheme), themeValue)
            ? (AppTheme)themeValue
            : AppTheme.System;

        var durationIndex = Math.Clamp(store.GetInt(KeyDurationIndex, 0), 0, DurationOptions.MaxIndex);

        return new AppSettings(store)
        {
            Mode = mode,
            KeepDisplayAwake = store.GetBool(KeyKeepDisplayAwake, false),
            DurationIndex = durationIndex,
            Theme = theme,
        };
    }

    public void Save()
    {
        _store.Set(KeyMode, ((int)Mode).ToString());
        _store.Set(KeyKeepDisplayAwake, KeepDisplayAwake.ToString());
        _store.Set(KeyDurationIndex, DurationIndex.ToString());
        _store.Set(KeyTheme, ((int)Theme).ToString());
        _store.Flush();
    }
}
