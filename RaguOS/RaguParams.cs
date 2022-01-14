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
    public class RaguParams : ServiceParameters {

        public RaguParams(BLogger pLogger, IConfig pConfig): base(pLogger) {
            _logHeader = "[RAGU PARAMS]";
            DefineParameters();
            SetParameterDefaultValues(_logger);

            // If we were passed INI configuration, overlay the default values
            if (pConfig != null) {
                SetParameterConfigurationValues(pConfig, _logger);
            }
        }

        // =====================================================================================
        // =====================================================================================
        // List of all of the externally visible parameters.
        // For each parameter, this table maps a text name to getter and setters.
        // To add a new externally referencable/settable parameter, add the paramter storage
        //    location somewhere in the program and make an entry in this table with the
        //    getters and setters.
        // It is easiest to find an existing definition and copy it.
        //
        // A ParameterDefn<T>() takes the following parameters:
        //    -- the text name of the parameter. This is used for console input and ini file.
        //    -- a short text description of the parameter. This shows up in the console listing.
        //    -- a default value
        //
        // The single letter parameters for the delegates are:
        //    v = value (appropriate type)

        // The following table is for easy, typed access to some of the parameter values
        public bool Enabled { get { return P<bool>("Enabled"); } }
        public string ExternalAccessHostname { get { return P<string>("ExternalAccessHostname"); } }
        public bool ShouldEnforceUserAuth { get { return P<bool>("ShouldEnfroceUserAuth"); } }

        public string OutputDir { get { return P<string>("OutputDir"); } }
        public bool UseDeepFilenames { get { return P<bool>("UseDeepFilenames"); } }

        public bool LogBuilding { get { return P<bool>("LogBuilding"); } }
        public bool LogGltfBuilding { get { return P<bool>("LogGltfBuilding"); } }


        private void DefineParameters() {
            ParameterDefinitions = new ParameterDefnBase[] {
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
        public  void SetParameterConfigurationValues(IConfig cfg, BLogger pLogger)
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
    }
}
