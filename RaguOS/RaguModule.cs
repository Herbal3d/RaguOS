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
using System.Linq;
using System.Reflection;
using Mono.Addins;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using org.herbal3d.cs.CommonUtil;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

using Nini.Config;
using log4net;

namespace org.herbal3d.Ragu {

    // When a SpaceServer sends a MakeConnection, it puts the expeced authentication here
    // so, when the OpenSession is received, the passed authentication can be verified.
    // These are periodically expired.
    public class WaitingInfo {
        public OSAuthToken incomingAuth;
        public OSAuthToken outgoingAuth;
        public DateTime whenCreated;

        public WaitingInfo() {
            incomingAuth = new OSAuthToken();
            outgoingAuth = new OSAuthToken();
            whenCreated = new DateTime();
        }
        public WaitingInfo(OSAuthToken pIncomingAuth) {
            incomingAuth = pIncomingAuth;
            outgoingAuth = new OSAuthToken();
            whenCreated = new DateTime();
        }
    }

    // Class passed around for global context for this region module instance
    public class RaguContext {
        // System configuration information
        public IConfig sysConfig;

        public RaguParams parms;    // assume it's readonly
        public readonly RaguStats stats;
        public Scene scene;
        public BLogger log;
        public readonly string sessionKey;

        // The following are the layer servers for this region.
        // TODO: someday make this a dynamic collection
        public SpaceServerListener SpaceServerCCService;
        public SpaceServerListener SpaceServerStaticService;
        public SpaceServerListener SpaceServerActorsService;
        public SpaceServerListener SpaceServerDynamicService;
        // When a client is sent a MakeConnection, the OpenSession auth info is added here
        public Dictionary<string, WaitingInfo> waitingForMakeConnection = new Dictionary<string, WaitingInfo>();

        // The logged in avatar
        public OMV.UUID focusAvatarUUID;
        public IClientAPI focusRaguAvatar;
        public ScenePresence focusAvatarScenePresence;

        // The hostname to use to access the service
        public string HostnameForExternalAccess;

        public RaguContext() {
            sessionKey = org.herbal3d.cs.CommonUtil.Util.RandomString(8);
        }
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RaguModule")]
    public class RaguModule : INonSharedRegionModule {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly String _logHeader = "[RaguModule]";

        // This context instance is per-region
        private RaguContext _context;
        private RaguRegion _regionProcessor = null;

        // IRegionModuleBase.Name
        public string Name { get { return "OSAuthModule"; } }

        // IRegionModuleBase.ReplaceableInterface
        // This module has nothing to do with replaceable interfaces.
        public Type ReplaceableInterface { get { return null; } }

        // IRegionModuleBase.Initialize
        public void Initialise(IConfigSource pConfig) {
            var iniConfig = pConfig.Configs["Ragu"];
            // Temporary logger that outputs to console before we have the configuration
            // var tempLogger = new BLoggerNLog(logBaseFilename: null, logToConsole: true);
            var tempLogger = new BLoggerLog4Net(_log);

            RaguParams raguParams = new RaguParams(tempLogger, iniConfig);
            _context = new RaguContext() {
                sysConfig = iniConfig,
                parms = raguParams,
                // log = new BLoggerNLog(logBaseFilename: raguParams.LogBaseFilename,
                //     logToConsole: raguParams.LogToConsole,
                //     logToFile: raguParams.LogToFile)
                log = tempLogger
            };
            if (_context.parms.Enabled) {
                _log.InfoFormat("{0} Enabled", _logHeader);
            }
        }
        //
        // IRegionModuleBase.Close
        public void Close() {
            // Stop the region processor.
            if (_regionProcessor != null) {
                _regionProcessor.Stop();
                _regionProcessor = null;
            }
        }

        // IRegionModuleBase.AddRegion
        // Called once for the region we're managing.
        public void AddRegion(Scene pScene) {
            // Remember all the loaded scenes
            _context.scene = pScene;
        }

        // IRegionModuleBase.RemoveRegion
        public void RemoveRegion(Scene pScene) {
            if (_context.scene != null) {
                Close();
                _context.scene = null;
            }
        }

        // IRegionModuleBase.RegionLoaded
        // Called once for each region loaded after all other regions have been loaded.
        public void RegionLoaded(Scene scene) {
            if (_context.parms.Enabled) {
                _context.log.Debug("{0} Region loaded. Starting region manager", _logHeader);
                _regionProcessor = new RaguRegion(_context);
                _context.scene.RegisterModuleInterface<RaguRegion>(_regionProcessor);
                _regionProcessor.Start();
            }
        }
    }
}
