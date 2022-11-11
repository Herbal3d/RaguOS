// Copyright (c) 2019 Robert Adams
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
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using org.herbal3d.cs.CommonUtil;

using Nini.Config;

namespace org.herbal3d.Ragu {

    public class ConfigParam : Attribute {
        public ConfigParam(string name, Type valueType, string desc = null) {
            this.name = name;
            this.valueType = valueType;
            this.desc = desc;
        }
        public string name;
        public Type valueType;
        public string desc;
    }
    public class RaguParams  {
        private readonly string _logHeader = "[RAGU PARAMS]";

        public RaguParams(BLogger pLogger, IConfig pConfig) {
            // If we were passed INI configuration, overlay the default values
            if (pConfig != null) {
                SetParameterConfigurationValues(pConfig);
            }
        }

        // =====================================================================================
        // =====================================================================================
        // List of all of the externally visible parameters.

        [ConfigParam(name: "Enabled", valueType: typeof(bool), desc: "If false, module is not enabled to operate")]
        public bool Enabled = false;

        [ConfigParam(name: "ExternalAccessHostname", valueType: typeof(string), desc: "Hostname for external clients it access. Computed if zero length")]
        public string ExternalAccessHostname = "";

        [ConfigParam(name: "ShouldEnforceUserAuth", valueType: typeof(bool), desc: "Whether to check and enforce user authentication in OpenConnection")]
        public bool ShouldEnforceUserAuth = true;

        [ConfigParam(name: "ShouldEnforceAssetAccessAuthorization", valueType: typeof(bool), desc: "All asset requests require an 'Authentication' header")]
        public bool ShouldEnforceAssetAccessAuthorization = false;

        [ConfigParam(name: "ShouldAliveCheckSessions", valueType: typeof(bool), desc: "Whether to start AliveCheck messages for open connections")]
        public bool ShouldAliveCheckSessions = false;

        // Layers are this WSPort + LayerPortOffset[LayerName]
        [ConfigParam(name: "SpaceServer_BasePort", valueType: typeof(int), desc: "Base port number for incoming WS connection")]
        public int SpaceServer_BasePort = 11440;
        [ConfigParam(name: "SpaceServer_WSIsSecure", valueType: typeof(bool), desc: "Whether WebSocket connections are secure")]
        public bool SpaceServer_WSIsSecure = false;
        [ConfigParam(name: "SpaceServer_WSExternalUrlTemplate", valueType: typeof(string), desc: "Template for external client access to WS services")]
        public string SpaceServer_WSExternalUrlTemplate = "ws://{0}:{1}/";

        [ConfigParam(name: "AssetUrlTemplate", valueType: typeof(string), desc: "Base of URL for external access to assets")]
        public string AssetUrlTemplate = "http://{0}:{1}{2}";
        [ConfigParam(name: "OutputDir", valueType: typeof(string), desc: "Base directory for Loden asset storage")]
        public string OutputDir = "./LodenAssets";
        [ConfigParam(name: "UseDeepFilenames", valueType: typeof(bool), desc: "Reference Loden assets in multi-directory deep file storage")]
        public bool UseDeepFilenames = true;

        [ConfigParam(name: "LogBaseFilename", valueType: typeof(string), desc: "where to send log files")]
        public string LogBaseFilename = null;
        [ConfigParam(name: "LogToConsole", valueType: typeof(bool), desc: "Whether to also log directly to the console")]
        public bool LogToConsole = false;
        [ConfigParam(name: "LogToFiles", valueType: typeof(bool), desc: "Log to special log file")]
        public bool LogToFile = false;
        [ConfigParam(name: "LogBuilding", valueType: typeof(bool), desc: "log detail BScene/BInstance object building")]
        public bool LogBuilding = true;
        [ConfigParam(name: "LogGltfBuilding", valueType: typeof(bool), desc: "output detailed gltf construction details")]
        public bool LogGltfBuilding = false;

