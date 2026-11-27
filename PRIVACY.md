# Privacy Policy — LyricsOverlay

**Last updated: 27 November 2026**

LyricsOverlay is a Windows desktop app that shows time-synced karaoke lyrics over whatever you are doing. This policy explains what information the app handles, what leaves your PC, and what stays on it.

**The short version:** LyricsOverlay has no accounts, no login, no analytics, no advertising, and no servers of its own. We do not collect personal information about you. To find lyrics for the song you are playing, the app sends basic song metadata (title, artist, album and duration) to two public lyric services. Everything else — your settings, diagnostic logs, artwork and browser cache — stays on your device.

---

## 1. Who is responsible

LyricsOverlay is published by:

> **Publisher:** Siddhant Adityan
> **Email:** sidhantaadityan@outlook.com
> **Website / support:** https://github.com/sidpi/LyricsOverlay

This policy covers the LyricsOverlay desktop application distributed through the Microsoft Store and its built-in Studio control panel. It does not cover the websites of third-party services listed in section 5.

## 2. Information the app processes

| Information | Where it comes from | Why the app uses it | Leaves your PC? |
|---|---|---|---|
| **Track title, artist, album, duration, playback position, playing state, and the name of the app playing the track** | The Windows system media session (SMTC) of your music player or browser | To identify the song, look up its lyrics, and keep the displayed line in sync | Title, artist, album and duration are sent to lyric providers (see section 5). Position, playing state and source app are **not** sent. |
| **Album artwork** | The Windows media session | Read locally to optionally match the lyric colors to the cover art | **No** — artwork is never uploaded |
| **Song lyrics and word timings** | lrc.red, LRCLIB, or a local `.lrc` file you supply | To render the karaoke overlay | Lyrics are downloaded to your PC; the app does not send lyrics anywhere |
| **Your visual preferences** (font, colors, text scale, animation style, display mode, full-screen, sync offset, keep-awake, preferred lyric provider, auto-start) | Settings you change in the Studio | To remember how you like the overlay | **No** — stored locally in `overlay.json` next to the app |
| **Diagnostic log entries** | The app itself | Troubleshooting: track names, provider results, request URLs, and error messages are appended to a local log | **No** — stored locally in `%LOCALAPPDATA%\LyricOverlay\overlay.log` and never uploaded |
| **WebView2 browser storage** | The embedded Studio window (Edge WebView2 runtime) | Local caches and permissions for the Studio UI | **No** — stored locally in `%LOCALAPPDATA%\LyricOverlay\WebView2`; background networking and extensions are disabled |
| **App version and your IP address** | The app / your network connection | When you use the built-in updater, it contacts GitHub to check for a new release and download the installer | Yes, to GitHub (see section 5) |

We do **not** ask for or process your name, email address, location, contacts, payment details, or any account credentials. There is no sign-in.

## 3. How the information is used

The app uses the information above only to:

- detect what is playing and find matching time-synced lyrics;
- draw the animated lyric overlay, including optional colors sampled from album art;
- remember your settings between launches;
- write local diagnostic logs so problems can be investigated;
- check for and install updates when you choose to use the updater.

We do not use your information for profiling, targeted advertising, marketing, or automated decision-making. We do not sell or share it for money or other valuable consideration.

## 4. Local processing and storage

- **Lyrics cache:** resolved lyrics are kept in memory for the current session so a track does not trigger repeated lookups. The cache is not written to disk and is cleared when the app closes.
- **Local lyric files:** if you place a file named `Artist - Title.lrc` in a `lyrics` folder next to the app (or in its working directory), the app reads it and uses it instead of the online providers. These files never leave your PC.
- **Settings, logs and WebView2 profile:** stored only on your device, in the locations named in section 2. They are not synced to the cloud.
- **Studio ↔ overlay communication:** the Studio and the overlay talk over a local Windows named pipe (`\\.\pipe\LyricOverlayControl`). This is a local inter-process channel; it is not a network service and no data is sent over the internet through it.
- **Auto-start:** if you enable "Start with Windows," a single entry is added to your user account's Windows Run registry key so the app can launch into the tray at sign-in. You can turn this off at any time in the Studio.

## 5. Services that receive information

