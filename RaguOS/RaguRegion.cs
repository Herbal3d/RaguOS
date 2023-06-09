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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenSim.Region.Framework.Scenes;

using org.herbal3d.transport;
using org.herbal3d.cs.CommonUtil;


namespace org.herbal3d.Ragu {
    // Per-region Ragu state and logic
    public class RaguRegion {
        private static readonly String _logHeader = "[RaguRegion]";

        public readonly RaguContext RContext;
        // Cancellation token for all services started by Ragu for this region.
        private readonly CancellationTokenSource _canceller;

        // Given a scene, do the LOD ("level of detail") conversion
        public RaguRegion(RaguContext pContext) {
            RContext = pContext;
            _canceller = new CancellationTokenSource();

            // Do an early calcuation of host name. It can be specified in the parameter
            //     file or we do a complex calculation to find a non-virtual ethernet
            //     connection.
            InitializeHostnameForExternalAccess(RContext);
        }

        public void Start() {
            // Wait for the region to have all its content before scanning
            RContext.scene.EventManager.OnPrimsLoaded += Event_OnPrimsLoaded;

            // Ragu's process for external access to the Loden generated content.
            // TODO: This should be an external process.
            RaguAssetService.CreateInstance(RContext);
        }

        public void Stop() {
            if (_canceller != null) {
                _canceller.Cancel();
            }
        }

        // All prims have been loaded into the region.
        // Start the 'command and control' SpaceServer.
        private void Event_OnPrimsLoaded(Scene pScene) {
            RContext.log.Debug("{0} Prims loaded. Starting listener for inbound connections", _logHeader);
            try {
                // Start the listener for this region.
                // This will receive all the connections and OpenSession's and create the
                //     space servers for same.
                // The port is assumed to be one more than the region port.
                //     This is because the region port is sent in the login response.
                //     and there is, currently, no mechanism to send the WS port.
                var wsPort = RContext.scene.RegionInfo.InternalEndPoint.Port + 1;
                RContext.Listener = new SpaceServerListener(
                    context: RContext,
                    transportParams: new BTransportParams[] {
                        new BTransportWSParams() {
                            preferred       = true,
                            isSecure        = RContext.parms.GetConnectionParam<bool>(RContext, null, "WSIsSecure"),
                            // port            = RContext.parms.GetConnectionParam<int>(RContext, null, "WSPort"),
                            port            = wsPort,
                            certificate     = RContext.parms.GetConnectionParam<string>(RContext, null, "WSCertificate"),
                            externalURLTemplate = RContext.parms.GetConnectionParam<string>(RContext, null, "WSExternalUrlTemplate"),
                            disableNaglesAlgorithm = RContext.parms.GetConnectionParam<bool>(RContext, null, "WSDisableNaglesAlgorithm")
                        }
                    },
                    canceller: _canceller
                ) ;
            }
            catch (Exception e) {
                RContext.log.Error("{0} Failed creation of SpaceServerCC: {1}", _logHeader, e);
            }
        }

        // There are several network interfaces on any computer.
        // First check if specified in the Regions.ini file or the configuration file, if not,
        //     find the non-virtual ethernet interface.
        // Find the first interface that is actually talking to the network and not
        //     one of the Docker interfaces.
        private void InitializeHostnameForExternalAccess(RaguContext pContext) {
            if (! String.IsNullOrEmpty(RContext.scene.RegionInfo.ExternalHostName)) {
                // The region specifies an external hostname. Use that one.
                pContext.HostnameForExternalAccess = RContext.scene.RegionInfo.ExternalHostName;
            }
            else {
                pContext.HostnameForExternalAccess = RContext.parms.ExternalAccessHostname;
                if (String.IsNullOrEmpty(pContext.HostnameForExternalAccess)) {
                    // The hostname was not specified in the config file so figure it out.
                    // Look for the first IP address that is Ethernet, up, and not virtual or loopback.
                    // Cribbed from https://stackoverflow.com/questions/6803073/get-local-ip-address
                    pContext.HostnameForExternalAccess = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                                && x.OperationalStatus == OperationalStatus.Up
                                && !x.Description.ToLower().Contains("virtual")
                                && !x.Description.ToLower().Contains("pseudo")
                        )
                        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                        .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork
                                && !IPAddress.IsLoopback(x.Address)
                        )
                        .Select(x => x.Address.ToString())
                        .First();
                }
            }

            // Put computed hostname in parameters so it can be seen externally
            RContext.parms.ExternalAccessHostname = pContext.HostnameForExternalAccess;

            RContext.log.Debug("{0} HostnameForExternalAccess = {1}", _logHeader, pContext.HostnameForExternalAccess);
        }
    }
}
