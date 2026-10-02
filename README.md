<div align="center">

# ScamWYF.ModHandler

![license](https://img.shields.io/badge/license-MIT-blue)
![last commit](https://img.shields.io/github/last-commit/swyf-modding/Mod-Handler?label=last%20commit&color=blue)
![version](https://img.shields.io/badge/version-1.0.0-blue)
![game build](https://img.shields.io/badge/game-v82--playtest-blue)
![Unity](https://img.shields.io/badge/Unity-6000.3.10f1-blue)

*Built with:*

![C#](https://img.shields.io/badge/C%23-512BD4?style=for-the-badge&logo=csharp&logoColor=white)
![Harmony](https://img.shields.io/badge/Harmony-51796aa?style=for-the-badge)
![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23.5-14172c?style=for-the-badge)
![UI Toolkit](https://img.shields.io/badge/UI%20Toolkit-000000?style=for-the-badge&logo=unity&logoColor=white)

</div>

---

## Table of Contents

- [Overview](#overview)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
  - [Developer Setup](#developer-setup)
  - [Usage](#usage)
  - [Testing](#testing)
- [Compatibility](#compatibility)
- [Collisions](#collisions)
- [Project Structure](#project-structure)
- [Continuous Builds](#continuous-builds)
- [Security](#security)
- [License](#license)
- [Related Projects](#related-projects)

---

## Overview

**ScamWYF.ModHandler** is an unofficial, community-built BepInEx mod for **Scam With Your Friends**. It
adds a **Plugins** tab to the shared in-game menu, listing every plugin file installed with a switch for
each one.

**Key Features:**

- Lists plugin files from both `BepInEx\plugins` and `BepInEx\plugins_disabled`, so a plugin that failed
  to load is still visible
- Reads each plugin's real name and version from its `[BepInPlugin]` metadata with Cecil, so a
  **disabled** plugin still shows its real name without its code ever being loaded
- Enables and disables by moving the file, which is what BepInEx itself watches — nothing is deleted
- Reports collisions between mods: the same hotkey, the same patched method, the same GUID, both owners
  named
- No redistributed game DLLs, decompiled game source, telemetry, or credential collection

> [!IMPORTANT]
> Toggling takes effect on the **next launch**, not immediately. BepInEx's chainloader loads every
> plugin *before* any plugin's `Awake` runs, so a plugin cannot stop a sibling from loading in the same
> session. Nothing is deleted, and the tab says so rather than leaving somebody to wonder why their click
> did nothing.

> [!NOTE]
> This mod cannot disable itself. BepInEx would refuse the file move while its own assembly is loaded,
> and a plugin that can switch itself off is a bad thing to ship.

This project is not affiliated with or endorsed by the developers or publisher of Scam With Your Friends.

---

## Getting Started

### Prerequisites

- A legally installed copy of **Scam With Your Friends**, with the build listed under
  [Compatibility](#compatibility)
- BepInEx plus the unstripped corlib override. This game ships a stripped `mscorlib` that BepInEx
  cannot start without — [Setup](../Setup) installs both
- [ScamWYF.Modding.Core](../mod-lib), which owns the menu this tab appears in

### Installation

1. Install BepInEx and the corlib override with [Setup](../Setup).
2. Copy `ScamWYF.ModHandler.dll` into `BepInEx\plugins`.
3. Copy `ScamWYF.Modding.Core.dll` into `BepInEx\core`, if it is not already there.
4. Launch the game once so BepInEx writes its config.

Everything still works with this mod uninstalled — you simply do not get that one tab.

### Developer Setup

```powershell
git clone --recurse-submodules https://github.com/swyf-modding/Mod-Handler.git
cd Mod-Handler
.\build.ps1                # build and install
.\build.ps1 -NoCopy -Test  # build, run the tests, do not touch the game
```

The library is a submodule, built to its own dll in `BepInEx\core` rather than compiled into this one.
That is deliberate: the library owns the singletons — the hotkey table, the menu, the panel — so
embedding it gave every mod a private copy, and F1 opened two windows. `build.ps1` builds the library
first and compiles this mod against the dll it just produced, so the reference is never stale.

Roslyn runs directly; no .NET SDK is needed to build. The game install is auto-detected, or set
`SWYG_GAME_DIR`.

### Usage

Press **F1** in game for the mod menu, then pick the **Plugins** tab. The hotkey is configurable in
game, on the menu's **About** tab.

The menu itself belongs to the shared library, not to this mod. It has a tab for every mod sharing the
library, plus **Mods** (what is loaded and what is colliding) and **About**. This mod claims the
**Plugins** tab by key, replacing the library's placeholder rather than adding a second tab of the same
name.

| Control | What it does |
|---|---|
| Toggle | Moves the dll between `BepInEx\plugins` and `BepInEx\plugins_disabled` |
| Refresh | Re-reads both folders |
| Open plugins folder | Opens `BepInEx\plugins` |
| Open disabled folder | Opens `BepInEx\plugins_disabled` |

For plugins running on the shared library, live information is used instead of the file: real load
state, and the reason if one failed. A dll with no `[BepInPlugin]` — a shared library, most likely — is
listed with a note saying there is nothing to enable, rather than a toggle that would do nothing.

### Testing

```powershell
.\build.ps1 -NoCopy -Test
```

That compiles the mod against the library, then runs the library's API surface check and its behaviour
tests. The API check is the tier that matters here: a mod compiles perfectly well against a library
surface it does not actually use, and then fails at runtime.

To verify a session headlessly, without the game taking over your screen:

```powershell
& "C:\...\Scam With Your Friends.exe" -batchmode -nographics
```

BepInEx still initialises and writes `BepInEx\LogOutput.log`, so plugin loading can be checked without a
display. A preloader crash lands in `preloader_*.log` in the game root.

> [!TIP]
> `-batchmode` does not load a scene, so UI Toolkit has no themed `PanelSettings` to clone. The menu
> reports that it is unavailable and every mod still loads. That is the path to check first if the menu
> is missing rather than merely ugly.

No game DLL belongs in this repository or in a GitHub release.

---

## Compatibility

| Component | Verified Version |
|---|---|
| Scam With Your Friends | `v82-playtest` |
| Unity | `6000.3.10f1` |
| BepInEx | `5.4.23.5`, Mono preloader |
| ScamWYF.Modding.Core | built from the pinned submodule commit |
| C# language level | `7.3` |
| Platform | Windows x64 |

The mod compiles with `-nostdlib+` against the game's own `mscorlib.dll`, so a call that managed
stripping removed is a build error rather than a `MissingMethodException` in a session.

---

## Collisions

The **Mods** tab reports collisions between mods on the shared library. When two claim the same hotkey,
patch the same game method, or register the same `[BepInPlugin]` GUID, both owners are named:

```text
hotkey collision on F1 + Ctrl+Shift: My Mod + Another Mod
patch collision on KolkataApi.CompleteOpenRouterAsync: com.example.a + com.example.b
```

Patches do compose and both hotkeys do fire, so this is a warning rather than a breakage report — but a
prefix that quietly stops firing is miserable to track down, and this says it out loud instead.

---

## Project Structure

```text
src/ModHandler.cs     The Plugins tab: scanning, toggling, refreshing
build.ps1             Wraps the shared library's build script
vendor/               The shared library, as a submodule
```

`Mono.Cecil` comes from `BepInEx\core`, which the build script passes as an extra reference.

Deliberately absent: no `Update`, no `OnGUI`, and no window of this mod's own. `ScamMod` seals the Unity
callbacks, the shared runner does that work once for every mod, and the menu is the library's. This mod
used to draw its own IMGUI window; it does not any more, because the shared panel inherits the game's
theme, fonts and scaling — see
[the library's UI notes](../mod-lib#what-the-library-provides).

---

## Continuous Builds

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every push and pull request, in two
tiers, because the build compiles against the game's assemblies and those are not ours to redistribute:

| Job | Runner | What |
|---|---|---|
| `library-tests` | hosted, any OS | the library's behaviour tests — pure BCL, no game |
| `build` | self-hosted with the game, or by hand | the real compile, the API check, both dlls as an artefact |

`build` skips itself with a notice when there is no game rather than failing, so a green run never
quietly means "nothing was compiled". To use a labelled runner, set a repository variable:

```text
SWYM_RUNNER = self-hosted, windows, scamwyf
```

---

## Security

Please do not publish suspected vulnerabilities, credentials, authentication tickets, private game data,
or sensitive logs in a public issue. Please do not paste `BepInEx\LogOutput.log` contents in public: mods
in this ecosystem configure API keys with it.

---

## License

MIT — Copyright © 2026 Ras_rap. See [LICENSE](LICENSE).

The shared library this mod builds against is a separate work under its own licence, included here as a
submodule.

---

## Related Projects

| Project | What it is |
|---|---|
| [mod-lib](../mod-lib) | Shared library: base class, menu, config editor, hot reload, patch coordinator |
| [Setup](../Setup) | Installs BepInEx, the corlib override, and the vtable patches this game needs |
| [Launcher](../Launcher) | Installs, launches, and manages mods from outside the game |
| [AI-Backend](../AI-Backend) | Routes the game's AI calls to your own LLM |