        /*
         * Loop through all the ConfigParam attributes and, if the parameter exists in the configuration
         *    file, set the configuration file value.
         */
        public void SetParameterConfigurationValues(IConfig cfg) {
            if (cfg != null) {
                foreach (FieldInfo fi in this.GetType().GetFields()) {
                    foreach (Attribute attr in Attribute.GetCustomAttributes(fi)) {
                        ConfigParam cp = attr as ConfigParam;
                        if (cp != null) {
                            if (cfg.Contains(cp.name)) {
                                string configValue = cfg.GetString(cp.name);
                                fi.SetValue(this, ParamBlock.ConvertToObj(cp.valueType, configValue));
                            }
                        }
                    }
                }
            }
        }
        // Return a string version of a particular parameter value
        public string GetParameterValue(string pName) {
            var ret = String.Empty;
            foreach (FieldInfo fi in this.GetType().GetFields()) {
                foreach (Attribute attr in Attribute.GetCustomAttributes(fi)) {
                    ConfigParam cp = attr as ConfigParam;
                    if (cp != null) {
                        if (cp.name == pName) {
                            var val = fi.GetValue(this);
                            if (val != null) {
                                ret = val.ToString();
                            }
                            break;
                        }
                    }
                }
                if (ret != String.Empty) {
                    break;
                }
            }
            return ret;
        }
        // Set a parameter value
        public bool SetParameterValue(string pName, string pVal) {
            var ret = false;
            foreach (FieldInfo fi in this.GetType().GetFields()) {
                foreach (Attribute attr in Attribute.GetCustomAttributes(fi)) {
                    ConfigParam cp = attr as ConfigParam;
                    if (cp != null) {
                        if (cp.name == pName) {
                            fi.SetValue(this, ParamBlock.ConvertToObj(cp.valueType, pVal));
                            ret = true;
                            break;
                        }
                    }
                }
                if (ret) {
                    break;
                }
            }
            return ret;
        }
        // Return a list of all the parameters and their descriptions
        public Dictionary<string, string> ListParameters() {
            var ret = new Dictionary<string,string>();
            foreach (FieldInfo fi in this.GetType().GetFields()) {
                foreach (Attribute attr in Attribute.GetCustomAttributes(fi)) {
                    ConfigParam cp = attr as ConfigParam;
                    if (cp != null) {
                        ret.Add(cp.name, cp.desc);
                    }
                }
            }
            return ret;
        }

        // Each layer gets a different connection port. If only port base is given, this is offset from that
        private int layerPortOffet = 0;
        private int NextLayerPortOffset() {
            return layerPortOffet++;
        }

        // Parameter values are "string" unless specified there
        public readonly Dictionary<string, Type> ConnectionParameterTypes = new Dictionary<string, Type>() {
            { "WSPort", typeof(int) },
            { "WSIsSecure", typeof(bool)},
            { "DisableNaglesAlgorithm", typeof(bool)}
        };

        public readonly Dictionary<string, string> ConnectionParameterDefaults = new Dictionary<string, string>() {
            { "WSPort", "14000" },
            { "WSIsSecure", "false"},
            { "WSConnectionHost", "0.0.0.0"},
            { "WSCertificate", ""},
            { "WSExternalUrlTemplate", "ws://{0}:{1}/"},
            { "WSDisableNaglesAlgorithm", "true" }
        };
        
