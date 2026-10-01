using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Mono.Cecil;
using ScamWYF.Modding.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScamWYF.ModHandler
{
    /// <summary>
    /// An in-game list of every BepInEx plugin installed, with a switch for each one, plus whatever
    /// the mods sharing ScamWYF.Modding.Core have collided on.
    /// </summary>
    /// <remarks>
    /// BepInEx's chainloader loads every DLL in BepInEx\plugins before any plugin's Awake runs, so a
    /// plugin cannot stop a sibling from loading in the same session. Disabling therefore moves the
    /// file to BepInEx\plugins_disabled and takes effect on the next launch, which is how mod
    /// managers generally do it. Nothing is deleted.
    ///
    /// The hotkey and the window belong to the shared library, not to this mod. That is the point:
    /// a second mod with an overlay does not end up fighting this one over OnGUI or over F1.
    /// </remarks>
    [BepInPlugin(PluginGuid, "Scam WYF Mod Handler", "1.0.0")]
    public sealed class ModHandlerPlugin : ScamMod
    {
        public const string PluginGuid = "com.community.scamwyf.modhandler";

        /// <summary>Unity build this mod was written against. Checked on load.</summary>
        private const string BuiltAgainst = "6000.3.10f1";

        private sealed class ModEntry
        {
            public string FileName;
            public string Name;
            public string Version;
            public string PluginGuid;

            /// <summary>Null unless this mod came up on the shared library.</summary>
            public ScamMod Live;

            public bool Enabled;
            public string Problem;

            public string Detail
            {
                get
                {
                    if (Live != null && Live.State == ModState.Failed) return "failed: " + Live.FailureReason;
                    if (!string.IsNullOrEmpty(Problem)) return Problem;
                    if (Live != null) return Live.ModId;
                    return PluginGuid;
                }
            }
        }

        private Vector2 _scroll;
        private readonly List<ModEntry> _mods = new List<ModEntry>();
        private ImGuiHost.Window _window;
        private string _status = "";
        private bool _restartNeeded;

        private string _pluginsDir;
        private string _disabledDir;

        protected override void OnModLoad()
        {
            GameBuild.CheckUnityVersion(ModLog, ModId, BuiltAgainst);

            var settings = new ModSettings(base.Config, ModLog, ModId);
            var toggle = settings.BindKey("General", "ToggleKey", Key.F1, "Key that opens and closes the mod list.");
            var showOnStart = settings.Bind("General", "ShowOnStart", false,
                "Open the mod list automatically when the game starts.");

            _pluginsDir = Paths.PluginPath;
            _disabledDir = Path.Combine(
                Path.GetDirectoryName(_pluginsDir) ?? Paths.BepInExRootPath, "plugins_disabled");

            _window = ImGuiHost.AddWindow(this, "Mods", new Rect(60f, 60f, 560f, 520f), Draw);
            _window.Visible = settings.Bool(showOnStart, false);

            Hotkeys.Register(this, settings.EnumValue(toggle, Key.F1), "Toggle the mod list", _window.Toggle);

            Refresh();
            ModLog.LogInfo(string.Format(
                "{0} plugin file(s) found. Press {1} in game. {2} mod(s) share this library.",
                _mods.Count, toggle.Value, ModRegistry.Count));
        }

        protected override void OnModUnload()
        {
            // The window and the hotkey belong to the shared library, which unregisters them.
        }

        // ---------------------------------------------------------------- mod list

        private void Refresh()
        {
            _mods.Clear();
            _status = "";

            var live = new Dictionary<string, ScamMod>();
            foreach (var mod in ModRegistry.Mods)
            {
                if (mod.ModId != null) live[mod.ModId] = mod;
            }

            try
            {
                Directory.CreateDirectory(_pluginsDir);
                Directory.CreateDirectory(_disabledDir);
                Collect(_pluginsDir, true, live);
                Collect(_disabledDir, false, live);
                _mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _status = "Could not read the plugin folders: " + ex.Message;
                ModLog.LogError(_status);
            }
        }

        private void Collect(string directory, bool enabled, Dictionary<string, ScamMod> live)
        {
            foreach (var path in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                var entry = new ModEntry
                {
                    FileName = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Version = "",
                    Enabled = enabled
                };

                ReadMetadata(path, entry);

                ScamMod running;
                if (entry.PluginGuid != null && live.TryGetValue(entry.PluginGuid, out running))
                {
                    // It came up on the shared library, so ask it rather than guess from metadata.
                    entry.Live = running;
                    entry.Name = running.DisplayName;
                    entry.Version = running.ModVersion;
                }

                _mods.Add(entry);
            }
        }

        /// <summary>
        /// Reads [BepInPlugin] straight out of the file's metadata, so a disabled mod can still be
        /// listed by its real name without loading its code.
        /// </summary>
        private static void ReadMetadata(string path, ModEntry entry)
        {
            try
            {
                using (var asm = AssemblyDefinition.ReadAssembly(path))
                {
                    foreach (var type in asm.MainModule.Types)
                    {
                        foreach (var attribute in type.CustomAttributes)
                        {
                            if (attribute.AttributeType.FullName != "BepInEx.BepInPlugin") continue;
                            if (attribute.ConstructorArguments.Count < 3) continue;
                            entry.PluginGuid = attribute.ConstructorArguments[0].Value as string;
                            entry.Name = attribute.ConstructorArguments[1].Value as string ?? entry.Name;
                            entry.Version = attribute.ConstructorArguments[2].Value as string ?? "";
                            return;
                        }
                    }

                    entry.Problem = "no [BepInPlugin] - probably a shared library, leave it enabled";
                }
            }
            catch (Exception ex)
            {
                entry.Problem = "unreadable: " + ex.Message;
            }
        }

        private void Toggle(ModEntry mod)
        {
            if (string.Equals(mod.PluginGuid, PluginGuid, StringComparison.Ordinal))
            {
                _status = "The mod handler cannot disable itself.";
                return;
            }

            var sourceRoot = mod.Enabled ? _pluginsDir : _disabledDir;
            var targetRoot = mod.Enabled ? _disabledDir : _pluginsDir;

            try
            {
                var relative = GetRelativePath(sourceRoot, mod.FileName);
                var target = Path.Combine(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.Delete(target);
                File.Move(mod.FileName, target);

                mod.FileName = target;
                mod.Enabled = !mod.Enabled;
                _restartNeeded = true;
                _status = string.Format("{0} {1}. Restart to apply.", mod.Name, mod.Enabled ? "enabled" : "disabled");
                ModLog.LogInfo(_status);
            }
            catch (Exception ex)
            {
                _status = "Could not move " + Path.GetFileName(mod.FileName) + ": " + ex.Message;
                ModLog.LogError(_status);
            }
        }

        private static string GetRelativePath(string root, string full)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            full = Path.GetFullPath(full);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length)
                : Path.GetFileName(full);
        }

        // ---------------------------------------------------------------- window

        private void Draw(int id)
        {
            var collisions = ModRegistry.CollisionReport();
            if (collisions != null)
            {
                GUILayout.Label("Collisions - these mods are stepping on each other:");
                // A Label per line, not GUILayout.TextArea: Unity's stripper removed TextArea from
                // this build because the game never called it, and the shared build rule
                // (-nostdlib+ against the game's own mscorlib) is what surfaces that here.
                foreach (var line in collisions.Split('\n'))
                {
                    if (line.Trim().Length > 0) GUILayout.Label("  " + line.TrimEnd());
                }
            }

            if (_mods.Count == 0)
            {
                GUILayout.Label("No plugins found in BepInEx\\plugins.");
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                foreach (var mod in _mods)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);

                    var self = string.Equals(mod.PluginGuid, PluginGuid, StringComparison.Ordinal);
                    GUI.enabled = !self;
                    var wanted = GUILayout.Toggle(mod.Enabled, "", GUILayout.Width(24f));
                    GUI.enabled = true;
                    if (wanted != mod.Enabled) Toggle(mod);

                    GUILayout.BeginVertical();
                    GUILayout.Label(string.IsNullOrEmpty(mod.Version) ? mod.Name : mod.Name + "  " + mod.Version);
                    var detail = mod.Detail;
                    if (!string.IsNullOrEmpty(detail)) GUILayout.Label("    " + detail);
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            if (_restartNeeded)
                GUILayout.Label("Changes apply after a restart.");
            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh")) Refresh();
            if (GUILayout.Button("Open folder"))
            {
                try { Application.OpenURL("file://" + _pluginsDir.Replace("\\", "/")); }
                catch (Exception ex) { _status = ex.Message; }
            }
            if (GUILayout.Button("Quit game")) Application.Quit();
            if (GUILayout.Button("Close")) _window.Visible = false;
            GUILayout.EndHorizontal();
        }
    }
}