using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// The interface languages ChatMixr ships: the family's nine identities, in the order of
/// winui-app-kit's build/languages.json. Canadian English reuses US wording and Canadian French
/// reuses French wording; they stay separate choices so the picker and Windows' preferences match
/// them exactly. Keep this list,
/// <c>Localization/Catalogs/*.json</c> and the manifest's <c>&lt;Resource Language&gt;</c> entries in step.
/// </summary>
public static class AppLanguages
{
    /// <summary>The supported set, in picker order.</summary>
    /// <remarks>
    /// An explicitly typed array, not a bare collection expression: the Game Bar widget links this
    /// file, and CsWinRT's AOT check rejects collection expressions that target an interface (CsWinRT1032).
    /// </remarks>
    public static SupportedLanguages Supported { get; } = new("en-US", ((string Identity, string Catalog, string NativeName)[])
    [
        ("en-US", "en-US", "English (United States)"),
        ("en-CA", "en-US", "English (Canada)"),
        ("es-ES", "es-ES", "Español (España)"),
        ("fr-FR", "fr-FR", "Français (France)"),
        ("fr-CA", "fr-FR", "Français (Canada)"),
        ("de-DE", "de-DE", "Deutsch (Deutschland)"),
        ("zh-CN", "zh-CN", "简体中文（中国）"),
        ("pt-BR", "pt-BR", "Português (Brasil)"),
        ("ja-JP", "ja-JP", "日本語（日本）"),
    ]);
}
