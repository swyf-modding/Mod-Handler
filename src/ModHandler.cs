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
// PluginBuildInfo.Version is a generated const, stamped by build.ps1 from this repository's git tag -
    // see mod-lib's Version.ps1. It was a "1.0.0" literal, which was correct on the first release and
    // silently wrong on every one after it, and was visible to players because this mod's own Plugins tab
    // reads the attribute back out of every dll in the folder.
    //
    // It must stay parseable by System.Version: two to four dot-separated integers and nothing else.
    // BepInEx constructs `new System.Version(<this string>)` inside a try/catch, and a plugin whose
    // version will not parse is skipped with "version is invalid" in the log - no error anywhere else,
    // and nothing loads. The commit lives in PluginBuildInfo.Informational, which is why there are two.
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

            // The library now provides a built-in Plugins tab. This mod no longer replaces it;
            // the native tab shows all plugin files with enable/disable. This mod's additional
            // features (running status, Cecil metadata) would need a separate tab or integration.
            // ModMenu.SetBuiltInPage(ModMenu.PluginsTab, "Plugins", BuildPluginsTab);

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

        // The plugin list lives in the library's built-in Plugins tab now. This mod's own tab is
        // its settings page, added automatically by ScamMod.

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
    }
}