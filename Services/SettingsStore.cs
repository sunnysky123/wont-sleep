using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Storage;

namespace wont_sleep.Services;

/// <summary>
/// 轻量键值存储。优先使用有包标识时的 <see cref="ApplicationDataContainer"/>（LocalSettings）；
/// 若应用没有包标识（例如直接运行 exe），退回 %LOCALAPPDATA%\wont-sleep\settings.txt。
/// </summary>
internal sealed class SettingsStore
{
    private const string ContainerName = "wont-sleep";
    private const string FileName = "settings.txt";

    private readonly ApplicationDataContainer? _container;
    private readonly string? _filePath;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    private SettingsStore(ApplicationDataContainer? container, string? filePath)
    {
        _container = container;
        _filePath = filePath;
    }

    public static SettingsStore Create()
    {
        try
        {
            var container = ApplicationData.Current.LocalSettings.CreateContainer(
                ContainerName, ApplicationDataCreateDisposition.Always);

            var store = new SettingsStore(container, null);

            foreach (var pair in container.Values)
            {
                if (pair.Value is string text)
                {
                    store._values[pair.Key] = text;
                }
            }

            return store;
        }
        catch (Exception)
        {
            // 没有包标识 / 访问受限时退回文件存储。
            return CreateFileStore();
        }
    }

    private static SettingsStore CreateFileStore()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "wont-sleep");

            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, FileName);
            var store = new SettingsStore(null, path);

            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    store._values[line[..separator]] = line[(separator + 1)..];
                }
            }

            return store;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无法持久化时退化为内存存储，保证应用仍可运行。
            return new SettingsStore(null, null);
        }
    }

    public bool GetBool(string key, bool fallback) =>
        bool.TryParse(GetString(key), out var value) ? value : fallback;

    public int GetInt(string key, int fallback) =>
        int.TryParse(GetString(key), out var value) ? value : fallback;

    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => _values[key] = value;

    public void Flush()
    {
        try
        {
            if (_container is not null)
            {
                foreach (var pair in _values)
                {
                    _container.Values[pair.Key] = pair.Value;
                }

                return;
            }

            if (_filePath is null)
            {
                return;
            }

            File.WriteAllLines(_filePath, _values.Select(pair => $"{pair.Key}={pair.Value}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            // 保存失败不应影响主流程。
        }
    }
}
