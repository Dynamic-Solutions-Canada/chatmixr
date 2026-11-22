> **Scope:** Building, checking and installing ChatMixr, and where it stores data.
> **Governs:** `Directory.Build.props`, `package.json`, `windows-build.json`, `scripts/**`, `ChatMixrMSIX/**`

# ChatMixr development

## Prerequisites

- WSL with this repository cloned inside it, including submodules: clone with `--recurse-submodules`, or run `git submodule update --init` (the app builds on `external/winui-app-kit` and `external/promo`).
- [lefthook](https://lefthook.dev) for the git hooks in `lefthook.yml`: run `lefthook install` once after cloning. Its secrets check is the kit's `scripts/devkit precommit`, which needs Node 22.12 or later, below 25, and installs its pinned tools inside the kit on first use.
- Node.js for the root `npm run` commands, and Python 3 for the catalog check.
- The [shared Windows build tools](https://github.com/Dynamic-Solutions-Canada/windows-build-tools), which run the Windows build from WSL and document their own Windows-side requirements, the disposable Windows TEMP copy and the local build VM. The link resolves once that repository is published.

## Build and install

Run these from the repository root in WSL:

| Command | What it does | What it proves |
| --- | --- | --- |
| `npm run verify:desktop` | Debug compile on Windows | The app compiles, XAML included |
| `npm run package:desktop:dev` | Signed dev MSIX | The package builds; not that it installs or runs |
| `npm run install:desktop` | Builds the dev MSIX, updates the installed copy in place and launches it | That it installs and starts; check behaviour in the app |
| `npm run package:desktop` | Unsigned Store MSIX | The Store package builds, with manifest text resolved for every language |

Signed dev builds (`package:desktop:dev`, `install:desktop`) install under the dev identity `TechnicallyReal.ChatMixr`. The app has no Store identity yet, so `npm run package:desktop` also carries the dev identity, and `--store-identity` is not available until the name is reserved in Partner Center. Never uninstall to get past an update error.

Packaging runs winui-app-kit's `build/build-msix.ps1`, configured by `windows-build.json` (the manifest folder, the dev and Store identities, and `manifest_strings`, which writes the manifest's translated text from the catalogs). `<Version>` in `Directory.Build.props` is the only version source. Dev builds raise the revision only in the staged copy, so the tracked version changes only for a release.

## Checks

`npm run validate` is the Linux gate. It leaves out the Store listing and screenshot checks until the app has a listing (see AGENTS.md). It runs, in order:

- `npm run format:check`: every C# project outside `external/` matches the root `.editorconfig`, which is winui-app-kit's file copied unchanged from the tag the app pins; never hand-edit it. `npm run format` rewrites the files.
- `npm run check:gitignore`: `.gitignore` ends with winui-app-kit's shared block, unchanged.
- `npm run check:l10n`: winui-app-kit's catalog check, set up by the `localization` block in `windows-build.json`. Every catalog has the same keys and `{{placeholders}}`, every key is used and every used key exists, no text contains an em dash, the app defines no `kit.` key, and the manifest languages match the family's list.
- `npm run check:notices`: `ChatMixr/THIRD-PARTY-NOTICES.txt` matches the packages the project restores, and `AboutContent.NoticesPath` (the About page's link) names that file. After adding, removing or updating a NuGet package, run `npm run generate:notices` and commit the file. The script lists every package except build tooling, with its full licence text, and fails on a licence it has no text for.
- `npm test`: every portable harness under `tests/`, found by winui-app-kit's `scripts/run-tests` without a hand-kept list. Each has a README saying what it covers and does not prove: the mixing rules (wheel reports, the volume curve, chat app matching and the volume bookkeeping), and the settings file (the kit's real store and this app's schema steps, against real files).

Formatting and tests run through winui-app-kit's `scripts/dotnet`, which uses the .NET SDK pinned in `global.json` when it is installed and otherwise its Docker image, capped at 3 GB (`KIT_DOTNET_MEMORY`). Run them as a non-root user. Run `npm run validate` and `npm run verify:desktop` before finishing a change.

### CI

`ci.yml` runs `npm run validate` on Linux for every push to `main` and every pull request. It also compiles the app on Windows for x64 and ARM64 (Debug, the same compile as `verify:desktop`) when app source, packaging, submodules or build configuration changed. `codeql.yml` scans the C# code and the workflows on each push, each pull request and weekly, leaving out `external/`. Dependabot proposes grouped NuGet and GitHub Actions updates weekly; the Windows App SDK and NLog move with winui-app-kit instead.

### Manual checks in the installed app

The harnesses cannot see the screen, the receivers or Windows' mixer. Before a release, install the build (`npm run install:desktop`), exit SteelSeries GG (or turn Sonar off), and check:

- The popup lists every playback device as a card: icon, name, what is playing, Switch to with its menu (the device in use has an accent bar at its left edge, the others a neutral one, Test, and the menu alone when calls or its mic are elsewhere), and a slider from Game to Chat. Arrow keys on a slider move it a tenth and stop at the middle; a wheel device's slider shows "Set by the headset's ChatMix wheel" on hover and snaps back if moved.
- Drag a device's slider while something plays on it: toward chat lowers everything but chat apps in Windows' volume mixer for that device, toward game lowers chat apps. Other devices are untouched. Exit and restart: each slider keeps its position.
- Hide a device from its right-click menu in the popup and with Hide on the Devices page: it leaves both lists and shows under Hidden devices, where Show brings it back.
- Chat apps: typing part of an installed app's name ("disc", "team") lists it, Enter adds the best match, and Discord adds as `Discord.exe`, not `Update.exe`. Remove a preset: it appears under Suggested, and Add all brings back every removed preset. An app that has played sound appears under Suggested as "Playing now" or, after a restart, "Heard recently".
- VB-Cable setup: with no cable installed, the Game voice chat card shows the three steps; "Open VB-Audio's page" opens vb-audio.com/Cable, "Restart now" asks before restarting, and after installing and restarting ChatMixr turns on in-game voice chat. Without a cable, the popup's Options panel (footer button) shows "Set up in-game voice chat", which opens the Chat apps page. VB-Cable's devices never appear in the lists.
- On a PC with onboard or laptop speakers (or the test VM with QEMU's `ich9-intel-hda`), those speakers are listed and mixed, although Windows puts them in the same container as VB-Cable.
- Turn on "Mix in-game voice chat separately": the line under it names "ChatMixr Voice Chat (VB-Audio Virtual Cable)" as the output to pick; the Chat apps page hides its list, search and suggestions and says why; Discord, even on the chat list, now follows the game side unless its output is the cable.
- Headsets: cards show On with battery, or Off (icon and slider dimmed, text not; Switch to disabled), right after ChatMixr starts, with no wheel move needed. With auto-switch on, turning a headset on or off switches to the first ready device in the Devices page order and the on-screen popup names it (not while the tray popup is open); turning it off and on again while another device is in use and lower in the order switches to the headset; a device picked by hand stays until the next power change. With the headset's wheel off-centre, turn it off and on: the mix is read again and apps are never muted while it is off.
- Settings > Keyboard shortcuts for the mix: off by default. Click a shortcut's button and press a new combination (Esc cancels; a plain key, a modifier alone, or the other shortcut's combination is refused with a note); the arrow restores the default. On, the shortcuts (Ctrl+Shift+Plus and Minus by default) move the in-use device's slider in steps that stop at the middle, even with another app in front; nothing happens on an Arctis headset in use.
- Turn a headset's wheel, or press a mix shortcut with a speaker in use: a popup at the bottom centre shows the device and its mix, follows each change, and fades about 1.5 s after the last; it never takes focus. With the tray popup open, it doesn't appear.
- Drag a slider near the middle: it snaps to the middle.
- Test: Test on a card plays a low hum and a repeating chime together until Stop; moving that device's slider (or its wheel) toward chat lowers the hum, toward game lowers the chime. Testing another device stops the first.
- In-game voice chat: turn it on. Windows' sound settings show the cable as the default communications device; Discord (output "Default") and a game's voice chat play on the calls device at its chat level and follow its slider or wheel. Test plays the chime through the cable. Turn it off, exit, or sign out and back in: the communications default returns to the calls device and voice chat still plays.
- Switch to on another device makes it Windows' default (check the taskbar's sound output list); the accent bar moves to it within a second. With one headset used for everything, Switch to another headset moves calls and the mic too (Sound settings, Recording). Set speakers for games and the headset for calls (menu: "Calls only" on the headset), then Switch to another device: calls stay on the headset, which shows a Calls badge.
- Theme: set ChatMixr's theme opposite to Windows' and open the popup and Devices page: cards, captions and badges follow ChatMixr's theme.
- Shut down or sign out with in-game voice chat on: after signing back in (ChatMixr not started), Sound settings show the calls device, not the cable, as the communications default; the previous log ends with "Windows is ending the session" and "Exit requested".
- Drag a card to a new place in the popup, and use Move up and Move down on the Devices page: both lists follow, and the order survives a restart and a device being unplugged and plugged back in.
- Plug in an Arctis receiver and turn the headset on: its card says to move the wheel and its slider is dimmed. Move the wheel: the slider follows it and can't be dragged, and in Windows' volume mixer the headset's chat apps (Discord, say) and other apps move in opposite directions. Back in the middle both return to where they were.
- With two receivers, each wheel moves only the apps playing through its own headset.
- Drag an app's slider in Windows' volume mixer, then turn the wheel: the app scales from your new level rather than jumping back.
- Start SteelSeries Sonar with a wheel headset connected: the amber dot, the tooltip and the popup and Devices page warnings appear within a few seconds and go when it exits.
- Exit from the tray menu: every app's volume in Windows' mixer is back where it started, `volume-baselines.json` is gone, and the log for that run ends with "Exit requested". End the process in Task Manager instead while a wheel has apps turned down, start ChatMixr again: the apps return to their original levels as it starts, before any wheel moves.
- Unplug a receiver or a device: it leaves the list, and the app does not crash or spin.
- Settings: start at sign-in, theme, and a language change that relabels the flyout, the window and the tray tooltip without reopening them. Reset settings asks first, then puts theme, language, the chat app list, every slider and the device order back to their defaults without changing Start with Windows.
- A second launch opens the window of the copy already running instead of starting another.
- About: the identity card with the icon and version, Check for updates, each link (the privacy policy page and the repository must be live before the first release), and "Open logs folder".
- A settings file that can't be read (replace `settings.json` with text that isn't JSON, then start the app): the window opens on Settings with the "settings were reset" notice naming the `.bad` backup.
- Light, dark and high contrast.