LyricsOverlay uses a small number of third-party services. When your device connects to them, their servers necessarily see standard connection information such as your IP address. Their own privacy practices and terms govern what they do with that information.

| Service | Purpose | What is sent | Links |
|---|---|---|---|
| **lrc.red** | Primary lyric source | A search query containing the track title and artist, an ISRC identifier returned by the search, and a request for the lyrics file. The app sends an identifying User-Agent with the app name, project link and contact address, as required by their API terms. | https://lrc.red/ · https://lrc.red/api |
| **LRCLIB** | Automatic fallback lyric source | Track title, artist, album and duration in the lookup request (or title and artist for a search). | https://lrclib.net/ · https://lrclib.net/docs.md |
| **GitHub** | Release check and installer download for the built-in updater | A request to the public Releases API for the latest version, with the app version in the User-Agent; the installer and checksum file are downloaded into your temporary folder. Song metadata is **not** sent to GitHub. | https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement |
| **Microsoft** | The Windows media APIs and the Edge WebView2 runtime that displays the Studio. The runtime is provided by Windows; it is not bundled by us. | Standard operating-system and browser-runtime behavior. We configure WebView2 with extensions and background networking disabled. | https://privacy.microsoft.com/privacystatement |

No other third-party services (including analytics, crash reporting, advertising or tracking providers) receive data from LyricsOverlay. The Studio's fonts and assets are bundled with the app rather than loaded from external content delivery networks.

## 6. Retention and deletion

We do not retain your information on any server operated by us. Data on your device is kept until you remove it:

- **Settings:** delete `overlay.json` next to the app to reset to defaults.
- **Diagnostic log:** delete `%LOCALAPPDATA%\LyricOverlay\overlay.log`.
- **Studio browser data:** delete the `%LOCALAPPDATA%\LyricOverlay\WebView2` folder.
- **Local lyric files:** delete the `.lrc` files you added.
- **Everything else:** uninstalling LyricsOverlay removes the application and its local settings; you can also disable auto-start in the Studio before uninstalling.

Note that information already transmitted to the third-party services in section 5 is retained according to those services' own policies and is outside our control.

## 7. Your choices and controls

- **Avoid online lookups entirely:** supply your own `lyrics/Artist - Title.lrc` files. Local files always take priority over the online providers.
- **Choose your provider:** switch the preferred lyric provider between lrc.red and LRCLIB in the Studio's Aesthetics tab.
- **Control auto-start:** turn "Start with Windows" on or off in the Studio.
- **Control artwork colors:** turn off "Match artwork colors" if you do not want the app to sample album art.
- **Control updates:** update checks and downloads only run when you use the updater.
- **Remove local data:** delete the files listed in section 6, or uninstall the app. Uninstalling is how you withdraw any consent to the processing described here.

## 8. Security

The updater verifies downloaded installers against a published SHA-256 checksum (or, if none is published, a Windows executable structure check) before running them. The Studio runs in a restricted WebView2 environment with extensions, background networking, developer tools (in release builds), status bar, zoom control, context menus and swipe navigation disabled. We do not collect or store passwords or credentials.

## 9. Children's privacy

LyricsOverlay is a general-purpose utility and is not directed at children. We do not knowingly collect information from anyone, including children.

## 10. Your privacy rights

Depending on where you live, you may have rights such as accessing, correcting, or deleting personal information held by a service provider. Because LyricsOverlay does not collect or store personal information about you on servers we control, most requests will concern data held locally on your device (see section 6) or data held by the third-party services in section 5. For questions about your rights or to exercise them, contact us using the details in section 1.

## 11. Changes to this policy

We may update this policy when the app changes or when required by law. The "Last updated" date at the top will change, and the current version will always be available at [link to where this policy is hosted]. Material changes will be reflected in a new app release and store listing.

## 12. Contact us

If you have questions, concerns, or requests about this policy or LyricsOverlay's handling of information, please contact:

> **Publisher:** Siddhant Adityan
> **Email:** sidhantaadityan@outlook.com
> **Project:** https://github.com/sidpi/LyricsOverlay/issues

---

*Lyrics data is provided by lrc.red and LRCLIB. LyricsOverlay is an independent project and is not affiliated with those services or with any music platform whose media session it reads.*
