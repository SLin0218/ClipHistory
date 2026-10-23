# AGENT.md - ClipHistory Developer & Agent Guide

This document provides essential context, architectural rules, workflows, and development guidelines for AI agents and developers working on the **ClipHistory** codebase.

---

## 1. Project Overview

**ClipHistory** is a high-performance, lightweight clipboard manager extension designed exclusively for **Microsoft PowerToys Command Palette (CmdPal / PowerToys Run V2)**.

### Tech Stack
- **Target Framework**: .NET 8 (`net8.0-windows10.0.22000.0`, `win-x64`)
- **Extension Architecture**: Windows App SDK / WinUI 3 App Extension hosted via Out-of-Process COM Server (`Microsoft.CommandPalette.Extensions`, CsWinRT `2.1.1`, Shmuelie.WinRTServer `2.2.1`)
- **Clipboard Monitoring**: Win32 `AddClipboardFormatListener` and message pump on a dedicated STA thread
- **Storage**: SQLite with Write-Ahead Logging (`PRAGMA journal_mode=WAL`, `Microsoft.Data.Sqlite`)
- **UI & Presentation**: CmdPal `IDynamicListPage` with Markdown detail previews and AdaptiveCard settings forms

---

## 2. Directory Structure

```text
ClipHistory/
├── CmdPalExt/                        # PowerToys Command Palette extension project
│   ├── ClipHistoryCmdPal.csproj      # .NET 8 WinRT COM Server project
│   ├── ClipExtension.cs              # IExtension root entry and lifecycle manager
│   ├── ClipCommandProvider.cs        # ICommandProvider delivering TopLevelCommands
│   ├── ClipHistoryPage.cs            # IDynamicListPage: search, Markdown preview, shortcuts
│   ├── ClipSettingsContentPage.cs   # FormContent: AdaptiveCard extension settings
│   ├── CmdPalModels.cs               # WinRT projection models, helper wrappers, and ExtLog
│   ├── Package.appxmanifest          # Windows AppX package identity & COM server registration
│   ├── Assets/                       # Fluent icons (Square44x44, Square150x150, StoreLogo)
│   └── Program.cs                    # Entry point handling -RegisterProcessAsComServer
├── Models/
│   ├── ClipItem.cs                   # Multi-modal item (Text & Image), subtitles, friendly time
│   └── ClipType.cs                   # Enum: Text, Image
├── Services/
│   ├── ClipboardWatcher.cs           # Win32 clipboard listener on dedicated STA Dispatcher
│   ├── DatabaseService.cs            # SQLite persistence, WAL checkpointing, LRU eviction
│   ├── ThumbnailService.cs           # Image scaling, 96px thumbnail & 440x340 preview caching
│   ├── SettingsService.cs            # Configuration manager (clip_history_settings.json)
│   └── I18n.cs                       # Bilingual localization helper (I18n.T)
├── distribution/                     # Package submission manifests
│   ├── cmdpal-gallery/               # Manifests for microsoft/CmdPal-Extensions gallery PR
│   └── winget/                       # WinGet manifests for microsoft/winget-pkgs PR
├── .github/workflows/
│   └── build-and-release.yml         # CI/CD release workflow for CmdPal zip & SHA256 checksums
├── build.ps1                         # One-click build, package, registration, and deploy script
├── LICENSE                           # MIT License
└── README.md                         # User-facing bilingual documentation
```

---

## 3. Development Workflow & Commands

### Prerequisites
- .NET 8 SDK
- Microsoft PowerToys (with Command Palette enabled)
- Windows 10 (19041+) or Windows 11

### Build & Deploy
Always use the root PowerShell script `build.ps1`:

```powershell
# Build, package AppX, register extension, and restart PowerToys:
.\build.ps1 -Deploy -Restart

# Build only:
.\build.ps1
```

