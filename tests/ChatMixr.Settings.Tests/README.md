# Settings file harness

Checks how the settings file loads and resets (CONF-3, CONF-4). It links winui-app-kit's real `JsonSettingsStore` and `AtomicFile`, and the app's real schema steps (`SettingsMigrations.cs`), settings class (`UserSettings.cs`) and default chat app list (`ChatApps.cs`), and runs them against real files in a temporary folder. The kit's theme and window types are the stand-ins in `Stubs.cs`.

Covers: a first run (file written with the current schema version, the default chat apps); a saved chat app list replacing the defaults, so a removed default stays removed; a saved `false` for a setting that defaults to `true`; an unreadable file (kept as `.bad`, defaults written, not a first run); a current file (no backup); a file from a newer version (backed up, loaded as far as this version understands); Reset (old file kept as `.reset`, defaults and the default chat apps back); and the version 0 to 1 step (cable names stay on only where ChatMixr renamed or hid something, with a backup).

Does not prove: what `SettingsManager` and the Settings page do with each outcome (including Reset keeping the cable's records and the calls device), the Reset button and its confirmation, or where a packaged install keeps the file. Those need the installed app; see the manual checks in [docs/development.md](../../docs/development.md).

Run with `npm test`, or alone with `external/winui-app-kit/scripts/dotnet run --project tests/ChatMixr.Settings.Tests`.