        /// <summary>
        /// Return a value for a layer connection parameter.
        /// The parameters are computed based on values that can be in RegionInfo or in
        ///     the RaguOS.ini file. If specific port numbers are not specified, they are
        ///     computed based on a base port number given in one if the INI files.
        /// To find a value, this looks for value in order:
        ///     "SpaceServer_LAYER_PARAM" in RegionInfo
        ///     "SpaceServer_LAYER_PARAM" in RaguOS.ini
        ///     "SpaceServer_PARAM" in RegionInfo (if found, builds value based on LAYER)
        ///     "SpaceServer_PARAM" in RaguOS.ini (if found, builds value based on LAYER)
        /// For ports, if the above tests don't find anything, it looks for:
        ///     "SpaceServer_LAYER_BasePort" in RegionInfo
        ///     "SpaceServer_LAYER_BasePort" in RaguOS.ini
        ///     "SpaceServer_BasePort" in RegionInfo
        ///     "SpaceServer_BasePort" in RaguOS.ini
        /// If any of these are found, the port number is computed using that number and
        ///     the value in LayerPortOffset.
        /// </summary>
        /// <remarks>
        /// A connection is defined by the parameters:
        ///     WSConnectionHost: host to specify in URL. Default is "0.0.0.0"
        ///     WSPort
        ///     WSIsSecure: whether WebSocket reader is set for secure handshake or not
        ///     WSExternalUrlTemplate: default: "wss://{0}:{1}/wss/". Host and port replaced.
        ///     DisableNaglesAlgorithm
        /// </remarks>
        /// <param name="pContext"></param>
        /// <param name="pLayer">Name of layer getting connection for</param>
        /// <param name="pParam">The parameter name. Used to build names to look up in INI files</param>
        /// <returns>Parameter value. Note that the type will change depending on the parameter</returns>
        public T GetConnectionParam<T>(RaguContext pContext, string pLayer, string pParam) {
            string val = FindConnectionParam(pContext, pLayer, pParam);
            if (val == null) {
                // Couldn't find the value so see if it's one we can build or default
                switch (pParam) {
                    case "WSPort":
                        var basePort = FindConnectionParam(pContext, null, "BasePort");
                        val = (Int32.Parse(basePort) + NextLayerPortOffset()).ToString();
                        break;
                    /* This is deprecated and not used by anyone
                    case "WSConnectionUrl":
                        bool isSecure = GetConnectionParam<bool>(pContext, pLayer, "WSIsSecure");
                        val = isSecure ? "wss://" : "ws://"
                            + GetConnectionParam<string>(pContext, pLayer, "WSConnectionHost")
                            + ":"
                            + GetConnectionParam<string>(pContext, pLayer, "WSPort");
                        break;
                    */
                    default:
                        if (ConnectionParameterDefaults.ContainsKey(pParam)) {
                            val = ConnectionParameterDefaults[pParam];
                        }
                        break;
                }
            }
            // pContext.log.Debug("{0} GetConnectionParam: l={1}, p={2}, ret={3}", _logHeader, pLayer, pParam, val);
            return ParamBlock.ConvertTo<T>(val);
        }
        // Search the RegionInfo and RaguParams for potential value and return the string value or null;
        // Looks for "SpaceServer_LAYER_PARAM" and then "SPACESERVER_PARAM"
        private string FindConnectionParam(RaguContext pContext, string pLayer, string pParam) {
            string val = null;
            string parm = "SpaceServer_" + (pLayer == null ? "" : (pLayer + "_")) + pParam;
            val = GetRegionInfoParam(pContext, parm);
            if (val == null) {
                // No explicit value in the RegionInfo file. One in the Ragu.ini file?
                val = this.GetParameterValue(parm);
                if (val == null || val.Length == 0) {
                    // No explicit value anywhere. Try the general value
                    parm = "SpaceServer_" + pParam;
                    val = GetRegionInfoParam(pContext, parm);
                    if (val == null) {
                        val = this.GetParameterValue(parm);
                        if (val == null || val.Length == 0) {
                            // No general value. Return "not found" as null
                            val = null;
                        }
                    }
                }
            }
            return val;
        }
        // Get RegionInfo param and remember what we got so we don't fetch multiple times.
        // This is done because RegionInfo outputs log messages if values are not found.
        private Dictionary<string, string> RememberConnectionParams = new Dictionary<string, string>();
        private string GetRegionInfoParam(RaguContext pContext, string parm) {
            string val = null;
            if (!RememberConnectionParams.TryGetValue("RegionInfo-" + parm, out val)) {
                val = pContext.scene.RegionInfo.GetSetting(parm);
                RememberConnectionParams.Add("RegionInfo-" + parm, val);
            }
            return val;
        }
    }
}