## Releases

Not set up yet. The name is not reserved in Partner Center, so there is no Store identity, listing, release notes or release workflow. When it is reserved, add what the standards require for a Store app (DIST-4, REL-6, LIST-5): the `release` skill, `build-msix.yml`, `store-publish.yml` and `credential-expiry.yml`, the Store listing in `store-assets/microsoft/`, and `check:listing` and `check:screenshots` back in `validate`.

## Where the app stores data

ChatMixr collects no telemetry or crash reports. It reads the receivers' wheel reports and Windows' list of playback devices and app audio sessions, and changes these things outside itself:

- Each app's own volume on each playback device, which Windows remembers per app and per device. A clean exit puts those volumes back; after a crash or an update, the next start puts them back from `volume-baselines.json`. If ChatMixr is uninstalled after a crash without being started again, the apps keep the reduced volumes until changed in Windows' volume mixer.
- Windows' default playback devices (games and media, calls) and the default microphone, when the user switches devices (or auto-switching does), or presses "Use my microphone".
- With in-game voice chat on: Windows' default communications device is the virtual cable while ChatMixr runs. Turning it off or exiting puts it back; a crash leaves it on the cable until ChatMixr next starts.
- With VB-Cable installed: the cable's two endpoint names ("ChatMixr Voice Chat", "ChatMixr internal, do not choose") and its hidden 16-channel entrance. Only while "Give the cable clear names" is on (off by default). These stay after uninstall; turning the switch off first puts VB-Cable's own names back and shows the entrance again, found by searching Windows' disabled devices on the cable's adapter even if ChatMixr's record was lost. Reset settings keeps those records.

