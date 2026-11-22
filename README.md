<h1 align="center">ChatMixr</h1>

<p align="center">
  Balance game and chat audio on any headset, with a slider or your Arctis ChatMix wheel, without SteelSeries GG.
</p>

Voice chat and game audio fight for the same headphones, and Windows only offers a separate slider per app. ChatMixr gives you one control that balances them: turn toward chat and everything else gets quieter, turn toward game and your chat apps do. Every playback device gets its own slider in the tray, and a Switch to button that brings your calls and headset microphone along when they were with the device you leave. With a SteelSeries Arctis Nova 7 headset, its ChatMix wheel works too, without SteelSeries GG: the wheel only tells the PC where it is, and ChatMixr does the mixing, on every headset at once (with two of these headsets on one PC, GG's Sonar has been seen to follow only one wheel).

## Features

- **A slider for every device:** the tray popup lists your headsets and speakers, each with its own remembered balance, a Switch to button (its menu moves just games, calls or the microphone), and an order you choose by dragging. Turn a headset on or off and ChatMixr can switch for you, with a popup saying where sound went.
- **Arctis ChatMix wheels, without GG:** each connected Arctis Nova 7 receiver's wheel controls its own headset, so a desk headset and a TV headset both work at once. Its slider shows the wheel's position.
- **Chat against everything else:** turn toward chat and everything except your chat apps gets quieter; turn toward game and your chat apps do. Discord, Teams, Zoom, Slack, TeamSpeak, Mumble and other common chat apps are listed to start with, and you can add or remove any program.
- **Game voice chat too:** voice chat built into a game plays from the game itself, so it can't be told apart by app. Install the free VB-Cable (ChatMixr walks you through it) and turn on "Mix in-game voice chat separately": voice chat gets its own channel, even in games.
- **Hear it work:** Test plays a game sound and a chat sound together until you stop it, so you can move the slider or wheel and hear the balance change.
- **Your volumes stay yours:** ChatMixr scales each app's own volume, the same sliders as Windows' volume mixer. Changes you make there are kept, and every app goes back to its own volume when ChatMixr exits.
- **In the tray:** a green dot while anything is turned down; click for every device's balance. An amber dot means SteelSeries Sonar is running and taking your apps' sound away from the headset.
- **Fits in:** follows Windows' light or dark theme and language, and can start when you sign in.

## Requirements and limits

- Any playback device for the sliders. For a wheel: a SteelSeries Arctis Nova 7 family headset and receiver. Tested with the Nova 7X and Nova 7X Gen 2; other Nova 7 models use the same wheel report and should work, but are untested.
- With an Arctis headset, Sonar must be off (in SteelSeries GG, or exit GG), because it sends your apps' sound to its own devices instead of the headset.
- Apps are told apart by program, so a chat in a browser tab is mixed with everything else that browser plays, unless the chat service can send its sound to the virtual cable. Use the chat service's desktop app otherwise.
- Each slider remembers its position. A wheel headset's position is read when its receiver connects and when the headset turns on.

## Get it

ChatMixr is in development and not yet released. You can build and install it yourself by following the [development guide](docs/development.md).

ChatMixr collects no telemetry or crash reports.

## License

Copyright (c) 2026 Dynamic Solutions. Licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).

ChatMixr is not made by or affiliated with SteelSeries.
