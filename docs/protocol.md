> **Scope:** How ChatMixr talks to SteelSeries Arctis Nova 7 family receivers, byte by byte.
> **Governs:** `ChatMixr/Models/ReceiverReport.cs`, `ChatMixr/Services/Receivers.cs`, `ChatMixr/Services/HidInterop.cs`

# Receiver protocol

This file is the source of truth for the receivers' reports. Change it in the same commit as `Models/ReceiverReport.cs` or `Services/Receivers.cs`.

## Which receivers

- SteelSeries' USB vendor id `1038` (`vid_1038` in device paths).
- Product ids, the Arctis Nova 7 family from the Linux kernel's driver:
  - discrete battery levels: `2202`, `2206`, `223A`, `227A`, `22A4`, `22AB`;
  - battery percentage: `22A1`, `227E`, `2258`, `229E`, `22A9`, `22A5`.
- Measured on the Nova 7X (`22A5`) and Nova 7X Gen 2 (`229E`) on 2026-11-21. The others are untested.
- A receiver has no serial number. Its HID interfaces and its playback endpoints share a Windows container id, the only link between a wheel and its headset.

## Interfaces

Each receiver exposes two vendor-defined HID interfaces that ChatMixr opens, both shared, so SteelSeries GG can read them too:

| Usage page | Used for |
| --- | --- |
| `0xFF00` | The wheel, power and battery messages, sent unprompted |
| `0xFFC0` | The status request and its answer (an output report of at least 2 bytes) |

## Messages

Every report starts with report ID `0`, then an opcode. Anything else is ignored.

| Opcode | Bytes after the opcode | Meaning |
| --- | --- | --- |
| `0x45` | game level, chat level | The ChatMix wheel moved. Each level is 0 to 100; both are 100 in the middle. |
| `0xB9` | `03` on, `02` off | The headset turned on or off. |
| `0xB7` | battery | The battery percentage. |
| `0xB0` | power (`03` or `02`), battery, (one byte unused), game level, chat level | The answer to a status request. |

- **Status request:** report ID `0`, opcode `0xB0`, padded to the interface's output report length. It only asks; it changes nothing on the headset. ChatMixr sends it when a receiver connects and again when its headset comes on, so the wheel's position is known without waiting for it to move.
- A status's wheel levels count only while the headset is on.
- Levels and battery are capped at 100, so a corrupt report can't raise an app above its own volume.
