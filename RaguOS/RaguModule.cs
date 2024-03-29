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
using Mono.Addins;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using org.herbal3d.cs.CommonUtil;
using org.herbal3d.cs.CommonEntities;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

using Nini.Config;
using log4net;

namespace org.herbal3d.Ragu {

    // When a SpaceServer sends a MakeConnection, it puts the expected authentication here
    //     so, when the OpenSession is received, the passed authentication can be verified.
    // These are periodically expired.
    // This is also used as a way to pass information from the sending of the MakeConnection
    //     to the SpaceServer that is eventually created. Most specifically, the agentUUID
    //     but more will be added in the future.
    public class WaitingInfo {
        public OSAuthToken incomingAuth;
        public DateTime whenCreated;
        public OMV.UUID agentUUID;
        public RaguContext rContext;
        public RaguRegion rRegion;
        public string spaceServerType;  // the type of the SpaceServer that is to be created
        // Function called when OpenSession is received and authentication is verified.
        //    This usually creates the SpaceServer that corresponds to the MakeConnection.
        public CreateSpaceServerProcessor createSpaceServer;

        public WaitingInfo() {
            // incomingAuth = new OSAuthToken();
            // outgoingAuth = new OSAuthToken();
            // Using shorter auth tokens
            incomingAuth = OSAuthToken.SimpleToken();
            whenCreated = new DateTime();
        }
        public WaitingInfo(OMV.UUID pAgentUUID): this() {
            agentUUID = pAgentUUID;
        }
    }

    // Class passed around for global context for this region module instance
    public class RaguContext {
        // System configuration information
        public IConfig sysConfig;

        public RaguParams parms;    // assume it's readonly
        public readonly RaguStats stats;
        public BLogger log;
        public readonly string sessionKey;

        // The RaguRegion instances created for each region
        public Dictionary<string, RaguRegion> RaguRegions = new Dictionary<string, RaguRegion>();  // regions known to Ragu

        public BFrameOfRef frameOfRef;

        // Listeners for the various SpaceServer advertizing transports
        // There is only one of these. Checked under lock(RaguContext)
        public static SpaceServerListener SimulatorCCListener;    // CC listener for simulator connections

        // Each region gets a listener for each type of transport
        // This is indexed by region name and then by transport type
        //        listener = RegionListeners[regionName][transportType];
        public Dictionary<string, Dictionary<string,SpaceServerListener>> RegionListeners =
                        new Dictionary<string, Dictionary<string,SpaceServerListener>>();

        // public Dictionary<string,SpaceServerListener> Listeners = new Dictionary<string, SpaceServerListener>();

        // All of the SpaceServers created for connections in this region.
        public List<SpaceServerBase> SpaceServers = new List<SpaceServerBase>();
        // When a client is sent a MakeConnection, the OpenSession auth info is added here
        public Dictionary<string, WaitingInfo> waitingForMakeConnection = new Dictionary<string, WaitingInfo>();

        // The hostname to use to access the service
        public string HostnameForExternalAccess;

        public RaguContext() {
            sessionKey = org.herbal3d.cs.CommonUtil.Util.RandomString(8);
        }

        // Remember a listener for a particular region and transport type
        public void addRegionListener(string pRegionName, string pTransportType, SpaceServerListener pListener) {
            lock (RegionListeners) {
                if (!RegionListeners.ContainsKey(pRegionName)) {
                    RegionListeners.Add(pRegionName, new Dictionary<string, SpaceServerListener>());
                }
                RegionListeners[pRegionName].Add(pTransportType, pListener);
            }
        }

