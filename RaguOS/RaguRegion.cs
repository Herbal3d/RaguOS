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

namespace org.herbal3d.Ragu {
    public class RaguRegion {
        private static readonly String _logHeader = "[RaguRegion]";

        public static string HostnameForExternalAccess;

        private readonly RaguContext _context;
        // Cancellation token for all services started by Ragu for this region.
        private readonly CancellationTokenSource _canceller;

        private RaguAssetService _assetService;

        // Given a scene, do the LOD ("level of detail") conversion
        public RaguRegion(RaguContext pContext) {
            _context = pContext;
            _canceller = new CancellationTokenSource();

            // Do an early calcuation of host name. It can be specified in the parameter
            //     file or we do a complex calculation to find a non-virtual ethernet
            //     connection.
            InitializeHostnameForExternalAccess();
        }

        public void Start() {
            // Wait for the region to have all its content before scanning
            _context.scene.EventManager.OnPrimsLoaded += Event_OnPrimsLoaded;

            // Ragu's process for external access to the Loden generated content.
            // TODO: This should be an external process.
            _assetService = new RaguAssetService(_context);

        }

        public void Stop() {
            if (_assetService != null) {
                _assetService.Stop();
                _assetService = null;
            }
            if (_canceller != null) {
                _canceller.Cancel();
            }
        }

        // All prims have been loaded into the region.
        // Start the 'command and control' SpaceServer.
        private void Event_OnPrimsLoaded(Scene pScene) {
            _context.log.DebugFormat("{0} Prims loaded. Starting command-and-control SpaceServer", _logHeader);
            try {
                // Create the layers of the 3d world that are referenced by the Basil server
                // The CommandAndControl layer tells the Basil server about all the layers
                // TODO: Make dynamic and region specific.
                _context.layerActors = new SpaceServerActorsLayer(_context, _canceller);
                // _context.layerDynamic = new SpaceServerDynamicLayer(_context, _canceller);
                _context.layerStatic = new SpaceServerStaticLayer(_context, _canceller);
                // Command and control
                _context.layerCC = new SpaceServerCCLayer(_context, _canceller);
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Failed creation of SpaceServerCC: {1}", _logHeader, e);
            }
        }

        // There are several network interfaces on any computer.
        // Find the first interface that is actually talking to the network and not
        //     one of the Docker interfaces.
        // First check if specified in the configuration file and, if not, find the non-virtual
        //     ethernet interface.
        // Discussion leading to solution: https://stackoverflow.com/questions/6803073/get-local-ip-address
        private void InitializeHostnameForExternalAccess() {
            RaguRegion.HostnameForExternalAccess = _context.parms.P<string>("ExternalAccessHostname");
            if (String.IsNullOrEmpty(RaguRegion.HostnameForExternalAccess)) {
                // The hostname was not specified in the config file so figure it out.
                // Look for the first IP address that is Ethernet, up, and not virtual or loopback.
                RaguRegion.HostnameForExternalAccess =  NetworkInterface.GetAllNetworkInterfaces()
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
            _context.log.DebugFormat("{0} HostnameForExternalAccess = {1}", _logHeader, RaguRegion.HostnameForExternalAccess);
        }
    }
}
