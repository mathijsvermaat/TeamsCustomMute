# Teams Custom Mute 🔕

> 🚧 **Status: early release (v0.x).** Tested only by the author on Windows + Edge.
> Expect rough edges — feedback and issues are welcome.

Mute a Microsoft Teams chat for *exactly* as long as you want — 1 hour, a day, a week,
or any custom number of hours — and it un-mutes itself automatically when the time is up.

Teams only lets you mute a chat indefinitely (until you remember to turn it back on).
**Teams Custom Mute** adds the missing piece: a small Windows system-tray app that mutes a
chat for a duration you choose, then quietly restores it for you. Perfect for noisy meeting
chats, temporary group threads, or "please stop pinging me until tomorrow" moments.

> ⚠️ **Unofficial.** This project is not affiliated with or endorsed by Microsoft. It drives
> the Teams **web** app through your own signed-in browser session using
> [Playwright](https://playwright.dev/dotnet/). There is no public Teams API for muting a chat,
> so it automates the UI on your behalf.

---

## Features

- ⏰ **Timed mutes** — mute a chat for 1 hour, 4 hours, 8 hours, 1 day, 1 week, or a custom
  number of hours. It auto-unmutes when the timer expires (even if your PC was asleep or off
  when the time lapsed — it catches up on next launch).
- ⭐ **Favorites** — save chats you mute often and mute them instantly from the tray with a
  single duration pick (no dialog).
- 🕑 **Recent chats picker** — the mute dialog lists your most recent chats so you don't have to
  type names. The list is kept warm in the background so the dialog opens instantly.
- 📋 **Active mutes** — see everything currently muted with the time remaining, and click any
  entry to unmute it immediately.
- 🔁 **Two-way sync** — if you unmute a chat directly inside Teams, the app notices and drops it
  from its active list automatically.
- 🔔 **Native notifications** — branded Windows toasts when a chat is muted, unmuted, or the
  timer expires.
- 🚀 **Start with Windows** — optional auto-launch at login; lives quietly in the system tray.

---

## Requirements

- **Windows 10 or 11**
- **Microsoft Edge** installed (the app automates Teams web through Edge)
- A Microsoft Teams account you can sign in to at <https://teams.microsoft.com>

To **build** from source you also need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## Getting started

### Download and run (no build required)

Grab the latest **`TeamsCustomMute-vX.Y.Z-win-x64.exe`** from the
[**Releases** page](https://github.com/mathijsvermaat/TeamsCustomMute/releases/latest) and just
run it — it's a self-contained single executable, so no .NET install is needed. Windows
SmartScreen may warn about an unknown publisher; choose **More info → Run anyway**.

### Run from source

```powershell
git clone https://github.com/mathijsvermaat/TeamsCustomMute.git
cd TeamsCustomMute
dotnet run --project src/TeamsCustomMuteWeb/TeamsCustomMuteWeb.csproj
```

The app starts in the system tray. The first thing to do is sign in:

1. Right-click the tray icon → **Sign in to Teams web…**
2. An Edge window opens. Sign in to Teams as you normally would.
3. Once your chat list loads, the window closes and the app runs silently in the background.
   Your session is remembered, so you only need to do this once.

### Mute a chat

- Right-click the tray icon → **Mute a chat…**, pick a chat (or type its name), choose a
  duration, and hit **Mute**. Optionally tick **Save as favorite**.
- Or use **Mute a favorite** → *chat* → *duration* to mute instantly.

That's it — the chat stays muted until the timer runs out, then it's automatically restored.

---

## How it works

There is no public API to mute a Teams chat, so the app uses
[Microsoft Playwright](https://playwright.dev/dotnet/) to drive the **Teams web** client through
your installed copy of **Microsoft Edge**:

1. A single, long-lived **headless** Edge context is launched against a persistent profile
   (so your sign-in is remembered between runs).
2. To mute/unmute, it finds the chat in the rail, opens its context menu, and clicks
   **Mute** / **Unmute** — exactly what you'd do by hand.
3. A 60-second timer auto-unmutes chats whose duration has elapsed and reconciles any mutes
   you cleared manually inside Teams.

Your Teams session lives only on your machine, in a local Edge profile under
`%LocalAppData%\TeamsCustomMute\EdgeProfile`. Nothing is sent anywhere except to Teams itself.

---

## Architecture

```
src/TeamsCustomMuteWeb/
├── Program.cs            # Entry point, single-instance mutex, notification branding
├── TrayAppContext.cs     # System-tray icon, context menu, and notifications
├── TeamsWebController.cs # Playwright singleton that drives Teams web via Edge
├── MuteManager.cs        # Tracks pending mutes, auto-unmute timer, two-way sync
├── MuteStore.cs          # Persists pending mutes to %AppData%
├── MuteDialog.cs         # "Mute a chat" dialog (pick chat + duration)
├── FavoritesStore.cs     # Persists favorite chats
├── FavoritesDialog.cs    # Manage-favorites dialog
├── ToastBranding.cs      # AppUserModelID + Start-menu shortcut so toasts show the app icon
├── AppIcon.cs            # Loads the embedded application icon
├── app.ico               # Application/tray icon
└── make-icon.ps1         # Script that generated app.ico
```

Runtime data is stored outside the repo:

- `%LocalAppData%\TeamsCustomMute\EdgeProfile` — the persistent Edge profile (your Teams session)
- `%AppData%\TeamsCustomMute\state.json` — pending timed mutes
- `%AppData%\TeamsCustomMute\favorites.json` — saved favorites

The `poc/` and `spike/` folders contain early throwaway prototypes kept for reference; the
shipping app is entirely under `src/`.

---

## Build a release

Produce a self-contained build that doesn't require the .NET runtime to be installed:

```powershell
dotnet publish src/TeamsCustomMuteWeb/TeamsCustomMuteWeb.csproj `
  -c Release -r win-x64 --self-contained `
  -o publish/win-x64
```

The published folder under `publish/win-x64` can be zipped and shared. Microsoft Edge still
needs to be installed on the target machine.

---

## Contributing

Issues and pull requests are welcome! This is a small hobby project, so please keep changes
focused. Good first contributions:

- Smarter chat matching / search when a chat isn't in the rail
- A friendlier first-run / onboarding experience
- Packaging (winget, signed releases, auto-update)

---

## License

[MIT](LICENSE) © Mathijs Vermaat
