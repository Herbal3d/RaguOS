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

using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using org.herbal3d.cs.CommonUtil;
using org.herbal3d.OSAuth;

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
    }

    // Class passed around for global context for this region module instance
    public class RaguContext {
        public IConfig sysConfig;
        public RaguParams parms;    // assume it's readonly
        public readonly RaguStats stats;
        public Scene scene;
        public BLogger log;
        public readonly string sessionKey;
        public string assetAccessKey;
        public DateTime assetKeyExpiration;
        // The following are the layer servers for this region.
        public Dictionary<string, SpaceServerListener> LayerListeners
                            = new Dictionary<string, SpaceServerListener>();
        public Dictionary<string, WaitingInfo> waitingForMakeConnection = new Dictionary<string, WaitingInfo>();
        public string HostnameForExternalAccess;

        public RaguContext() {
            var randomNumbers = new Random();
            stats = new RaguStats(this);
            // TODO: make session and asset keys bearer certificates with expiration, etc
            sessionKey = randomNumbers.Next().ToString();
            assetAccessKey = randomNumbers.Next().ToString();
            assetKeyExpiration = DateTime.UtcNow.AddHours(2);
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
            var tempLogger = new BLogger(new ParamBlock(
                new Dictionary<string, object>() { { "LogToConsole", true } })
            );
            RaguParams raguParams = new RaguParams(tempLogger, iniConfig);
            _context = new RaguContext() {
                sysConfig = iniConfig,
                parms = raguParams,
                log = new BLogger(raguParams)

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
                _regionProcessor.Start();
            }
        }
    }
}
