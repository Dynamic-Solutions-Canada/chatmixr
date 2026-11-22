# Microsoft Store listing text

`listings.json` is the listing text in the shared `schemaVersion: 1` / `locales` shape (LIST-3), for the app's nine locale identities: `en-US`, `en-CA`, `es-ES`, `fr-FR`, `fr-CA`, `de-DE`, `zh-CN`, `pt-BR` and `ja-JP` (the same set the package manifest declares). Seven records hold wording; `en-CA` is `{ "copyFrom": "en-US" }` and `fr-CA` is `{ "copyFrom": "fr-FR" }`, matching the app's own catalogs. Fields are semantic names, not Partner Center CSV field IDs. No publisher reads this file yet (there is no repository or Store workflow), so the first submission is entered by hand from it (LIST-9).

Release notes are not in this file. Each version's "What's new" text is `release-notes/<version>.md` (`en-US`) plus one `release-notes/<version>.<identity>.md` per other wording catalog (REL-6, LIST-10).

Translated wording uses the same terms as the in-app catalogs (`ChatMixr/Localization/Catalogs`), so the listing matches what people see. Every translation, in the listing and in the release notes, was drafted without native-speaker review.

`npm run check:listing` (part of `npm run validate`) runs winui-app-kit's shared listing check, set up by the `listing` block in `windows-build.json`: the locales and aliases, every field, the limits below, matching caption counts, no em dashes, and that the README tagline is the first sentence of the `en-US` short description.

## Limits

| Text | Limit |
| --- | --- |
| Full description | 4,000 characters |
| Short description | 270 characters |
| Features | Up to 20, each up to 200 characters |
| Keywords | Up to 7, each up to 40 characters, 21 words combined |
| Screenshot captions | One per screenshot, up to 200 characters |
| Release notes (`release-notes/`) | 1,500 characters |

`check:listing` enforces these; the en-US draft is 3,144 characters of description and 12 features.

## Claims to keep true

- Wheels: read without SteelSeries GG from Arctis Nova 7 family receivers, tested on the Nova 7X and Nova 7X Gen 2 only. Don't name other models as tested.
- Sonar must be off to mix an Arctis headset, and the app says when it is on.
- Apps are told apart by program, so a chat in a browser tab is mixed with the rest of that browser.
- In-game voice chat needs VB-Cable, which the user installs; ChatMixr never downloads or installs it (its licence forbids bundling).
- Volumes: ChatMixr scales each app's own volume and puts it back on exit and Windows shutdown. A crash can leave apps turned down until ChatMixr next starts.
- Privacy: no account, ads, analytics or telemetry. Settings and logs are local. The only network request is the "Our other apps" catalogue, when that page opens; "Check for updates" opens the Store's updates page.
- Not made by or affiliated with SteelSeries. "ChatMix", SteelSeries and Arctis are named only to say what the app works with.
- The interface is in English, Spanish, French, German, Simplified Chinese, Brazilian Portuguese and Japanese, following Windows or a choice in Settings.
- Requires Windows 11 (the manifest's minimum version, 10.0.22000).

## Console-only fields

| Field | Value |
| --- | --- |
| Store ID | 9N3F55DFWLQP (https://apps.microsoft.com/detail/9N3F55DFWLQP) |
| Package identity | `DynamicSolutionsCA.ChatMixr`, publisher `CN=B55B96A5-F10C-49D3-8D0B-7260EDBE7625`, family `DynamicSolutionsCA.ChatMixr_3y7xdm7hdtb94` |
| Category | Utilities & tools |
| Pricing | US $0.99 with a 7-day free trial, set in Partner Center |
| Markets | All 240 markets Partner Center offers (it lists no Lebanon or Western Sahara), set in Partner Center |
| Age rating | IARC questionnaire: all other app types, No to every question; "Online content" is No because "Our other apps" only lists the publisher's own Store-rated apps |
| Privacy policy URL | https://www.dynamic-solutions.ca/privacy/chatmixr (D-017); live since 2026-11-22 |

## Screenshots

Five, in caption order: the tray popup, the mix popup above the taskbar, the Devices page, the Chat apps page and the Settings page, all in dark mode, captured from the real installed app on the test VM with a lent receiver and emulated devices; `store-assets/README.md` lists the capture steps, and `store-assets/manifest.json` holds the files, order and alt text (LIST-8). English captures serve every listing.

## Open questions before submission

- Recapture screenshot 00 once the winui-app-kit flyout fix is pinned: until then the tray popup touches the screen edge.
- Add the product to the shared promo catalog (apps-promo) once the listing is live.
