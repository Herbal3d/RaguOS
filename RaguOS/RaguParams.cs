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

        [ConfigParam(name: "SpaceServerCC_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerCC_IsSecure = false;
        [ConfigParam(name: "SpaceServerCC_WSConnectionHost", valueType: typeof(string), desc: "Host to use when making WS connection")]
        public string SpaceServerCC_WSConnectionHost = "0.0.0.0";
        [ConfigParam(name: "SpaceServerCC_WSConnectionPort", valueType: typeof(int), desc: "Port for incoming WS connection")]
        public int SpaceServerCC_WSConnectionPort = 11440;
        [ConfigParam(name: "SpaceServerCC_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerCC_WSCertificate = null;
        [ConfigParam(name: "SpaceServerCC_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerCC_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "SpaceServerStatic_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerStatic_IsSecure = false;
        [ConfigParam(name: "SpaceServerStatic_WSConnectionHost", valueType: typeof(string), desc: "Host to use when making WS connection")]
        public string SpaceServerStatic_WSConnectionHost = "0.0.0.0";
        [ConfigParam(name: "SpaceServerStatic_WSConnectionPort", valueType: typeof(int), desc: "Port for incoming WS connection")]
        public int SpaceServerStatic_WSConnectionPort = 11441;
        [ConfigParam(name: "SpaceServerStatic_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerStatic_WSCertificate = null;
        [ConfigParam(name: "SpaceServerStatic_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerStatic_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "SpaceServerActors_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerActors_IsSecure = false;
        [ConfigParam(name: "SpaceServerActors_WSConnectionHost", valueType: typeof(string), desc: "Host to use when making WS connection")]
        public string SpaceServerActors_WSConnectionHost = "0.0.0.0";
        [ConfigParam(name: "SpaceServerActors_WSConnectionPort", valueType: typeof(int), desc: "Port for incoming WS connection")]
        public int SpaceServerActors_WSConnectionPort = 11442;
        [ConfigParam(name: "SpaceServerActors_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerActors_WSCertificate = null;
        [ConfigParam(name: "SpaceServerActors_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerActors_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "SpaceServerDynamic_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerDynamic_IsSecure = false;
        [ConfigParam(name: "SpaceServerDynamic_WSConnectionHost", valueType: typeof(string), desc: "Host to use when making WS connection")]
        public string SpaceServerDynamic_WSConnectionHost = "0.0.0.0";
        [ConfigParam(name: "SpaceServerDynamic_WSConnectionPort", valueType: typeof(int), desc: "Port for incoming WS connection")]
        public int SpaceServerDynamic_WSConnectionPort = 11443;
        [ConfigParam(name: "SpaceServerDynamic_WSCertificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerDynamic_WSCertificate = null;
        [ConfigParam(name: "SpaceServerDynamic_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerDynamic_DisableNaglesAlgorithm = true;


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
    }
}
