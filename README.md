# ScamWYF.ModHandler

An in-game list of every BepInEx plugin installed, with a switch for each one — plus a live report
of anything the mods sharing [ScamWYF.Modding.Core](../scam-wyf-modding-lib) have collided on.

Built against **Unity 6000.3.10f1**, Mono, managed stripping **on**.

Part of a four-repo setup:

| Repo | What it is |
|---|---|
| [scam-wyf-modding-lib](../scam-wyf-modding-lib) | Shared library: base class, patch coordinator, hotkeys, IMGUI host |
| [scam-wyf-aibackend](../scam-wyf-aibackend) | Sends the game's AI calls to your own LLM |
| **scam-wyf-modhandler** (this one) | This mod |
| [scam-wyf-setup](../scam-wyf-setup) | BepInEx, Doorstop and the corlib patches the game needs |

Prerequisite: the game has to be loadable at all, which on this build means BepInEx plus the
unstripped corlib override. See [scam-wyf-setup](../scam-wyf-setup).

---

## Using it

Press **F1** in game (configurable) for the list.

Each row shows the mod's real name and version, read from its `[BepInPlugin]` metadata with Cecil —
so a **disabled** mod still shows its real name without its code ever being loaded. For mods running
on the shared library, live information is used instead: real load state, and the reason if one
failed.

| Button | What it does |
|---|---|
| Toggle | Moves the dll between `BepInEx\plugins` and `BepInEx\plugins_disabled` |
| Refresh | Re-reads both folders |
| Open folder | Opens `BepInEx\plugins` |
| Quit game | `Application.Quit()` |
| Close | Hides the window |

BepInEx's chainloader loads every plugin **before** any plugin's `Awake` runs, so a plugin cannot
stop a sibling from loading in the same session. Disabling therefore moves the file and takes effect
next launch, which is how mod managers generally do it. Nothing is deleted. The mod handler cannot
disable itself.

## Collisions

The section at the top is the reason this mod is on the shared library rather than rolling its own
key handling. When two mods on `ScamWYF.Modding.Core` claim the same hotkey, or patch the same game
method, or register the same `[BepInPlugin]` GUID, it is listed here with both owners named:

```
Collisions - these mods are stepping on each other:
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
The dll lands in `BepInEx\plugins`.

```powershell
git submodule update --init --recursive    # first-time clone only
```

The shared library is a submodule whose sources are compiled into this dll, so there is one file in
`BepInEx\plugins` and no version of anything to keep in step.

## How it uses the shared library

| Service | Used for |
|---|---|
| `ScamMod` | identity from `[BepInPlugin]`, load failures logged instead of fatal, unload handled |
| `Hotkeys` | F1, so two mods cannot both own it |
| `ImGuiHost` | the window, so two mods with overlays do not fight over `OnGUI` |
| `ModRegistry` | which mods are live, and the collision report |
| `ModSettings` | the toggle key, with a fallback when the config names a key that does not exist |
| `GameBuild` | Unity version check on load |

Deliberately no `Update` and no `OnGUI`: `ScamMod` seals them, and the shared runners do that work
once for every mod.

## Notes on this build

Unity's managed stripping removed IMGUI helpers the game itself never called. `GUILayout.TextArea`
is gone from `UnityEngine.IMGUIModule` in this build, and `GUIUtility.guiDepth` is read-only, so a
mod cannot force its own window z-order. Compiling with `-nostdlib+` against the game's own
`mscorlib.dll` is what turns that into a build error instead of a `MissingMethodException` at
runtime — see the build rule in the [library README](../scam-wyf-modding-lib#the-one-build-rule-that-matters).

`Mono.Cecil` comes from `BepInEx\core`, which the build script passes as an extra reference.

## Testing without the game taking over your screen

```powershell
& ".\Scam With Your Friends.exe" -batchmode -nographics
```

BepInEx still initialises and writes `BepInEx\LogOutput.log`, so plugin loading can be verified
headlessly. A preloader crash lands in `preloader_*.log` in the game root.