        public void addSpaceServer(SpaceServerBase pSS) {
            lock (this.SpaceServers) {
                this.SpaceServers.Add(pSS);
            }
        }
        public void removeSpaceServer(SpaceServerBase pSS) {
            lock (this.SpaceServers) {
                this.SpaceServers.Remove(pSS);
            }
        }
        // Get the allocated SpaceServer of a particular type
        public T getSpaceServer<T>() where T : SpaceServerBase {
            lock (this.SpaceServers) {
                foreach (SpaceServerBase ss in this.SpaceServers) {
                    if (ss is T) {
                        return (T)ss;
                    }
                }
            }
            return null;
        }
        // WHen sending an OpenSession, this remembers the credentials of the request
        //     so the response can be validated.
        public WaitingInfo RememberWaitingForOpenSession(WaitingInfo pWInfo) {
            lock (waitingForMakeConnection) {
                waitingForMakeConnection.Add(pWInfo.incomingAuth.Token, pWInfo);
                // log.Debug("SpaceServerBase.RememberWaitingForOpenSession: itoken={0}", pWInfo.incomingAuth.Token);
            }
            return pWInfo;
        }
        // Look for the WaitingInfo indexed by the passed auth. Return the found WaitingInfo
        //    or null if not found.
        // This also removes the WaitingInfo from the list of waiting infos.
        public WaitingInfo GetWaitingForOpenSession(string pAuth, bool pRemove = true) {
            lock (waitingForMakeConnection) {
                // log.Debug("SpaceServerBase.GetWaitingForOpenSession: itoken={0}", pAuth);
                if (waitingForMakeConnection.TryGetValue(pAuth, out WaitingInfo foundInfo)) {
                    if (pRemove) {
                        log.Debug("SpaceServerBase.GetWaitingForOpenSession: removing itoken={0}", pAuth);
                        waitingForMakeConnection.Remove(pAuth);
                    }
                    return foundInfo;
                }
            }
            // log.Debug("SpaceServerBase.GetWaitingForOpenSession: failed to find itoken={0}", pAuth);
            return null;
        }
        // DEBUG DEBUG
        private void DumpWaitingForMakeConnection() {
            lock (waitingForMakeConnection) {
                // log.Debug("SpaceServerBase.DumpWaitingForMakeConnection: waitingForMakeConnection.Count={0}, Context.sessionKey", waitingForMakeConnection.Count, sessionKey);
                foreach (var kvp in waitingForMakeConnection) {
                    log.Debug("SpaceServerBase.DumpWaitingForMakeConnection: itoken={0}, inAuth={1}",
                        kvp.Key, kvp.Value.incomingAuth.Token);
                }
            }
        }
        // END DEBUG DEBUG
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RaguModule")]
    public class RaguModule : ISharedRegionModule {
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
                _log.InfoFormat("{0} Enabled. Using Loden={1}, CommonEntities={2}, CommonUtil={3}", _logHeader,
                    org.herbal3d.Loden.VersionInfo.longVersion,
                    org.herbal3d.cs.CommonEntities.VersionInfo.longVersion,
                    org.herbal3d.cs.CommonUtil.VersionInfo.longVersion
                );
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

        public void PostInitialise() {
            if (_context.parms.Enabled) {
                _context.log.Debug("{0} PostInitialize");
            }
        }

        // IRegionModuleBase.AddRegion
        // Called once for the region we're managing.
        public void AddRegion(Scene pScene) {
            return;
        }

        // IRegionModuleBase.RemoveRegion
        public void RemoveRegion(Scene pScene) {
            if (_context.RaguRegions.Remove(pScene.Name, out RaguRegion foundRegion)) {
                foundRegion.Stop();
            };
        }

        // IRegionModuleBase.RegionLoaded
        // Called once for each region loaded after all other regions have been loaded.
        public void RegionLoaded(Scene pScene) {
            if (_context.parms.Enabled) {
                _context.log.Debug("{0} Region loaded. Starting region manager", _logHeader);
                _context.log.Debug("{0}      RegionName={1}, RContext.sessionKey={2}", _logHeader, pScene.Name, _context.sessionKey);   // DEBUG DEBUG

                // Create the Ragu processor for this region
                _regionProcessor = new RaguRegion(_context, pScene);
                pScene.RegisterModuleInterface<RaguRegion>(_regionProcessor);

                // Remember the region processor for this region
                _context.RaguRegions.Add(pScene.Name, _regionProcessor);

                // Make it go
                _regionProcessor.Start();
            }
        }
    }
}
