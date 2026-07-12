using System;
using System.IO;
using System.Text.Json;
using Avalonia.Styling;

namespace SafetyProto.AuthoringApp.Gui.Themes;

/// <summary>
/// Remembers the chosen theme variant between runs — the desktop counterpart of the
/// dashboard's localStorage theme key. Stored as a single JSON key under %APPDATA%.
/// </summary>
/// <remarks>
/// Every IO path here is deliberately non-fatal: a preference file that cannot be read or
/// written costs the user their theme choice, which is not worth failing a launch or
/// interrupting an edit over. Unreadable or absent state means Light.
/// </remarks>
internal static class ThemePreference
{
    private const string DarkName = "dark";
    private const string LightName = "light";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SafetyProto",
        "authoring-gui.json");

    private sealed record Preferences(string Theme);

    public static ThemeVariant Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return ThemeVariant.Light;

            var prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
            return string.Equals(prefs?.Theme, DarkName, StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ThemeVariant.Light;
        }
    }

    public static void Save(ThemeVariant variant)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory != null) Directory.CreateDirectory(directory);

            var name = variant == ThemeVariant.Dark ? DarkName : LightName;
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Preferences(name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preference not persisted; the session still honours the toggle.
        }
    }
}
