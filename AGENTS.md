# ChatMixr: Agent Instructions

ChatMixr is a resident WinUI 3 tray app that balances chat apps against everything else on each playback device by adjusting each app's own volume. Its tray popup lists every device with its own Game-to-Chat slider and a Switch to button that moves Windows' defaults to it. For Arctis Nova 7 family headsets it reads each receiver's ChatMix wheel itself, without SteelSeries GG, on several headsets at once. It ships as an MSIX (`ChatMixrMSIX/`) built from WSL through the shared Windows build bridge. Shared plumbing comes from the WinUI App Kit (`external/winui-app-kit`) and the "Our other apps" page from apps-promo (`external/promo`).

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards: read [stacks/windows.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/windows.md), then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

- **Host and startup:** `App.xaml.cs` (kit `AppBootstrap`) and the hidden `MainWindow.xaml(.cs)` (kit `HostWindow`), which owns the tray, the engine, the exit steps and the identity values.
- **Mixing:** `Services/MixEngine.cs` owns the audio thread and every pass; other `Services/` talk to Windows and the receivers, and `Models/*.cs` are portable rules that never call Windows, so the Linux harness links them unchanged.
- **UI:** the tray flyout `Windows/MixFlyout.xaml(.cs)`, the main window `MixerWindow.xaml(.cs)` with `Pages/*`, one row shape (`Classes/CardRow.cs`), and the device list and its actions in `Classes/DeviceList.cs` and `Classes/DeviceActions.cs`.
- **Game Bar widget:** `ChatMixr.GameBar/`, a UWP app in the same package, served over a pipe (`Models/WidgetProtocol.cs`, `Services/WidgetServer.cs`).
- **Shared code:** plumbing from the WinUI App Kit (`external/winui-app-kit`), the "Our other apps" page from apps-promo (`external/promo`).

