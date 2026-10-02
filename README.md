# ScamWYF.ModHandler

An in-game list of every BepInEx plugin file installed, with a switch for each one.

Built against **Unity 6000.3.10f1**, Mono, managed stripping **on**.

Part of a four-repo setup:

| Repo | What it is |
|---|---|
| [scam-wyf-modding-lib](../scam-wyf-modding-lib) | Shared library: base class, menu, config, patch coordinator, hotkeys |
| [scam-wyf-aibackend](../scam-wyf-aibackend) | Sends the game's AI calls to your own LLM |
| **scam-wyf-modhandler** (this one) | This mod |
| [scam-wyf-setup](../scam-wyf-setup) | BepInEx, Doorstop and the corlib patches the game needs |

Prerequisite: the game has to be loadable at all, which on this build means BepInEx plus the
unstripped corlib override. See [scam-wyf-setup](../scam-wyf-setup).

---

## Using it

Press **F1** in game for the mod menu, then pick the **Plugins** tab. The hotkey is configurable
in game, on the menu's **About** tab.

That menu belongs to the shared library, not to this mod. It has a tab for every mod that shares
the library, plus **Mods** (what is loaded and what is colliding) and **About**. This mod claims the
**Plugins** tab for the list of plugin *files* on disk. Everything still works with this mod
uninstalled — you just do not get that one tab.

Each row shows the plugin's real name and version, read from its `[BepInPlugin]` metadata with
Cecil, so a **disabled** plugin still shows its real name without its code ever being loaded. For
plugins running on the shared library, live information is used instead: real load state, and the
reason if one failed.

| Button | What it does |
|---|---|
| Toggle | Moves the dll between `BepInEx\plugins` and `BepInEx\plugins_disabled` |
| Refresh | Re-reads both folders |
| Open plugins folder | Opens `BepInEx\plugins` |
| Open disabled folder | Opens `BepInEx\plugins_disabled` |

BepInEx's chainloader loads every plugin **before** any plugin's `Awake` runs, so a plugin cannot
stop a sibling from loading in the same session. Disabling therefore moves the file and takes
effect next launch, which is how mod managers generally do it. Nothing is deleted, and the tab says
so rather than leaving somebody to wonder why their click did nothing. The mod handler cannot
disable itself.

A dll with no `[BepInPlugin]` — a shared library, most likely — is listed with a note saying there
is nothing to enable, rather than a toggle that would do nothing.

## Collisions

The **Mods** tab is where collisions are reported. When two mods on `ScamWYF.Modding.Core` claim the
same hotkey, patch the same game method, or register the same `[BepInPlugin]` GUID, it is listed
with both owners named:

```
hotkey collision on F1 + Ctrl+Shift: My Mod + Another Mod
patch collision on KolkataApi.CompleteOpenRouterAsync: com.example.a + com.example.b
```

Patches do compose and both hotkeys do fire, so this is a warning rather than a breakage report —
but a prefix that quietly stops firing is miserable to track down, and this says it out loud instead.

## Build and install

```powershell
.\build.ps1
.\build.ps1 -CscDll C:\path\to\roslyn\csc.dll      # if you have no compiler on PATH
.\build.ps1 -GameDir "C:\...\steamapps\common\Scam With Your Friends"
```

Roslyn runs directly; no .NET SDK needed. The game install is auto-detected, or set `SWYG_GAME_DIR`.
The build puts this mod's dll in `BepInEx\plugins` and the shared library in `BepInEx\core`.

```powershell
git submodule update --init --recursive    # first-time clone only
```

The library is a submodule built to its own dll rather than compiled into this one, because it owns the
singletons — the hotkey table, the menu, the panel. Embedded, every mod would have had a private copy,
and F1 would have opened two windows. Build both from one command; there is still nothing to keep in
step by hand.

## How it uses the shared library

| Service | Used for |
|---|---|
| `ScamMod` | identity from `[BepInPlugin]`, load failures logged instead of fatal, unload handled |
| `ModMenu` | the tab this mod claims, keyed so it replaces the library's placeholder rather than adding a second one |
| `Widgets` | every row and button, drawn in the base game's own UI theme |
| `ModRegistry` | which mods are live, for the Mods tab |
| `GameBuild` | Unity version check on load |

Deliberately no `Update`, no `OnGUI`, and no window of its own: `ScamMod` seals the Unity callbacks,
the shared runner does that work once for every mod, and the menu is the library's.

## Notes on this build

This mod used to draw its own IMGUI window. It does not any more, because the game builds its menus
with UI Toolkit and the shared panel inherits the game's theme, fonts and scaling — see the
[library README](../scam-wyf-modding-lib#the-ui--the-base-games-own-not-a-lookalike). Compiling with
`-nostdlib+` against the game's own `mscorlib.dll` is what turns a call that managed stripping
removed into a build error instead of a `MissingMethodException` at runtime; see
[the build rule](../scam-wyf-modding-lib#the-one-build-rule-that-matters).

`Mono.Cecil` comes from `BepInEx\core`, which the build script passes as an extra reference.

## Testing without the game taking over your screen

```powershell
& ".\Scam With Your Friends.exe" -batchmode -nographics
```

BepInEx still initialises and writes `BepInEx\LogOutput.log`, so plugin loading can be verified
headlessly. A preloader crash lands in `preloader_*.log` in the game root.

Note that `-batchmode` does not load a scene, so UI Toolkit has no themed `PanelSettings` to clone.
The menu reports that it is unavailable and every mod still loads — that path is the one to check
first if the menu is missing rather than ugly.