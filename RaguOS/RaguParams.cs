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
        [ConfigParam(name: "SpaceServer_IsSecure", valueType: typeof(bool), desc: "Port for incoming WS connection")]
        public bool SpaceServer_IsSecure = false;

        [ConfigParam(name: "SpaceServer_CC_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServer_CC_WSCertificate = null;

        [ConfigParam(name: "SpaceServer_Static_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServer_Static_WSCertificate = null;

        [ConfigParam(name: "SpaceServer_Actors_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServer_Actors_WSCertificate = null;

        [ConfigParam(name: "SpaceServer_Dynamic_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServer_Dynamic_WSCertificate = null;

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

        // If a new layer is dynamically created, add it here to allow connection parameter creation
        public void AddParameterLayer(string pLayer, Type pValueType, string pDefault) {
            LayerPortOffset.Add(pLayer, ++LayerPortOffsetLast);
            ConnectionParameterTypes.Add(pLayer, pValueType);
            ConnectionParameterDefaults.Add(pLayer, pDefault);
        }

        // Each layer gets a different connection port. If only port base is given, this is offset from that
        public readonly Dictionary<string, int> LayerPortOffset = new Dictionary<string, int>() {
            { SpaceServerCC.StaticLayerType, 0 },
            { SpaceServerStatic.StaticLayerType, 1 },
            { SpaceServerActors.StaticLayerType, 2 },
            { SpaceServerDynamic.StaticLayerType, 3 }
        };
        private int LayerPortOffsetLast = 3;

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
            { "DisableNaglesAlgorithm", "true" }
        };
        
        /// <summary>
        /// Return a value for a layer connection parameter.
        /// The parameters are computed based on values that can be in RegionInfo or in
        ///     the RaguOS.ini file. If specific port numbers are not specified, they are
        ///     computed based on a base port number given in one if the INI files.
        /// To find a value, this looks for value in order:
        ///     "SpaceServer.LAYER.PARAM" in RegionInfo
        ///     "SpaceServer.LAYER.PARAM" in RaguOS.ini
        ///     "SpaceServer.PARAM" in RegionInfo (if found, builds value based on LAYER)
        ///     "SpaceServer.PARAM" in RaguOS.ini (if found, builds value based on LAYER)
        /// For ports, if the above tests don't find anything, it looks for:
        ///     "SpaceServer.LAYER.BasePort" in RegionInfo
        ///     "SpaceServer.LAYER.BasePort" in RaguOS.ini
        ///     "SpaceServer.BasePort" in RegionInfo
        ///     "SpaceServer.BasePort" in RaguOS.ini
        /// If any of these are found, the port number is computed using that number and
        ///     the value in LayerPortOffset.
        /// </summary>
        /// <remarks>
        /// A connection is defined by the parameters:
        ///     WSConnectionHost: host to specify in URL. Default is "0.0.0.0"
        ///     WSPort
        ///     WSIsSecure: whether "ws:" or "wss:"
        ///     WSConnectionUrl: can be built from 'IsSecure', 'ConnectionHost', and 'Port'
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
                        val = FindConnectionParam(pContext, null, "BasePort");
                        if (val == null) {
                            throw new Exception(String.Format("Could not find port parameter for SpaceServer {0}", pLayer));
                        }
                        val = (Int32.Parse(val) + LayerPortOffset[pLayer]).ToString();
                        break;
                    case "WSConnectionUrl":
                        bool isSecure = GetConnectionParam<bool>(pContext, pLayer, "WSIsSecure");
                        val = isSecure ? "wss://" : "ws://"
                            + GetConnectionParam<string>(pContext, pLayer, "WSConnectionHost")
                            + ":"
                            + GetConnectionParam<string>(pContext, pLayer, "WSPort");
                        break;
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
            string parm = "SpaceServer_" + (pLayer == null ? "" : pLayer + "_") + pParam;
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
