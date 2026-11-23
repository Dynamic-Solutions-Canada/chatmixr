using ChatMixr.Classes;
using Windows.Storage;
using WinUIAppKit.Localization;

namespace ChatMixr.GameBar;

/// <summary>
/// The few words the widget shows on its own: while ChatMixr isn't connected, and the slider's
/// drag bubble (everything else comes from ChatMixr in its own language). Read from ChatMixr's
/// catalogs through the kit's dependency-free <see cref="TextLookup"/>, in the language ChatMixr
/// last said it uses, else Windows'.
/// </summary>
internal static class WidgetText
{
    /// <summary>Where the widget remembers ChatMixr's language between runs.</summary>
    private const string LanguageSetting = "language";

    private static string _locale = Resolve();

    private static readonly TextLookup Lookup = new(
        new TextCatalogs(typeof(WidgetText).Assembly, "ChatMixr.Localization."),
        AppLanguages.Supported,
        () => _locale);

    /// <summary>The text for a catalog key.</summary>
    public static string Get(string key) => Lookup.Get(key);

    /// <summary>The text for a catalog key with its placeholders filled.</summary>
    public static string Format(string key, params (string Name, object? Value)[] values) => Lookup.Format(key, values);

    /// <summary>
    /// Follow ChatMixr's language. Returns true when it changed, so the page redraws its own words.
    /// </summary>
    public static bool Follow(string language)
    {
        string? normalized = AppLanguages.Supported.NormalizeOverride(language);
        if (normalized == null || normalized == _locale)
        {
            return false;
        }

        _locale = normalized;
        try
        {
            ApplicationData.Current.LocalSettings.Values[LanguageSetting] = normalized;
        }
        catch (Exception)
        {
            // Not remembered; the next run starts in Windows' language until ChatMixr says otherwise.
        }

        return true;
    }

    /// <summary>The remembered language, else the best match for Windows' language list.</summary>
    private static string Resolve()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[LanguageSetting] is string saved
                && AppLanguages.Supported.NormalizeOverride(saved) is { } language)
            {
                return language;
            }
        }
        catch (Exception)
        {
            // Unreadable settings: fall through to Windows' list.
        }

        return AppLanguages.Supported.Resolve(global::Windows.System.UserProfile.GlobalizationPreferences.Languages);
    }
}
