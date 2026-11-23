> **Read when:** adding or changing any window, page, flyout, device card, button or card.
> **Governs:** `ChatMixr/**/*.xaml`, `ChatMixr/**/*.xaml.cs`, `ChatMixr/Classes/DeviceList.cs`, `ChatMixr/Classes/DeviceActions.cs`, `ChatMixr/Classes/MixVisuals.cs`, `ChatMixr/Windows/**`, `ChatMixr/Pages/**`, `ChatMixr/App.xaml`

# UI conventions

This app's own interface conventions. The shared family rules are in the standards' [ui.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/ui.md); this file records only what is specific to this app.

## Main window and pages

- `MixerWindow` hosts the kit's `SettingsShell` (title bar, navigation, built-in Settings, and the fixed footer: "Our other apps", then About). The window adds two menu items in code: Devices, then Chat apps. Never add footer items or restyle the shell.
- Every page is `ScrollViewer` then `StackPanel Style="{StaticResource PageContentStyle}"`, a `PageTitleStyle` title, then sections. A section is a `SectionHeaderStyle` heading (`FirstSectionHeaderStyle` for the first one under the title) above a `CardBorderStyle` card. These styles come from winui-app-kit's shared XAML (merged in `App.xaml`).
- The Settings page uses the kit's `StartupSetting`, `LanguageSetting`, `ThemeSetting` and `ResetSetting` rows and saves what they report.
- About is the kit's `AboutPage`, filled from `AboutContent.Create()`; never build an app About page.
- Pages that follow the mix derive from `LivePage` and redraw from its `OnAttach` handlers, on the engine's `Changed` and on a language change. Pages are never re-navigated to refresh.

## Tray flyout

- `MixFlyout` has four rows: a `BodyStrongTextBlockStyle` header, the device list, warnings (the relay failing, the microphone, Sonar while it runs with a wheel headset connected; always in the popup itself, never in Options), and a footer on `LayerOnAcrylicFillColorDefaultBrush` with a top `CardStrokeColorDefaultBrush` border. The footer holds the in-use device's volume (mute button, slider, level; it follows changes made elsewhere, and collapses when nothing is in use), then an Options button (`E9E9`) whose `Flyout` opens above it with the in-game voice chat switch and its instruction line (or, with no cable, a "Set up in-game voice chat" link to the Chat apps page), auto-switch and shortcuts, then a gear button that opens the main window. Both are icon-only with a tooltip. The popup shows no status line; the tray icon's tooltip keeps it.
- The list is refreshed from the engine's snapshot on every change and every language change while it is open, and on each open; the flyout resizes only when the number of devices changes.

## Mix popup

- `OsdWindow` is the one on-screen popup, with one face per notice. The mix, styled after Windows' own volume popup: a game icon, a thick rounded bar filled in the accent colour from its centre toward the side the mix leans, with a line marking the position that moves out from the centre, a chat icon; no visible text (its accessible name carries the device and levels). The switch (auto-switching): the device's kind icon in the accent colour, its endpoint name, its device name in tertiary caption type. Bottom centre of the main display, never activating, click-through, fading; never while the tray popup is open. Anything else that needs a passing notice adds a face to it.

## Device list and messages

