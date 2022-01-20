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
        [ConfigParam(name: "SpaceServerCC_SecureConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerCC_SecureConnectionURL = "wss://0.0.0.0:11440";
        [ConfigParam(name: "SpaceServerCC_Certificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerCC_Certificate = null;
        [ConfigParam(name: "SpaceServerCC_WebSocketPort", valueType: typeof(int), desc: "Port to open for inbound connection")]
        public int SpaceServerCC_WebSocketPort = 11440;
        [ConfigParam(name: "SpaceServerCC_ConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerCC_ConnectionURL = "ws://0.0.0.0:11440";
        [ConfigParam(name: "SpaceServerCC_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerCC_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "SpaceServerStatic_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerStatic_IsSecure = false;
        [ConfigParam(name: "SpaceServerStatic_SecureConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerStatic_SecureConnectionURL = "wss://0.0.0.0:11441";
        [ConfigParam(name: "SpaceServerStatic_Certificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerStatic_Certificate = null;
        [ConfigParam(name: "SpaceServerStatic_WebSocketPort", valueType: typeof(int), desc: "Port to open for inbound connection")]
        public int SpaceServerStatic_WebSocketPort = 11441;
        [ConfigParam(name: "SpaceServerStatic_ConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerStatic_ConnectionURL = "ws://0.0.0.0:11441";
        [ConfigParam(name: "SpaceServerStatic_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerStatic_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "SpaceServerActors_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerActors_IsSecure = false;
        [ConfigParam(name: "SpaceServerActors_SecureConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerActors_SecureConnectionURL = "wss://0.0.0.0:11442";
        [ConfigParam(name: "SpaceServerActors_Certificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerActors_Certificate = null;
        [ConfigParam(name: "SpaceServerActors_WebSocketPort", valueType: typeof(int), desc: "Port to open for inbound connection")]
        public int SpaceServerActors_WebSocketPort = 11442;
        [ConfigParam(name: "SpaceServerActors_ConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerActors_ConnectionURL = "ws://0.0.0.0:11442";
        [ConfigParam(name: "SpaceServerActors_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerActors_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "SpaceServerDynamic_IsSecure", valueType: typeof(bool), desc: "Whether to accept only secure connections")]
        public bool SpaceServerDynamic_IsSecure = false;
        [ConfigParam(name: "SpaceServerDynamic_SecureConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerDynamic_SecureConnectionURL = "wss://0.0.0.0:11443";
        [ConfigParam(name: "SpaceServerDynamic_Certificate", valueType: typeof(string), desc: "Certificate to accept for secure inbound connection")]
        public string SpaceServerDynamic_Certificate = null;
        [ConfigParam(name: "SpaceServerDynamic_WebSocketPort", valueType: typeof(int), desc: "Port to open for inbound connection")]
        public int SpaceServerDynamic_WebSocketPort = 11443;
        [ConfigParam(name: "SpaceServerDynamic_ConnectionURL", valueType: typeof(string), desc: "URL to use to create inbound connection")]
        public string SpaceServerDynamic_ConnectionURL = "ws://0.0.0.0:11443";
        [ConfigParam(name: "SpaceServerDynamic_DisableNaglesAlgorithm", valueType: typeof(bool), desc: "Whether to enable/disable outbound delay")]
        public bool SpaceServerDynamic_DisableNaglesAlgorithm = true;

        [ConfigParam(name: "OutputDir", valueType: typeof(string), desc: "Base directory for Loden asset storage")]
        public string OutputDir = "./LodenAssets";
        [ConfigParam(name: "UseDeepFilenames", valueType: typeof(bool), desc: "Reference Loden assets in multi-directory deep file storage")]
        public bool UseDeepFilenames = true;

        [ConfigParam(name: "LogBaseFilename", valueType: typeof(string), desc: "where to send log files")]
        public string LogBaseFilename = null;
        [ConfigParam(name: "LogToConsole", valueType: typeof(bool), desc: "where to send log files")]
        public bool LogToConsole = false;
        [ConfigParam(name: "LogToFiles", valueType: typeof(bool), desc: "where to send log files")]
        public bool LogToFile = false;
        [ConfigParam(name: "LogBuilding", valueType: typeof(bool), desc: "log detail BScene/BInstance object building")]
        public bool LogBuilding = true;
        [ConfigParam(name: "LogGltfBuilding", valueType: typeof(bool), desc: "output detailed gltf construction details")]
        public bool LogGltfBuilding = false;


        /*
        private void DefineParameters() {
            base.ParameterDefinitions = new ParameterDefnBase[] {
                new ParameterDefn<bool>("Enabled", "If false, module is not enabled to operate",
                    false),

                new ParameterDefn<string>("ExternalAccessHostname", "Hostname for external clients it access. Computed if zero length",
                    ""),
                new ParameterDefn<bool>("ShouldEnforceUserAuth", "Whether to check and enforce user authentication in OpenConnection",
                    true),
                new ParameterDefn<bool>("ShouldEnforceAssetAccessAuthorization", "All asset requests require an 'Authentication' header",
                    false),
                new ParameterDefn<bool>("ShouldAliveCheckSessions", "Whether to start AliveCheck messages for open connections",
                    false),

                new ParameterDefn<bool>("SpaceServerCC_IsSecure", "Whether to accept only secure connections",
                    false),
                new ParameterDefn<string>("SpaceServerCC_SecureConnectionURL", "URL to use to create inbound connection",
                    "wss://0.0.0.0:11440"),
                new ParameterDefn<string>("SpaceServerCC_Certificate", "Certificate to accept for secure inbound connection",
                    ""),
                new ParameterDefn<int>("SpaceServerCC_WebSocketPort", "URL to use to create inbound connection",
                    11440),
                new ParameterDefn<string>("SpaceServerCC_ConnectionURL", "URL to use to create inbound connection",
                    "ws://0.0.0.0:11440"),
                new ParameterDefn<bool>("SpaceServerCC_DisableNaglesAlgorithm", "Whether to enable/disable outbound delay",
                    true),

                new ParameterDefn<bool>("SpaceServerStatic_IsSecure", "Whether to accept only secure connections",
                    false),
                new ParameterDefn<string>("SpaceServerStatic_SecureConnectionURL", "URL to use to create inbound connection",
                    "wss://0.0.0.0:11441"),
                new ParameterDefn<string>("SpaceServerStatic_Certificate", "Certificate to accept for secure inbound connection",
                    ""),
                new ParameterDefn<string>("SpaceServerStatic_ConnectionURL", "URL to use to create inbound connection",
                    "ws://0.0.0.0:11441"),
                new ParameterDefn<bool>("SpaceServerStatic_DisableNaglesAlgorithm", "Whether to enable/disable outbound delay",
                    true),

                new ParameterDefn<bool>("SpaceServerActors_IsSecure", "Whether to accept only secure connections",
                    false),
                new ParameterDefn<string>("SpaceServerActors_SecureConnectionURL", "URL to use to create inbound connection",
                    "wss://0.0.0.0:11442"),
                new ParameterDefn<string>("SpaceServerActors_Certificate", "Certificate to accept for secure inbound connection",
                    ""),
                new ParameterDefn<string>("SpaceServerActors_ConnectionURL", "URL to use to create inbound connection",
                    "ws://0.0.0.0:11442"),
                new ParameterDefn<bool>("SpaceServerActors_DisableNaglesAlgorithm", "Whether to enable/disable outbound delay",
                    true),

                new ParameterDefn<string>("OutputDir", "Base directory for Loden asset storage",
                    "./LodenAssets"),
                new ParameterDefn<bool>("UseDeepFilenames", "Reference Loden assets in multi-directory deep file storage",
                    true),

                new ParameterDefn<bool>("LogBuilding", "log detail BScene/BInstance object building",
                    true),
                new ParameterDefn<bool>("LogGltfBuilding", "output detailed gltf construction details",
                    false),
            };
        }

        // =====================================================================================
        // =====================================================================================
        // Get user set values out of the ini file.
        public  void SetParameterConfigurationValues(IConfig cfg, IBLogger pLogger)
        {
            foreach (ParameterDefnBase parm in ParameterDefinitions)
            {
                // _logger.log.DebugFormat("{0}: parm={1}, desc='{2}'", _logHeader, parm.name, parm.desc);
                parm.logger = pLogger;
                string configValue = cfg.GetString(parm.name, parm.GetValue());
                if (!String.IsNullOrEmpty(configValue)) {
                    parm.SetValue(cfg.GetString(parm.name, parm.GetValue()));
                }
            }
        }
        */

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
    }
}
