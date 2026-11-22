# Store assets

| Folder | Holds |
| --- | --- |
| `microsoft/listings.json`, `listings.md` | The Store listing text in every locale, and the claims it makes (see `listings.md`) |
| `manifest.json` | Every Store screenshot in listing order: its raw source, theme, crop, en-US caption and alt text (LIST-8) |
| `raw/microsoft/` | The raw captures: the test VM's whole desktop at 1:1, each with the `.json` sidecar the capture helper writes |
| `microsoft/screenshots/` | The listing screenshots, rendered from the raw captures |

## Screenshots

Captured from the real installed app, never from a personal machine (LIST-8). Every shot is in dark mode on the Windows 11 dark wallpaper (`img19.jpg`), at 1920 x 1080 and 100% scale. The captions in `manifest.json` are the en-US `screenshotCaptions` in `listings.json`, in the same order and count; each other locale has its own translation there. A new or changed screenshot is approved by the maintainer before it is committed.

The test VM has no headset or speakers, so the hardware is lent or emulated, and the app does the rest for real:

| Element | Source |
| --- | --- |
| The headset ("Arctis Nova 7X", its wheel, power and battery) | A real Arctis Nova 7X receiver, lent for the session: attached to WSL with usbipd-win and passed to the guest with QEMU's `usb-host` |
| The other playback devices | QEMU's `usb-audio` devices, renamed in the guest |
| The virtual cable for in-game voice chat | VB-Cable, installed in the throwaway guest from VB-Audio's own download |

1. **Capture** on the Windows 11 test VM (`windows-dev`) with the windows-build-tools app-test helpers, in a throwaway session: an uncommitted compose override adds `-snapshot` through the image's `ARGUMENTS` setting (check it is in QEMU's command line before Windows boots), the emulated audio devices, `/dev/bus/usb` and the receiver's `usb-host` device. Install the Release dev package, run `screenshot-desktop.ps1 -Mode Prepare`, then capture each scene with `Save-DesktopCapture`, after a click on blank space clears any focus rectangle. Copy each `.png` and `.json` into `raw/microsoft/` under the manifest's names, run `app_test_clean`, stop the VM and recreate it from the base compose, and give the receiver back to Windows (`usbipd detach`).
2. **Render** with `npm run shots:render` (winui-app-kit's renderer; its README has the rules): each capture is cropped to 1366 x 768, with nothing composited, shaved or scaled, and written without an alpha channel to `microsoft/screenshots/` (ASSET-9).
3. **Check** with `npm run check:screenshots`: sidecars, dark theme, 100% scale, effects and wallpaper, exact size, captions matching the en-US `screenshotCaptions`, and no stray files.
4. **Sync** to Partner Center with the next submission: the files in order and each locale's captions from `listings.json`.

A UI change that shows in a screenshot means recapturing that shot.