- The device list is `DeviceList` in both the popup and the Devices page, a `ListView` (no selection, drag to reorder) whose items are device cards. Do not build a second list. The page adds Move up and Move down; everything a control does goes through `DeviceActions`.
- Every list row in the app is a `CardRow` (devices, chat apps, suggestions, hidden devices): build rows from it, never a new card shape. Its main action is `CardRow.TextButton`; an action that must be easy to find has an icon and a label (`CardRow.IconTextButton`); secondary actions are `CardRow.IconButton` (borderless, with tooltip and accessible name).
- A card is a rounded `CardBackgroundFillColorDefaultBrush` surface with a 1 px `CardStrokeColorDefaultBrush` border (device cards: a 3 px rounded vertical bar inside the left edge, `AccentFillColorDefaultBrush` with an accent icon on the default device and `ControlStrongStrokeColorDisabledBrush` on the rest; never an accent outline): the kind icon, the endpoint's own name ("Headphones") with the device name ("Arctis Nova 7X Gen 2") under it in tertiary caption type (never Windows' "Name (Device)" brackets), the activity line ("Testing…" while its test plays), on the right a labelled Test button on the device in use (or one still testing; "Test", then "Stop" while playing), then the switch control: a `SplitButton` "Switch to" (follow-along) whose menu moves games and media, calls or the microphone alone, or everything (roles already there greyed); on the device in use a chevron-only `DropDownButton` with the same menu, shown only when calls or its mic are elsewhere. The device in use has no "In use" label: its accent bar shows it, and screen readers get "In use" as the card's item status, then "Game", the slider and "Chat" across the card's full width under the header. On the page the cards are the surface: never wrap the list in another card.
- Rows update in place when the same devices are shown in the same order, so sliders move rather than redraw, and a slider being dragged is never set from the snapshot. A dragged slider snaps to the middle within `SliderMix.Detent` of it (a centre detent, like the wheel's). A wheel device's slider follows the wheel, can't be dragged, and is dimmed until the wheel reports.
- Badges after a card's name are `Pill`s (icon, short secondary text, subtle fill). A wireless headset's (`HeadsetBadge`): a battery icon that fills with the level, green, amber at 40% and below, red below 15%, with the percentage; or a grey power icon and "Off" (icon and slider dimmed, text kept readable, Switch to disabled). Then "Calls" and "Mic" on a device holding that role apart from the device in use.
- Anything built in code takes its colours from a style in `Styles/RowStyles.xaml` (`{ThemeResource}` setters), never a brush read from `Application.Current.Resources`, which follows Windows' theme rather than the window's.
- Names, activity lines, levels and the status line come from `MixVisuals`, so the tooltip, popup and window can't disagree.
- Secondary text (an empty list, the Sonar warning) is `EmptyState.Create`: secondary-coloured `CaptionTextBlockStyle`, wrapping. A note under a control in XAML uses the same style and brush.
- Buttons are named for screen readers with the device or app they act on (Switch to, Move up, Move down, Remove); icon-only buttons also get a tooltip.

## Switches

- Every ChatMixr switch is a `LabeledSwitch` (`LabelKey` set to a catalog key in the page's code, not XAML, so the catalog check sees it; `HelpKey` likewise for an optional help icon whose text shows on hover and in a panel on click): the switch, then its label beside it, wrapping when long and toggling when clicked, never a header above with "On"/"Off" (a ToggleSwitch's own content can't wrap and overflows the popup). The kit's Settings rows keep their own layout, so they never share a card with `LabeledSwitch`es: on the Settings page the Startup row has the General card to itself and ChatMixr's switches sit in "Devices and shortcuts".
- Options switched in more than one place (auto-switch, shortcuts) go through `AppOptions`, which raises `Changed` so every place shows the same state.

## Game Bar widget

The widget (`ChatMixr.GameBar/`) is UWP, so none of the kit's or this app's WinUI 3 rows, styles or badges reach it. It mirrors the popup's card instead: icon, name, caption (the headset's state, then what plays), Switch to on the right (In use on the device in use, disabled for a headset that's off), and the Game-to-Chat slider below, read-only on a wheel device with the wheel's tooltip. Every word it shows while connected comes from the tray app (`Classes/WidgetLink.cs`), so wording changes belong there or in the catalogs, never in the widget. Game Bar draws the frame and title; the widget follows Game Bar's theme, not ChatMixr's. Its styles live in `WidgetPage.xaml` (`WidgetSecondaryText` for captions, the status line and In use; `DeviceRow` is handed it), never in its `App.xaml`: Windows' UWP XAML only auto-loads an `App.xaml` at the package root, so the widget's is never loaded. That root must stay empty, which is why ChatMixr's own `App.xaml` is packaged as `Tray/App.xbf` (`ChatMixr.csproj`); a WinUI 3 `App.xaml` at the root crashes the widget.

## Text

- Visible text follows the catalog rule under "Conventions and gotchas" in [AGENTS.md](../../AGENTS.md).
