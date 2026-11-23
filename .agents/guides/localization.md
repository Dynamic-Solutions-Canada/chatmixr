> **Read when:** adding or changing any user-visible text, a language, or the language setting.
> **Governs:** `ChatMixr/Localization/**`, `ChatMixr/Classes/AppLanguages.cs`, `release-notes/**`, `store-assets/microsoft/listings.json`

# Localization

The app ships the nine-locale set from the standards' [locales.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/locales.md): nine locale identities in `Classes/AppLanguages.cs`, read from seven wording catalogs (en-CA and fr-CA reuse en-US and fr-FR wording).

## Interface text

- Every user-visible string goes through the catalogs: `Localization/Catalogs/*.json`, keyed by dotted identifiers (`status.sonar`, `flyout.header`) and embedded in the exe.
- In code, use `UiText.Get("key")` or `UiText.Format("key", ("name", value))`.
- In XAML, use `l10n:Localize.Text|Name|ToolTip|...="key"`; it relabels live on a language change.
- Shared labels (Settings, On and Off, About, the About page, the Settings rows, Cancel) are winui-app-kit's `kit.*` keys, translated in the kit; use them and never define a `kit.` key here.
- Add every new key to all seven catalogs, then run `npm run check:l10n` (winui-app-kit's check, configured by the `localization` block in `windows-build.json`). Keys the code names through `DescriptionKey`, `LabelKey` or `HelpKey` are listed there as `extra_key_patterns`.
- `AppDescription` is the package manifest's description (`ms-resource:Resources/AppDescription`), written into `resources.pri` for every language at packaging.

## What stays English

Logs, settings values, program names and device names are never translated. WinUI's built-in control text (for example a ToggleSwitch's On and Off when the app doesn't set them) follows Windows' own language, not the app's.

## Language choice

- The language follows Windows unless `UserSettings.Language` is set with the kit's Language row on the Settings page (empty means "Use system setting").
- The "Our other apps" page follows the app's language through `PromoLocalization.LocaleProvider`, set in `MainWindow`.

## Adding a locale

A new locale lands in one change: the catalog, `AppLanguages`, `Package.appxmanifest` `<Resources>`, every record in the Store `listings.json`, the release notes for the next version, and the README `## Languages` section.

## Review status

Every catalog, the listing and the release notes are AI-drafted; none has had a native-speaker review yet (LOC-12):

| Catalog | Review |
| --- | --- |
| en-US (also en-CA) | Source wording |
| fr-FR (also fr-CA) | Unreviewed |
| es-ES | Unreviewed |
| de-DE | Unreviewed |
| zh-CN | Unreviewed |
| pt-BR | Unreviewed |
| ja-JP | Unreviewed |

Record a review here, per wording catalog, when one happens.
