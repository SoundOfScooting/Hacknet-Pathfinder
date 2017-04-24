using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Hacknet;
using Hacknet.Gui;
using Microsoft.Xna.Framework;
using Pathfinder.Event;
using Pathfinder.OS;
using Pathfinder.Util;

namespace Pathfinder
{
    static class Pathfinder
    {
        /*private static readonly Version AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        public static readonly Version Version = new Version(AssemblyVersion.Major,AssemblyVersion.Minor,1);
        private static Version latestVersion;*/

        class ExceptionInvalidId : ArgumentException
        {
            public ExceptionInvalidId(string msg) : base(msg) { }
        }

        private static Dictionary<string, IPathfinderMod> mods = new Dictionary<string, IPathfinderMod>();
        //private static bool currentSaveMissingMods = false;

        public static readonly string ModFolderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                                          + Path.DirectorySeparatorChar + "Mods";

        public static void init()
        {
            Logger.Verbose("Registering Pathfinder listeners");
            /*var verStr = Updater.GetString("https://api.github.com/repos/Arkhist/Hacknet-Pathfinder/releases/latest",
                                           "tag_name"); // Does not work, IDK
            if (Version.TryParse(verStr.Select(c => Char.IsDigit(c) || c == '.' ? c : (char)0).ToString(), out latestVersion)
               && latestVersion > Version)
                EventManager.RegisterListener<DrawMainMenuEvent>(DrawNewReleaseGraphic);*/

            EventManager.RegisterListener<CommandSentEvent>(OverwriteProbe);
            EventManager.RegisterListener<CommandSentEvent>(Command.Handler.CommandListener);

            EventManager.RegisterListener<DrawMainMenuEvent>(GUI.PathfinderMainMenu.drawMainMenu);
            EventManager.RegisterListener<DrawMainMenuButtonsEvent>(GUI.PathfinderMainMenu.drawPathfinderButtons);

            EventManager.RegisterListener<ExecutableExecuteEvent>(Executable.Handler.ExecutableListener);
            EventManager.RegisterListener<CommandSentEvent>(Executable.Handler.ExecutableListInsertListener);

            EventManager.RegisterListener<OSSaveWriteEvent>(ManageSaveXml);

            EventManager.RegisterListener<GameLoadContentEvent>(ExeInfoManager.LoadExecutableStruct);

            EventManager.RegisterListener<GameUnloadEvent>(UnloadMods);

            Logger.Verbose("Loading mods");
            LoadMods();
        }

        public static void testEventListener(PathfinderEvent pathfinderEvent)
        {
            Console.WriteLine("HEY ! LISTEN !!");
        }

