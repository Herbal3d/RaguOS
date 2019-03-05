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
using System.Text;
using System.Threading.Tasks;
using Mono.Addins;

using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using org.herbal3d.cs.CommonEntitiesUtil;

using Nini.Config;
using log4net;

namespace org.herbal3d.Ragu {

    // Class passed around for global context for this region module instance
    public class RaguContext {
        public IConfig sysConfig;
        public RaguParams parms;
        public RaguStats stats;
        public BLogger log;
        public string contextName;  // a unique identifier for this context -- used in filenames, ...

        public RaguContext(IConfig pSysConfig, RaguParams pParms, ILog pLog) {
            sysConfig = pSysConfig;
            parms = pParms;
            log = new LoggerLog4Net(pLog);
            stats = new RaguStats(this);
            contextName = String.Empty;
        }
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RaguModule")]
    public class RaguModule : INonSharedRegionModule {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly String _logHeader = "[RaguModule]";

        private RaguContext _context;
        private Scene _scene;
        private RaguRegion _regionProcessor = null;

        // IRegionModuleBase.Name
        public string Name { get { return "OSAuthModule"; } }

        // IRegionModuleBase.ReplaceableInterface
        // This module has nothing to do with replaceable interfaces.
        public Type ReplaceableInterface { get { return null; } }

        // IRegionModuleBase.Initialize
        public void Initialise(IConfigSource pConfig) {
            var sysConfig = pConfig.Configs["Ragu"];
            _context = new RaguContext(sysConfig, null, _log);
            _context.parms  = new RaguParams(_context);
            if (sysConfig != null) {
                _context.parms.SetParameterConfigurationValues(sysConfig, _context);
            }
            if (_context.parms.P<bool>("Enabled")) {
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
            _scene = pScene;
        }

        // IRegionModuleBase.RemoveRegion
        public void RemoveRegion(Scene pScene) {
            if (_scene != null) {
                Close();
                _scene = null;
            }
        }

        // IRegionModuleBase.RegionLoaded
        // Called once for each region loaded after all other regions have been loaded.
        public void RegionLoaded(Scene scene) {
            if (_context.parms.P<bool>("Enabled")) {
                _context.log.DebugFormat("{0} Region loaded. Starting region manager", _logHeader);
                _regionProcessor = new RaguRegion(_scene, _context);
                _regionProcessor.Start();
            }
        }
    }
}
