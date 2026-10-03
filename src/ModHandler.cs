using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Mono.Cecil;
using ScamWYF.Modding.Core;
using ScamWYF.Modding.Core.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.ModHandler
{
    /// <summary>
    /// A list of every BepInEx plugin installed, with a switch for each one.
    /// </summary>
    /// <remarks>
    /// This is a tab in the shared mod menu rather than a window of its own. That is the whole point of
    /// the menu: one hotkey reaches everything, so a mod does not need its own key and its own window
    /// to be discoverable.
    ///
    /// BepInEx's chainloader loads every DLL in BepInEx\plugins before any plugin's Awake runs, so a
    /// plugin cannot stop a sibling from loading in the same session. Disabling therefore moves the file
    /// to BepInEx\plugins_disabled and takes effect on the next launch, which is how mod managers
    /// generally do it. Nothing is deleted, and the list says so.
    ///
    /// Reading [BepInPlugin] out of a file's metadata means a disabled mod can still be listed by its
    /// real name, without loading its code.
    /// </remarks>
    // PluginBuildInfo.Version is a generated const, stamped by build.ps1 from this repository's git
    // tag - see mod-lib's Version.ps1. It was a "1.0.0" literal, which was correct on the first
    // release and wrong on every one after it, and was visible to players because this mod's own
    // Plugins tab reads the attribute back out of every dll in the folder.
    [BepInPlugin(PluginGuid, "Scam WYF Mod Handler", PluginBuildInfo.Version)]
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

        private readonly List<ModEntry> _mods = new List<ModEntry>();
        private string _status = "";
        private bool _restartNeeded;

        private string _pluginsDir;
        private string _disabledDir;

        protected override void OnModLoad()
        {
            GameBuild.CheckUnityVersion(ModLog, ModId, BuiltAgainst);

            var settings = new ModSettings(base.Config, ModLog, ModId);
            settings.Bind("General", "ShowRunningFirst", true,
                "List mods that are running this session before the ones that are only on disk.\n" +
                "Off lists them in one alphabetical list instead.");

            _pluginsDir = BepInEx.Paths.PluginPath;
            _disabledDir = Path.Combine(
                Path.GetDirectoryName(_pluginsDir) ?? BepInEx.Paths.BepInExRootPath, "plugins_disabled");

            // Take over the tab the library reserves for a plugin list. Registering by key replaces the
            // placeholder rather than adding a second tab.
            ModMenu.SetBuiltInPage(ModMenu.PluginsTab, "Plugins", BuildPluginsTab);

            Refresh();
            ModLog.LogInfo(string.Format(
                "{0} plugin file(s) found across BepInEx\\plugins and plugins_disabled. " +
                "The mod menu lists everything sharing this library; this tab lists plugin files.",
                _mods.Count));
        }

        protected override void OnModUnload()
        {
            // The menu tab, patches, hotkeys and windows are cleaned up by ScamMod.
        }

        // ---------------------------------------------------------------- the tab

        private void BuildPluginsTab(VisualElement host)
        {
            // The menu's page host already scrolls; a nested one would take the wheel first and make the
            // tab feel stuck.
            var scroll = host;

            var row = Widgets.WrapRow(scroll);
            Widgets.DescribedButton(row, "Refresh", "Re-read both plugin folders from disk", Refresh);
            Widgets.DescribedButton(row, "Open plugins folder", "Open BepInEx\\plugins",
                delegate { Open(_pluginsDir); });
            Widgets.DescribedButton(row, "Open disabled folder", "Open BepInEx\\plugins_disabled",
                delegate { Open(_disabledDir); });

            Widgets.Spacer(scroll, 4f);

            if (_mods.Count == 0)
            {
                Widgets.Note(scroll, "No plugin files found in BepInEx\\plugins or plugins_disabled.", false, false);
            }
            else
            {
                foreach (var mod in _mods) BuildRow(scroll, mod);
            }

            Widgets.Spacer(scroll, 6f);

            // Stated plainly, because "I clicked the switch and nothing happened" is the obvious first
            // confusion with a plugin manager.
            Widgets.Note(scroll,
                "Enabling or disabling moves the dll to the other folder. BepInEx loads every plugin " +
                "before any of them run, so a change cannot apply to the session already in progress - " +
                "it takes effect on the next launch.", _restartNeeded, false);

            if (!string.IsNullOrEmpty(_status)) Widgets.Label(scroll, _status, true);
        }

        private void BuildRow(VisualElement parent, ModEntry mod)
        {
            var box = Widgets.Column(parent);
            box.style.backgroundColor = UiTheme.Panel;
            box.style.borderTopLeftRadius = 4f;
            box.style.borderTopRightRadius = 4f;
            box.style.borderBottomLeftRadius = 4f;
            box.style.borderBottomRightRadius = 4f;
            box.style.paddingLeft = 6f;
            box.style.paddingRight = 6f;
            box.style.paddingTop = 4f;
            box.style.paddingBottom = 4f;
            box.style.marginBottom = 3f;

            var header = Widgets.Row(box);

            // A library rather than a plugin - a BepInEx shared assembly - has nothing to enable and
            // nothing to switch off, so it gets a label saying so instead of a dead toggle.
            var self = string.Equals(mod.PluginGuid, PluginGuid, StringComparison.Ordinal);
            var isPlugin = string.IsNullOrEmpty(mod.Problem);

            if (isPlugin && !self)
            {
                var toggle = Widgets.Toggle(header, "", mod.Enabled, delegate { Toggle(mod); });
                toggle.style.width = 20f;
                toggle.style.flexGrow = 0f;
                toggle.style.marginLeft = 0f;
            }
            else
            {
                var marker = Widgets.Label(header, mod.Enabled ? "[x]" : "[ ]", true);
                marker.style.width = 20f;
                marker.style.unityTextAlign = TextAnchor.MiddleCenter;
                marker.style.marginLeft = 0f;
            }

            var name = Widgets.Label(header, string.IsNullOrEmpty(mod.Version)
                ? mod.Name
                : mod.Name + "  " + mod.Version, false);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;

            Widgets.Filler(header);

            if (self)
            {
                var tag = Widgets.Label(header, "this mod", true);
                tag.style.fontSize = 11f;
            }
            else if (mod.Live != null && mod.Live.State == ModState.Active)
            {
                var tag = Widgets.Label(header, "running", true);
                tag.style.fontSize = 11f;
            }

            var detail = mod.Detail;
            if (!string.IsNullOrEmpty(detail))
            {
                var line = Widgets.Label(box, detail, true);
                line.style.fontSize = 11f;
                line.style.marginLeft = 20f;
            }
        }

        // ---------------------------------------------------------------- scanning

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
                _mods.Sort(delegate (ModEntry a, ModEntry b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch (Exception ex)
            {
                _status = "Could not read the plugin folders: " + ex.Message;
                ModLog.LogError(_status);
            }

            // The list is only interesting if it is on screen; rebuilding it is otherwise wasted work.
            if (ModMenu.IsOpen) ModMenu.Refresh();
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

                    entry.Problem = "not a plugin - probably a library, so nothing to enable";
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
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                if (File.Exists(target)) File.Delete(target);
                File.Move(mod.FileName, target);

                mod.FileName = target;
                mod.Enabled = !mod.Enabled;
                _restartNeeded = true;
                _status = string.Format("{0} {1}. Restart to apply.",
                    mod.Name, mod.Enabled ? "enabled" : "disabled");
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

        private static void Open(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                Application.OpenURL("file://" + path.Replace("\\", "/"));
            }
            catch (Exception ex)
            {
                // Opening a folder is a convenience; failing to do so is not worth more than a log line.
                Debug.Log("Could not open " + path + ": " + ex.Message);
            }
        }
    }
}