### Runtime Paths
- **SQLite Database**: `%APPDATA%\PowerToys\ClipHistory\clip_history.db`
- **Cached Images**: `%APPDATA%\PowerToys\ClipHistory\Images\`
- **Settings**: `%APPDATA%\PowerToys\ClipHistory\clip_history_settings.json`
- **Extension Log**: `%APPDATA%\PowerToys\ClipHistory\cmdpal_extension.log`

To inspect live extension logs in real-time:
```powershell
Get-Content "$env:APPDATA\PowerToys\ClipHistory\cmdpal_extension.log" -Tail 20 -Wait
```

---

## 4. Architectural Rules & Invariants

When modifying or refactoring code, you **MUST** adhere to the following principles:

### A. Resource Management & Memory Safety
1. **No GDI+ File Locks**:
   - **NEVER** use `Image.FromFile(path)` or open unmanaged file streams directly on image cache files. GDI+ retains exclusive handles that cause `IOException` on subsequent deletes.
   - **ALWAYS** load bytes into memory first via `File.ReadAllBytes(path)` and construct a `MemoryStream`.
2. **Clean STA Dispatcher Teardown**:
   - `ClipboardWatcher` uses a dedicated background STA thread running `Dispatcher.Run()`.
   - On `Dispose()`, you must explicitly call `_dispatcher?.InvokeShutdown()` and clean up the `HwndSource`.
3. **Event Unsubscription**:
   - All classes subscribing to `_watcher.OnClipCaptured` or `_settingsService.OnSettingsChanged` (`ClipHistoryPage`, `ClipSettingsContentPage`) **MUST** implement `IDisposable` and detach their delegates to prevent reference leaks.
4. **String & Memory Bounding**:
   - In `FormatTextForPreview`, text preview rendering is capped at **80 lines** with an ellipsis notice. Never render unbounded megabytes of text into HTML/Markdown preview cards.
5. **Orphan File Cleanup**:
   - Deleting clip records or clearing history **MUST** cascade to delete associated physical files (`img_*.png`, `thumb_*.png`, `prev_*.png`).

### B. Performance Invariants
1. **Sub-millisecond Search**:
   - Query filtering in `ClipHistoryPage` is **100% in-memory** against `_cache`. Never execute blocking SQLite queries or disk I/O on keystroke events.
2. **SQLite WAL Mode**:
   - SQLite connections use `PRAGMA journal_mode=WAL` with `PRAGMA wal_autocheckpoint=1000`. On service disposal, perform `PRAGMA wal_checkpoint(PASSIVE)`.

### C. UX & Safety Invariants
1. **Copy on Enter**:
   - Pressing `Enter` writes the selected item to the clipboard. Direct paste simulation is deliberately removed.
2. **Destructive Operations Require Confirmation**:
   - Deleting an entry (`Ctrl + Backspace`), clearing all history, or clearing unpinned items **MUST** trigger a confirmation prompt (`CommandResult.Confirm` with `ConfirmationArgs`).
3. **Pinning Protection**:
   - Pinned items (`IsPinned = true`) are strictly protected from LRU cache eviction and unpinned history clearing.

### D. Localization (i18n)
- **Zero Hardcoded Strings**: All user-visible strings (titles, subtitles, button labels, markdown headers, confirmation messages) must be localized using `I18n.T("中文", "English")`.
- `I18n.IsZh` checks the current UI culture to select the language dynamically.

---

## 5. Packaging & AppX Deployment Rules

1. **Avoid 0x80073CFB**:
   - In development mode, `Add-AppxPackage -Register` fails if an existing package with the same identity is currently registered.
   - `build.ps1` automatically unregisters existing packages before re-registering.
2. **Icon Handling**:
   - Runtime icons for `TopLevelCommands`, `ClipCommandProvider`, and `ClipHistoryPage` must be loaded via `IconInfo.FromAsset("Square44x44Logo.png")` (using `RandomAccessStreamReference`) rather than static glyphs.
   - Keep PNG assets optimized (< 100KB) to comply with Windows AppX and Command Palette gallery guidelines.
