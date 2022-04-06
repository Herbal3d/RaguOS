// Copyright (c) 2022 Robert Adams
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Reflection;
using System.Collections.Generic;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace org.herbal3d.Ragu {
    /// <summary>
    /// </summary>
    /// <remarks>
    /// </remarks>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RaguConsole")]
    public class RaguConsole : ISharedRegionModule
    {
        private const string LogHeader = "RaguCommand";
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_scenes = new List<Scene>();
        private static bool m_commandsLoaded = false;

        #region ISharedRegionModule
        public string Name { get { return "Ragu console commands"; } }

        public Type ReplaceableInterface { get { return null; } }

        public void Initialise(IConfigSource source)
        {
            m_log.DebugFormat("{0}: INITIALIZED MODULE", LogHeader);
        }

        public void PostInitialise()
        {
            m_log.DebugFormat("[{0}: POST INITIALIZED MODULE", LogHeader);
            InstallInterfaces();
        }

        public void Close()
        {
            m_log.DebugFormat("{0}: CLOSED MODULE", LogHeader);
        }

        public void AddRegion(Scene scene)
        {
            m_log.DebugFormat("{0}: REGION {1} ADDED", LogHeader, scene.RegionInfo.RegionName);
            m_scenes.Add(scene);
        }

        public void RemoveRegion(Scene scene)
        {
            m_log.DebugFormat("{0}: REGION {1} REMOVED", LogHeader, scene.RegionInfo.RegionName);
            if (m_scenes.Contains(scene))
                m_scenes.Remove(scene);
        }

        public void RegionLoaded(Scene scene)
        {
            m_log.DebugFormat("{0}: REGION {1} LOADED", LogHeader, scene.RegionInfo.RegionName);
        }
        #endregion INonSharedRegionModule

        private const string getInvocation = "ragu get [<param>|ALL]";
        private const string setInvocation = "ragu set <param> [<value>|TRUE|FALSE] [localID|ALL]";
        private const string listInvocation = "ragu list";
        private void InstallInterfaces()
        {
            if (!m_commandsLoaded)
            {
                MainConsole.Instance.Commands.AddCommand(
                    "Regions", false, "ragu list",
                    listInvocation,
                    "List state of Ragu connections",
                    ProcessRaguList);

                MainConsole.Instance.Commands.AddCommand(
                    "Regions", false, "ragu set",
                    setInvocation,
                    "Set Ragu parameter",
                    ProcessRaguSet);

                MainConsole.Instance.Commands.AddCommand(
                    "Regions", false, "ragu get",
                    getInvocation,
                    "Get Ragu parameter",
                    ProcessRaguSet);

                m_commandsLoaded = true;
            }
        }

        // TODO: extend get so you can get a value from an individual localID
        private void ProcessRaguGet(string module, string[] cmdparms)
        {
            if (cmdparms.Length != 3)
            {
                WriteError("Parameter count error. Invocation: " + getInvocation);
                return;
            }
            if (SceneManager.Instance == null || SceneManager.Instance.CurrentScene == null) {
                WriteError("Error: no region selected. Use 'change region' to select a region.");
                return;
            }
            Scene scene = SceneManager.Instance.CurrentScene;

            RaguRegion ragu = scene.RequestModuleInterface<RaguRegion>();
            if (ragu != null) {
                string parm = cmdparms[2];
                WriteOut("  {0}: {1}", parm, ragu.RContext.parms.GetParameterValue(parm));
            }
        }

        private void ProcessRaguSet(string module, string[] cmdparms)
        {
            if (cmdparms.Length < 4 || cmdparms.Length > 5) {
                WriteError("Parameter count error. Invocation: " + getInvocation);
                return;
            }
            if (SceneManager.Instance == null || SceneManager.Instance.CurrentScene == null) {
                WriteError("Error: no region selected. Use 'change region' to select a region.");
                return;
            }
            Scene scene = SceneManager.Instance.CurrentScene;

            RaguRegion ragu = scene.RequestModuleInterface<RaguRegion>();
            if (ragu != null) {
                try {
                    var parm = cmdparms[2];
                    var valparm = cmdparms[3].ToLower();
                    ragu.RContext.parms.SetParameterValue(parm, valparm);
                    WriteOut("  {0}: {1}", parm, ragu.RContext.parms.GetParameterValue(parm));
                }
                catch
                {
                    WriteError("  Error parsing parameters. Invocation: " + setInvocation);
                    return;
                }
            }
        }

        private void ProcessRaguList(string module, string[] cmdparms)
        {
            if (SceneManager.Instance == null || SceneManager.Instance.CurrentScene == null) {
                WriteError("Error: no region selected. Use 'change region' to select a region.");
                return;
            }
            Scene scene = SceneManager.Instance.CurrentScene;

            RaguRegion ragu = scene.RequestModuleInterface<RaguRegion>();
            if (ragu != null)
            {
                var parms = ragu.RContext.parms;
                Dictionary<string, string> parmList = parms.ListParameters();
                WriteOut("Available Ragu parameters:");
                foreach (var kvp in parmList)
                {
                    WriteOut("   {0}: {1}", kvp.Key, kvp.Value);
                }
            }
            else
            {
                WriteError("Current regions does not have Ragu enabled");
            }
            return;
        }

        private void WriteOut(string msg, params object[] args)
        {
            // m_log.InfoFormat(msg, args);
            MainConsole.Instance.Output(msg, args);
        }

        private void WriteError(string msg, params object[] args)
        {
            // m_log.ErrorFormat(msg, args);
            MainConsole.Instance.Output(msg, args);
        }
    }
}
