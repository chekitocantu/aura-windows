# Aura for Windows

Animated (live) wallpapers for Windows 10 and 11, inspired by [Aura](https://github.com/antwny/aura) for the COSMIC desktop.

- **Library**: your videos and images, with thumbnails. Click a card to set it.
- **Explore**: free wallpapers from MotionBGS (live), Wallhaven, Bing daily photos and a minimalistic collection. One click downloads and applies.
- **Screens**: a different wallpaper on each monitor.
- **Auto-pause**: stops the video during games and full-screen apps, on battery, or while the PC is locked.
- Starts with Windows and lives in the tray. Spanish and English.

Videos play with [mpv](https://mpv.io) behind the desktop icons. It works on both the classic desktop and the Windows 11 24H2 "raised" desktop.

## Install

Download `Aura-Setup-x.y.z.exe` from Releases. It installs for the current user only, so no admin rights are needed. Windows SmartScreen may warn because the app is not signed: choose **More info → Run anyway**.

## Build

```bash
dotnet test tests/Aura.Core.Tests
dotnet publish src/Aura -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

CI (`.github/workflows/build.yml`) bundles mpv, builds the Inno Setup installer and runs `Aura.exe --selftest` on Windows Server 2022 and 2025. The self-test applies a real wallpaper, checks where the window ended up and saves screenshots.

## License

GPL-3.0-or-later, the same as the original Aura.
