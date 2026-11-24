> **Scope:** How ChatMixr works: its threads and passes, mixing, Windows' default devices, in-game voice chat, and the product rules people also read.
> **Governs:** `ChatMixr/Services/**`, `ChatMixr/Models/**`, `ChatMixr/Classes/DeviceActions.cs`, `ChatMixr/Classes/DeviceList.cs`, `ChatMixr/Classes/ChatDeviceSetup.cs`, `ChatMixr/Classes/MixShortcuts.cs`, `ChatMixr/Classes/WidgetLink.cs`, `ChatMixr.GameBar/**`

# Architecture

The area table in [AGENTS.md](AGENTS.md#architecture) says which file owns what. This file says how the parts behave. The receivers' byte-level reports are in [docs/protocol.md](docs/protocol.md).

## Threads and passes

- **One audio thread:** all Core Audio calls and the volume bookkeeping run on the engine's one audio thread (`Services/MixEngine.cs`). Receivers' reader threads, the timer and the interface only post work to it. The interface reads `MixEngine.Snapshot` on the UI thread and redraws on `Changed`.
- **Passes:** every second the engine re-applies (new apps, user changes); every third pass it also looks for receivers and devices and checks whether SteelSeries Sonar runs. The default device is read every pass.
- **New apps:** an app that starts playing is picked up by the next pass (within a second): its current volume becomes its baseline and it is scaled.

## Devices

- **Every active playback device** is listed and mixed. A device whose container id matches a connected receiver takes its levels from that wheel (its slider shows the wheel and can't be dragged). Every other device has its own slider, -100 (toward game) to 100 (toward chat), 0 leaving both at 100, saved per endpoint id in settings (shortly after a drag, and at exit).
- **Which headset:** a receiver and its playback endpoints share a Windows container id, the only link ([docs/protocol.md](docs/protocol.md)).
- **Virtual devices are never shown:** any software device (VB-Cable, Sonar's) is left out of the lists and mixing; ChatMixr manages the cable itself. A device is software when its audio adapter is enumerated by `ROOT` or `SWD` (the endpoint's undocumented adapter property), not by its container: Windows puts the PC's own hardware (onboard and laptop speakers, their microphones) in the same container as software devices. Without that property, the container decides.
- **Hidden devices** are taken out of both lists (Hide on the page's cards and in every card's right-click menu) and listed on the Devices page with Show. They are still mixed at their slider's position.
- **Order:** the user's order, by endpoint id, from dragging in the popup or the Devices page, or Move up and Move down on the page. Devices not connected keep their place; new ones go last, in Windows' order.
- **Centre detent:** a dragged slider within 2 of the middle snaps to it.
- **Volume row:** the tray popup shows the in-use device's own volume and mute (Windows' endpoint volume), read fresh with each snapshot so keyboard volume keys and Windows' own slider show within a second.

## Mixing

- **Factors:** on each device, chat apps (the editable list, matched by program name without `.exe`) get the chat factor and every other app the game factor. Factor = (level / 100)², a starting point to tune by ear. Windows' system sounds and ChatMixr itself are left alone.
- **Apps' own volumes are kept:** ChatMixr sets baseline × factor, never an absolute level. The baseline is an app's level before ChatMixr touched it; a level ChatMixr did not set is the user's change in Windows' mixer and becomes the new baseline. Baselines are saved in `volume-baselines.json` so a crash doesn't turn a scaled level into a baseline, and a clean exit puts every reachable app back and forgets those baselines.
- **A wheel device isn't scaled until its receiver says where the wheel is:** asked when the receiver connects and when its headset comes on. At start, apps a previous run left turned down (a crash, or an update that ended it) are put back first.
- **Session end:** shut down, restart and sign-out run the exit steps like Exit (the kit's `HostWindow`).
- **Sonar:** while `SteelSeriesSonar` runs, apps play to Sonar's virtual devices, not the headset, so ChatMixr can't mix the headset. With a wheel headset connected, the popup, Devices page, tooltip and an amber tray dot say so.
- **Limits:** a chat in a browser tab is mixed with the rest of that browser (apps are told apart by program), unless the chat service can send its output to the chat device.

## Windows' default devices

- **Three defaults:** games and media (console and multimedia roles), calls (communications) and the microphone. All are read and set through the undocumented `IPolicyConfig` that Windows' own Sound settings use (`Services/DefaultDevices.cs`, `Services/AudioPolicy.cs`).
- **Switch to:** a plain Switch to (and auto-switching) moves games and media, and brings calls along only when they were with the device being replaced, so "speakers for games, headset for calls" stays split. It brings the target's own mic (a headset's, found by container id) when the mic in use was the replaced device's own, or is another headset's mic left behind: one whose headset now carries neither games and media nor calls, as after a trip through speakers or a TV, which have no mic. A desk microphone, or anything on the PC's own mic jack, stays put (`Models/DefaultRoles.cs`). The button's menu moves one of them alone, or everything; the device in use has the menu alone when calls or its mic are elsewhere. Devices holding calls or the mic apart from the device in use show a Calls or Mic badge.
- **The calls device** the user chose is kept in settings (`CallsDevice`), because in-game voice chat lends Windows' communications default to the cable.

## Headsets

- **Several at once:** each receiver's wheel controls its own headset; with two Nova 7 receivers plugged in, SteelSeries GG was seen to follow only one wheel.
- **State:** each receiver is asked for its status when it connects (power, battery, wheel position: no "move the wheel" wait), and announces power and battery changes itself (`Models/ReceiverReport.cs`). Cards show "On · 79%" or "Off" (dimmed). A headset that comes on is asked for its status again.
- **Auto-switch:** with "Switch devices when a headset turns on or off" (Settings, on by default), a headset turning on or off makes ChatMixr switch (follow-along) to the first ready device in the user's device order (a headset while on, any other device always, hidden ones never) and show the device in the on-screen popup; between changes, a manual switch sticks. Overlaps with Hub Display Switch's audio switching; turn one off if they disagree.

## Shortcuts and the mix popup

- **Mix shortcuts (off by default, Settings):** two global shortcuts (Ctrl+Shift+Plus / Minus by default, recorded by the user on the Settings page; `Models/Shortcut.cs` has the rules: a modifier and a real key, and the two must differ) move the device in use 10 steps toward chat / game, stopping at the middle; nothing on a wheel device. Global hotkeys registered on the host window (`Classes/MixShortcuts.cs`); while on, other apps don't receive those keys, and the Settings page says when another app already holds one.
- **Mix popup:** a ChatMix wheel turn or a mix shortcut press shows the device and its Game-to-Chat position bottom-centre on the main display, like Windows' volume popup: never activates, click-through, holds 1.5 s after the last change, then fades. Not for the start-up status reading, and not while the tray popup is open.

## Chat apps page

A search of installed apps (Start menu shortcuts, uninstall entries and packages, the way Little Launcher finds apps; a Squirrel shortcut's program is the one after `--processStart`), the chat list as cards with Remove, and Suggested: removed presets (with Add all) and programs heard on any device, remembered across restarts (the 30 most recent), each with Add.

## In-game voice chat (Separate chat)

- **Why:** games play their own voice chat from the game's program, so per-app mixing can't separate it.
- **The switch:** with a virtual cable installed (VB-Cable, through the guided steps on the Chat apps page; `Classes/ChatDeviceSetup.cs`), one "Mix in-game voice chat separately" switch (in the popup and on the Chat apps page; Separate chat in code) applies everywhere. Its caption names the device to pick in games and chat apps.
- **While it is on:** ChatMixr makes the cable Windows' default *communications* device (voice chat set to "Default" follows; apps that pick a device are pointed at "CABLE Input" by hand) and plays the cable's recording side on the calls device the user chose (else the device in use) at its chat level (`Services/ChatRelay.cs`). The chat app list is hidden and not applied: every app playing straight on a device is game sound, and voice chat is whatever arrives through the cable. The game side stays on per-app volumes until ChatMixr's own driver.
- **When it is off,** or at exit, shutdown or sign-out, the communications default goes back to that calls device so voice chat is never left on a cable nobody plays. A crash can leave it on the cable until ChatMixr next starts.
- **The cable** is the first virtual device whose recording side pairs by name (VB-Cable's plain "CABLE Input" preferred over its 16-channel twin; `Models/ChatDevicePairing.cs`).
- **Delay:** 20 ms recording buffer, the output kept about 30 ms ahead (a 60 ms buffer never filled), and clock drift trimmed back to 10 ms held, during quiet moments or regardless past 60 ms (`RelayBuffer.Trim`, `Models/RelaySilence.cs`); silence is added when the output would run dry. The relay thread runs at MMCSS "Pro Audio" priority.
- **Microphone indicator:** it records the cable only while something plays into it (checked every 250 ms) and 8 s after, so Windows' microphone indicator isn't always on.
- **VB-Cable is never downloaded or run by ChatMixr:** its licence forbids integrating its package into another software's installation procedure without the author's agreement. The guided steps open VB-Audio's page, say what to run, offer a restart (after asking), and turn on in-game voice chat when the cable appears.

## Test sounds

A Test button on the device in use plays a game sound (a low pulsing hum) and a chat sound (a two-note chime every 1.5 s) continuously, together, until stopped, so the slider or wheel can be heard moving the balance (`Services/TestPlayer.cs`, `Models/TestSignal.cs`). The game sound plays on the device at its game level; the chat sound goes through the cable when that device is separating chat (so hearing it proves the route), otherwise on the device at its chat level. One device tests at a time; exit stops it.

## Game Bar widget

- **What it shows:** a widget in Xbox Game Bar (Win+G) shows the popup's devices (Switch to, the Game-to-Chat slider, read-only on a wheel device) and the in-game voice chat switch, in ChatMixr's own words and language.
- **How it reaches ChatMixr:** it is a sandboxed UWP app in ChatMixr's package with no audio access. The running tray app serves it over a named pipe, one per Windows session, whose access list allows only the user and, among sandboxed apps, this package, with a low integrity label so the widget can write to it (`Services/WidgetServer.cs`; the design is in [docs/game-bar-widget.md](docs/game-bar-widget.md)). Inside an installed package Windows was seen to return no SID for the package, so ChatMixr works it out from the package name (`Models/AppContainerSid.cs`, pinned by a test). A failure to serve the pipe only disables the widget.
- **Commands are checked:** each is checked against the current snapshot (a listed device, not a wheel device's slider, Switch to only where the popup offers it) and carried out through `DeviceActions`, like the popup's controls.
- **Not running:** the widget says so and offers Open ChatMixr, which starts it quietly in the tray through the package's full-trust launcher (`--silent`); a protocol launch would close Game Bar. It never starts ChatMixr on its own.

## Planned, not built: ChatMixr's own virtual devices

A separate signed audio driver project (C, from Microsoft's ACX or SysVAD samples; Rust has no audio driver support) creating two devices named exactly "ChatMixr (game)" and "ChatMixr (chat)". It needs an EV code-signing certificate, a Partner Center hardware account and its own installer (an MSIX can't install drivers), and is developed on the build VM, never a gaming PC (test-signing mode breaks anti-cheat). Separate chat would then route games to ChatMixr (game) and voice chat to ChatMixr (chat), and the game side moves off per-app volumes.