The app makes no network requests of its own beyond the two below, both only when the user acts ("Open VB-Audio's page" only opens the browser), and sends nothing about the user or their devices:

- **"Our other apps":** when the page opens, it downloads the app catalogue (`apps.json`) and its icons from the apps-promo repository on GitHub (`raw.githubusercontent.com`), so GitHub sees the request's IP address.
- **Check for updates (About page):** a packaged install opens the Microsoft Store's updates page and makes no request itself. A copy run without a package asks this repo's GitHub Releases (`api.github.com`) for the latest version.

It writes:

| What | Where | After uninstall |
| --- | --- | --- |
| Settings (`settings.json`), including each device's slider, the device order, hidden devices, in-game voice chat on or off, the calls device (Windows' communications default as last chosen), the cable's original names and hidden entrance (kept through Reset settings), and the names of the 30 programs most recently heard (for the chat app suggestions) | `%AppData%\ChatMixr\` | Removed, see below |
| Settings backups: `settings.json.v<version>.bak` before an upgrade, `.bad` for an unreadable file, `.<time>.reset` after Reset settings | `%AppData%\ChatMixr\` | Removed, see below |
| Apps' volumes before ChatMixr scaled them (`volume-baselines.json`), while it runs or after a crash | `%AppData%\ChatMixr\` | Removed, see below |
| Logs (`logs.<start time>.txt`, the last few kept) | `%AppData%\ChatMixr\` | Removed, see below |
| Each app's own volume on a headset | Windows' per-app volume settings | Kept; put back on a clean exit |
| Start at sign-in | The package's startup task | Removed with the package |
| "Our other apps" cache (`apps.json` and icons) | The package's `LocalState\PromotedApps\` | Removed with the package |

Every install, including dev installs from `install:desktop`, is an MSIX package, so Windows redirects `%AppData%\ChatMixr\` into the package's own data folder and deletes it on uninstall. The About page's "Open logs folder" opens the real location. A copy run without a package writes to the real `%AppData%\ChatMixr\`, keeps the promo cache in `%LocalAppData%\TechnicallyReal\PromotedApps\` and, for start at sign-in, sets a `HKCU\...\CurrentVersion\Run` value; all three remain until removed by hand.

This list has not yet been confirmed against a real uninstall.