The file map, area by area, is in [ARCHITECTURE.md](ARCHITECTURE.md#where-things-live).

## Behaviour

How it all works is in [ARCHITECTURE.md](ARCHITECTURE.md); the receivers' reports are in [docs/protocol.md](docs/protocol.md), which changes in the same commit as `Models/ReceiverReport.cs` and `Services/Receivers.cs`. The rules a change can break:

- **Scale, never set:** each app gets its baseline × factor, never an absolute level; a level ChatMixr didn't set is the user's new baseline. Exit puts every app back; `volume-baselines.json` covers a crash.
- **No guessing the wheel:** a wheel device isn't scaled until its receiver reports where the wheel is. Receivers are opened shared, so SteelSeries GG can read them too.
- **One headset link:** a receiver and its playback endpoints are matched only by Windows container id (the receivers have no serial number).
- **Virtual devices stay hidden:** software devices are never listed or mixed, judged by the audio adapter's enumerator, not the container.
- **Defaults move together, or not at all:** Switch to brings calls along only when they were with the device being replaced, and a headset's microphone when it was the replaced device's or was left behind on a headset no longer in use; a desk microphone stays put (`Models/DefaultRoles.cs`).
- **Voice chat is never stranded:** whenever in-game voice chat turns off, or ChatMixr exits, the communications default goes back to the user's calls device.
- **VB-Cable is never downloaded or run by ChatMixr** (its licence forbids bundling).
- **Untested receivers stay untested:** only the Nova 7X and Nova 7X Gen 2 are measured; never call another model tested.

## Conventions and gotchas

- The kit's [Gotchas](external/winui-app-kit/README.md#gotchas) apply here and are not repeated. This app's identity values are the constants at the top of `MainWindow.xaml.cs` and never change.
- **Threads:** all Core Audio calls and the volume bookkeeping run on the engine's one audio thread; everything else only posts work to it ([ARCHITECTURE.md](ARCHITECTURE.md#threads-and-passes)).
- **The Game Bar widget is the one UWP project** (`ChatMixr.GameBar/`): it builds through the kit's `companions` entry, not `dotnet build`, can't use the kit's WinUI 3 UI, and keeps its XAML in `GameBar/`. Why, and the `App.xbf` rule, are in [ARCHITECTURE.md](ARCHITECTURE.md#game-bar-widget).
- **Every user-visible string goes through the catalogs,** in all seven, never as a `kit.` key; see [.agents/guides/localization.md](.agents/guides/localization.md).
- **Package identity:** the manifest carries the Store identity `DynamicSolutionsCA.ChatMixr`; the kit's packaging script stamps signed dev builds with the dev identity `TechnicallyReal.ChatMixr` and the dev certificate's publisher. Never change either identity.
- **Product name:** "ChatMix" is SteelSeries' feature name. "ChatMixr" was chosen knowing the trademark risk; listings name SteelSeries and Arctis only to say what the app works with.

## Commands

Run from WSL with submodules initialised (`git submodule update --init`); setup is in `docs/development.md`.

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run verify:desktop` | The app compiles on Windows (Debug), XAML included | That it packages or runs; it does not build the Game Bar widget (`package:desktop:dev` does) |
| `npm run package:desktop:dev` | A signed dev package builds | That it installs or runs |
| `npm run install:desktop` | The dev package updates in place and launches | Behaviour; check it with the headsets |
| `npm run package:desktop` | An unsigned package builds, manifest strings resolved for every language | Signing; that it runs |
| `npm test` | Every harness under `tests/` passes on Linux: the mixing rules and the settings file | Anything needing Windows or the headsets (see each README) |
| `npm run validate` | The Linux gate: format, gitignore, icon, l10n, listing, notices, screenshot and hero checks, then `npm test` | A Windows compile; also run `verify:desktop` |
| `npm run format` | Rewrites C# outside `external/` to the kit's style | Behaviour |

`docs/development.md` covers each check `validate` runs and the generators (`icons`, `hero`, `generate:notices`); `store-assets/README.md` covers the screenshots and `shots:render`. Before finishing a change, run `npm run validate` and `npm run verify:desktop` (the `desktop` skill).

## Release

ChatMixr is live in the Microsoft Store; its Store ID, identity and privacy policy are in `store-assets/microsoft/listings.md`. Use the `release` skill. A `v*` tag creates a notes-only GitHub Release (`build-msix.yml`) and uploads a Store draft (`store-publish.yml`); submitting the draft is a separate manual run, described in `docs/development.md`.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Finding which file owns an area; changing mixing, devices, defaults, headsets, shortcuts, in-game voice chat or the test sounds | `ChatMixr/Services/**`, `ChatMixr/Models/**`, the device actions |
| [docs/protocol.md](docs/protocol.md) | Changing how receivers are found, read or asked for status | `Models/ReceiverReport.cs`, `Services/Receivers.cs`, `Services/HidInterop.cs` |
| [docs/development.md](docs/development.md) | Building, checking, or changing where the app writes data | Build setup, data locations, manual checks |
| [.agents/guides/localization.md](.agents/guides/localization.md) | Any user-visible text, a language, or the language setting | `ChatMixr/Localization/**`, `Classes/AppLanguages.cs`, release notes, listing text |
| [docs/game-bar-widget.md](docs/game-bar-widget.md) | Changing the widget's pipe, its messages, or how ChatMixr checks them | The connection's security design (D-120) |
| [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md) | Any window, page, flyout, row or card | `ChatMixr/**/*.xaml(.cs)`, `ChatMixr/Windows/**`, `ChatMixr/Pages/**` |
| [WinUI App Kit README](external/winui-app-kit/README.md) | Using any kit API or shared XAML | `external/winui-app-kit/**` (read only) |
| [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) | A Windows build, install or packaging problem | `windows-build.json`, the kit's `scripts/windows-build` |
| `desktop` skill | Building, verifying or installing locally | Local Windows builds |
| `release` skill | Releasing a version: notes, bump, tag, the Release and the Store draft | `release-notes/**`, `<Version>`, tags |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A file, service or window added, moved or removed | The file map in `ARCHITECTURE.md`, and Architecture above if an ownership boundary moves |
| A UI pattern, shared style or control | `.agents/guides/ui-conventions.md` |
| An npm command | The Commands table above, `docs/development.md`, the `desktop` skill, `paseo.json`, and `.github/workflows/ci.yml` if `validate` or the Windows compile changes |
| A mixing rule or product behaviour | `ARCHITECTURE.md` (and Behaviour above if it is a rule a change can break), `README.md`, the manual checks in `docs/development.md`, `tests/ChatMixr.Mixing.Tests/` |
| The receivers' reports or a supported receiver | `docs/protocol.md` in the same commit, `README.md`, `tests/ChatMixr.Mixing.Tests/` |
| Where the app writes data, or a new kind of data | The data table in `docs/development.md` |
| A setting's shape | A step in `Classes/SettingsMigrations.cs` and a scenario in `tests/ChatMixr.Settings.Tests/` |
| winui-app-kit pin bumped | The `uses:` tags in `.github/workflows/store-publish.yml` and `credential-expiry.yml` (the Store workflow refuses to run when they differ from the pin); the kit's `.editorconfig` and `build/shared.gitignore` (the last part of `.gitignore`) at that tag, then `npm run format` |
| A language or catalog rule | `.agents/guides/localization.md`, the `localization` block in `windows-build.json` |
| A NuGet package added, removed or updated | `npm run generate:notices`, and commit the notices file |
| The Game Bar SDK (`Microsoft.Gaming.XboxGameBar`) updated | `ChatMixrMSIX/Package.appxmanifest`: its classes from the readme, its interfaces from `ChatMixr.GameBar/list-private-interfaces.ps1`; then `npm run generate:notices` |
| The widget's pipe messages | `Models/WidgetProtocol.cs`, both ends (`Classes/WidgetLink.cs`, `ChatMixr.GameBar/GameBar/`), the widget scenarios in `tests/ChatMixr.Mixing.Tests/` |
| A guide or skill moved or removed | The guidance map above and every skill that names it |
