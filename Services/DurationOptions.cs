namespace wont_sleep.Services;

/// <summary>「持续时长」下拉框与托盘菜单的取值，索引一一对应。</summary>
internal static class DurationOptions
{
    public static readonly TimeSpan[] Values =
    {
        TimeSpan.Zero,                // 不限时
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    };

    /// <summary>与 <see cref="Values"/> 对应的本地化文案。</summary>
    public static readonly string[] Labels = BuildLabels();

    public static int MaxIndex => Values.Length - 1;

    public static TimeSpan Resolve(int index) =>
        index >= 0 && index < Values.Length ? Values[index] : TimeSpan.Zero;

    private static string[] BuildLabels()
    {
        var labels = new string[Values.Length];
        labels[0] = Strings.Get("DurationUnlimited");

        var hoursFormat = Strings.Get("DurationHoursFormat");
        for (var i = 1; i < Values.Length; i++)
        {
            labels[i] = string.Format(hoursFormat, (int)Values[i].TotalHours);
        }

        return labels;
    }
}
