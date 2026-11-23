<p align="center">
  <img src="assets/generated/icon-256.png" alt="ChatMixr" width="120">
</p>

<h1 align="center">ChatMixr</h1>

<p align="center">
  Balance game and chat audio on any headset, with a slider or your Arctis ChatMix wheel, without SteelSeries GG.
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9N3F55DFWLQP">
    <img src="https://get.microsoft.com/images/en-us%20dark.svg" alt="Download ChatMixr from the Microsoft Store" height="56">
  </a>
</p>

<p align="center">
  <img src="store-assets/microsoft/screenshots/01-popup-dark.png" alt="ChatMixr's tray popup above the notification area in the dark theme: the Arctis Nova 7X headphones, in use, at 64% battery, with a slider set by its wheel a little toward chat; Speakers (Creative Pebble V3) with its slider toward game and LG TV with its slider toward chat, each with a Switch to button; and the volume of the device in use at 100%." width="250">
  &nbsp;
  <img src="store-assets/microsoft/screenshots/03-devices-dark.png" alt="ChatMixr's Devices page in the dark theme: cards for the Arctis Nova 7X headphones with a Test button and 64% battery, Speakers and LG TV with Switch to buttons, each with a Game to Chat slider and buttons to move it up or down or hide it, above a How it works section." width="250">
  &nbsp;
  <img src="store-assets/microsoft/screenshots/04-chat-apps-dark.png" alt="ChatMixr's Chat apps page in the dark theme: a Game voice chat card with steps to download VB-Cable from VB-Audio and restart, a search box to add a chat app from your installed apps, and the start of the chat app list with Discord and Discord Canary." width="250">
</p>

For anyone whose voice chat and game sound fight over the same headphones. Windows only offers a volume slider per app; ChatMixr gives each playback device one control that balances them. Turn toward chat and everything else gets quieter; turn toward game and your chat apps do. A SteelSeries Arctis Nova 7 headset's ChatMix wheel works too, without SteelSeries GG, on several headsets at once.

## Features

- **A slider for every device:** the tray popup lists your headsets and speakers, each with its own remembered balance, plus the volume of the one in use and an order you choose by dragging.
- **Arctis ChatMix wheels, without GG:** each connected Arctis Nova 7 receiver's wheel controls its own headset, so a desk headset and a TV headset both work at once. Battery and power show on each headset's card.
- **Chat against everything else:** Discord, Teams, Zoom, Slack, TeamSpeak, Mumble and other common chat apps are listed to start with, and you can add or remove any program.
- **Game voice chat too:** voice chat built into a game plays from the game itself, so it can't be told apart by app. Install the free VB-Cable (ChatMixr walks you through it) and turn on "Mix in-game voice chat separately": voice chat gets its own channel, even in games.
- **Switch devices the way you use them:** Switch to brings your calls and headset microphone along when they were with the device you leave, and its menu moves just games, calls or the microphone. Turn a headset on or off and ChatMixr can switch for you, with a popup saying where sound went.
- **Shortcuts and a popup:** optional keyboard shortcuts move the mix from any app, and a popup like Windows' volume popup shows the mix when a shortcut or wheel moves it.
- **In Xbox Game Bar:** press Win+G for a widget that moves the mix, switches devices and turns in-game voice chat on or off without leaving your game.
- **Hear it work:** Test plays a game sound and a chat sound together until you stop it, so you can move the slider or wheel and hear the balance change.
- **Your volumes stay yours:** ChatMixr scales each app's own volume, the same sliders as Windows' volume mixer. Changes you make there are kept, and every app goes back to its own volume when ChatMixr exits or Windows shuts down.
- **Fits in:** follows Windows' light or dark theme and language, and can start when you sign in.

## Get it

Get ChatMixr from the **[Microsoft Store](https://apps.microsoft.com/detail/9N3F55DFWLQP)**. It installs and stays up to date automatically. To build it yourself instead, follow the [development guide](docs/development.md).

ChatMixr collects no telemetry or crash reports.

### Requirements

- Windows 10 (22H2) or Windows 11.
- Any playback device for the sliders. For a wheel: a SteelSeries Arctis Nova 7 family headset and receiver. Tested with the Nova 7X and Nova 7X Gen 2; other Nova 7 models use the same wheel report and should work, but are untested.
- For game voice chat: [VB-Cable](https://vb-audio.com/Cable/), free from VB-Audio, installed by you.

## Good to know

- On Windows 10, ChatMixr's tray icon may start among the hidden icons (the arrow next to the clock). Drag it onto the taskbar to keep it in view.
- With an Arctis headset, Sonar must be off (in SteelSeries GG, or exit GG), because it sends your apps' sound to its own devices instead of the headset.
- Apps are told apart by program, so a chat in a browser tab is mixed with everything else that browser plays, unless the chat service can send its sound to the virtual cable. Use the chat service's desktop app otherwise.

## Languages

English (United States and Canada), Spanish, French (France and Canada), German, Simplified Chinese, Brazilian Portuguese and Japanese.

## Developers

- [Development guide](docs/development.md): building, checking, manual checks, and where the app stores data.
- [Agent instructions](AGENTS.md): the code map, the rules a change can break, and conventions.
- [Architecture](ARCHITECTURE.md): how mixing, devices, headsets and in-game voice chat work, and the [receiver protocol](docs/protocol.md).
- [UI conventions](.agents/guides/ui-conventions.md): how the windows, popups and cards are built.

## License

Copyright (c) 2026 Dynamic Solutions. Licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE). ChatMixr is not made by or affiliated with SteelSeries.
