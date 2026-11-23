# Game Bar widget: the connection to ChatMixr

The widget (`ChatMixr.GameBar/`) runs sandboxed inside ChatMixr's own package and can't touch audio. It is a view: ChatMixr sends it what to show, and it sends back what the user did. This note is the security design of that connection; the standards' D-120 points here.

## Transport

- **A named pipe served by the tray app,** `ChatMixr.GameBar.<session id>` (`Models/WidgetProtocol.cs`, `Services/WidgetServer.cs`). One per Windows session, so two signed-in users never meet. At most 4 connections at once (Game Bar shows one; a reopen can briefly overlap).
- **Why not an app service:** an app service is package-scoped by design, but hosting one from a full-trust WinUI 3 app needs its own activation path into the running tray app. The pipe gives the same boundary (below) with a connection the tray app already owns, and it was checked working from inside Game Bar's sandbox.

## Who can connect

The pipe's security descriptor is `O:<user>D:(A;;GA;;;<user>)(A;;FRFW;;;<package SID>)S:(ML;;NW;;;LW)`:

- the signed-in user has full access;
- among sandboxed apps, only this package's AppContainer SID, read and write;
- a low integrity label, because the widget runs at low integrity.

Any of the user's own unsandboxed programs could connect too; they could already change the user's audio themselves. The package SID comes from Windows (`DeriveAppContainerSidFromAppContainerName`) or, when Windows returns none inside the package, from the same rule worked out by `Models/AppContainerSid.cs`, which a test pins to a SID Windows reported.

## Messages

- One JSON message per line, UTF-8, source-generated serialisation (no reflection). A line over 256 KB is dropped and ends the connection (the whole line is read first; only the user and this package can connect).
- **ChatMixr to the widget:** `WidgetState`, the whole view: language, status line, the popup's devices, the in-game voice chat switch, and the fixed words. Sent on connect and whenever it changes.
- **The widget to ChatMixr:** `WidgetCommand`, one of `slider` (device, position), `switch` (device) or `voice-chat` (on or off).
- **No version field:** both ends ship in the same package and are replaced together on update (Windows ends the package's processes first), so they never mismatch. A message that doesn't parse is dropped with its connection.
- **No secrets cross it:** device ids and names, mix positions and interface text only.

## Validation in the tray app

Every command is treated as untrusted input from another process:

1. `WidgetPipe.DecodeCommand` rejects malformed JSON, unknown operations, and a slider or switch without a device; the connection is then dropped.
2. `Classes/WidgetLink.cs` checks the command against the current snapshot: the device must be one the popup lists, a slider can't move a wheel device, Switch to only for a device that could be switched to, and the voice chat switch only while a virtual cable is installed. The slider position is snapped and kept in range as the popup's own slider is.
3. The action then runs through `Classes/DeviceActions.cs`, the same code the popup's controls use.

## Failure isolation

- Serving the widget can never stop ChatMixr starting: any failure only leaves the widget saying ChatMixr isn't running.
- A crash in the widget ends the whole package, ChatMixr included (ChatMixr then runs its exit steps). So the widget guards every dispatcher callback and its event handlers, and logs to `widget.log` in the package's `LocalState` folder instead.

## Opening ChatMixr from the widget

Open ChatMixr first starts ChatMixr through the package's full-trust launcher (`FullTrustProcessLauncher`, the manifest's "Quiet" parameter group, which passes `--silent`), so it starts in the tray and Game Bar stays open. Only if that fails does it ask Game Bar to open the package's `chatmixr:` link (`XboxGameBarWidget.LaunchUriAsync`), which also starts ChatMixr in the tray but closes Game Bar. Windows refuses a widget's own plain launch of another app, since the widget never counts as the app in front. Any other use of the link only starts ChatMixr.
