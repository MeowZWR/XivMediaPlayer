using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;

namespace XivMediaPlayer.Localization;

internal static class Loc
{
    private static string _stringsDir = string.Empty;
    private static Dictionary<string, string> _current = new();
    private static Dictionary<string, string> _english = new();
    private static IDalamudPluginInterface? _pluginInterface;
    private static IDalamudPluginInterface.LanguageChangedDelegate? _languageChangedHandler;

    public static event Action? OnLanguageChanged;

    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        _stringsDir = Path.Combine(
            Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName) ?? string.Empty,
            "Localization",
            "Strings");

        _english = LoadFile("en");
        SetLanguage(pluginInterface.UiLanguage);

        _languageChangedHandler = langCode => SetLanguage(langCode);
        pluginInterface.LanguageChanged += _languageChangedHandler;
    }

    public static void Dispose()
    {
        if (_pluginInterface != null && _languageChangedHandler != null)
        {
            _pluginInterface.LanguageChanged -= _languageChangedHandler;
        }

        _pluginInterface = null;
        _languageChangedHandler = null;
    }

    public static string T(string key)
    {
        if (_current.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_english.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    public static string T(string key, params object[] args)
    {
        var template = T(key);
        if (args == null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    public static string Chat(string key, params object[] args) => T("Chat.Prefix") + T("Chat." + key, args);

    private static void SetLanguage(string uiLanguage)
    {
        var langFile = MapLanguage(uiLanguage);
        var loaded = LoadFile(langFile);
        _current = loaded.Count > 0 ? loaded : _english;
        OnLanguageChanged?.Invoke();
    }

    private static string MapLanguage(string uiLanguage)
    {
        if (string.IsNullOrWhiteSpace(uiLanguage))
        {
            return "en";
        }

        if (uiLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        var exactPath = Path.Combine(_stringsDir, uiLanguage + ".json");
        if (File.Exists(exactPath))
        {
            return uiLanguage;
        }

        return "en";
    }

    private static Dictionary<string, string> LoadFile(string lang)
    {
        var path = Path.Combine(_stringsDir, lang + ".json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