        public static void LoadMods()
        {
            var separator = Path.DirectorySeparatorChar;

            Logger.Verbose("Checking/creating Mod folder '{0}'", ModFolderPath);
            if (!Directory.Exists(ModFolderPath))
                Directory.CreateDirectory(ModFolderPath);

            foreach (string dll in Directory.GetFiles(ModFolderPath + separator, "*.dll"))
            {
                try
                {
                    var modAssembly = Assembly.LoadFile(dll);
                    Type modType = null;
                    foreach (Type t in (modAssembly.GetExportedTypes().Where(t =>
                                                                     t.IsClass && !t.IsAbstract
                                                                     && typeof(IPathfinderMod).IsAssignableFrom(t)))
                            )
                    {
                        string name = null;
                        try
                        {
                            modType = t;
                            var modInstance = (IPathfinderMod)Activator.CreateInstance(modType);

                            var methodInfo = modType.GetProperty("Identifier").GetGetMethod();
                            if (methodInfo == null)
                                throw new NotSupportedException("Method 'Identifier' doesn't exist, mod '"
                                                                + Path.GetFileName(modAssembly.Location) + "' is invalid");
                            name = (string)methodInfo.Invoke(modInstance, null);
                            if (IsModLoaded(name))
                                throw new ExceptionInvalidId("Mod with identifier '" + name + "' is either already loaded or is reserved");
                            if (name.Contains('.'))
                                throw new ExceptionInvalidId("Mod identifier '" + name + "' contains a period, mod identifiers may not contain a period (.)");
                            Logger.Info("Loading mod '{0}'", name);

                            mods.Add(name, modInstance);

                            modInstance.Load();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Mod '{0}' of file '{1}' failed to load:\n\t{2}", t.FullName, Path.GetFileName(dll), ex);
                            if (mods.ContainsKey(name))
                                mods.Remove(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Mod file '{0}' failed to load:\n\t{1}", Path.GetFileName(dll), ex);
                }
            }
        }

        /// <summary>
        /// Determines whether a mod is loaded
        /// </summary>
        /// <returns><c>true</c>, if mod is loaded, <c>false</c> otherwise.</returns>
        /// <param name="id">Mod Identifier.</param>
        public static bool IsModLoaded(string id)
        {
            return id == "Pathfinder" || id == "Hacknet" || mods.ContainsKey(id);
        }

        internal static IPathfinderMod GetModByAssembly(Assembly asm)
        {
            foreach (var pair in mods)
            {
                if (pair.Value.GetType().Assembly == asm)
                    return pair.Value;
            }
            return null;
        }

        internal static void LoadModContent()
        {
            foreach (var mod in mods)
            {
                Logger.Verbose("Loading mod '{0}'s content", mod.Key);
                mod.Value.LoadContent();
            }
        }

        internal static List<string> LoadedModIdentifiers
        {
            get
            {
                return mods.Keys.ToList();
            }
        }

        internal static void ManageSaveXml(OSSaveWriteEvent e)
        {
            var i = e.SaveString.IndexOf("</HacknetSave>", StringComparison.Ordinal);
            string modListStr = "";
            foreach (var pair in mods)
                modListStr += "\t<Mod assembly='" + Path.GetFileName(pair.Value.GetType().Assembly.Location) + "'>" + pair.Key + "</Mod>\n";
            e.SaveString = e.SaveString.Insert(i, "\n<PathfinderMods>\n" + modListStr + "</PathfinderMods>\n");
        }

        internal static void UnloadMods(GameUnloadEvent e)
        {
            foreach (var mod in mods)
            {
                Logger.Verbose("Unloading mod '{0}'", mod.Key);
                mod.Value.Unload();
            }
        }

        internal static void OverwriteProbe(CommandSentEvent e)
        {
            if (e.Arguments[0] == "probe" || e.Arguments[0] == "nmap")
            {
                e.HandleReturn = true;
                e.IsCancelled = true;
                var os = e.OS;
                int i;
                var c = os.GetCurrentComputer();
                os.write(string.Concat("Probing ", c.ip, "...\n"));
                for (i = 0; i < 10; i++)
                {
                    Thread.Sleep(80);
                    os.writeSingle(".");
                }
                os.write("\nProbe Complete - Open ports:\n");
                os.write("---------------------------------");
                if (Port.Instance.compToInst.ContainsKey(c)) foreach (var ins in Port.Instance.compToInst[c])
                {
                    os.write("Port#: " + ins.Port.PortDisplay + " - " + ins.Port.PortName + (ins.Unlocked ? "OPEN" : ""));
                    Thread.Sleep(120);
                }
                for (i = 0; i < c.ports.Count; i++)
                {
                    os.write("Port#: " + c.GetDisplayPortNumberFromCodePort(c.ports[i]) + "  -  " + PortExploits.services[c.ports[i]]
                            + (c.portsOpen[i] > 0 ? " : OPEN" : ""));
                    Thread.Sleep(120);
                }
                os.write("---------------------------------");
                os.write("Open Ports Required for Crack : " + Math.Max(c.portsNeededForCrack + 1, 0));
                if (c.hasProxy)
                    os.write("Proxy Detected : " + (c.proxyActive ? "ACTIVE" : "INACTIVE"));
                if (c.firewall != null)
                    os.write("Firewall Detected : " + (c.firewall.solved ? "SOLVED" : "ACTIVE"));
            }
        }

        internal static void DrawNewReleaseGraphic(DrawMainMenuEvent e)
        {
            if(e.GameTime.ElapsedGameTime.Seconds % 3 != 0)
                TextItem.doFontLabel(new Vector2(300, 100), "New Release Up", GuiData.font, Color.White);
        }
    }
}
