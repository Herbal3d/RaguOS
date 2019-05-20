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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;
using BasilType = org.herbal3d.basil.protocol.BasilType;

namespace org.herbal3d.Ragu {

    public class SpaceServerCCLayer : SpaceServerLayer {

        // Initial SpaceServerCC invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        // Note: this canceller is for the overall layer.
        public SpaceServerCCLayer(RaguContext pContext, CancellationTokenSource pCanceller)
                        : base(pContext, pCanceller, "SpaceServerCC") {
        }

        // Return an instance of me
        protected override SpaceServerLayer InstanceFactory(RaguContext pContext,
                        CancellationTokenSource pCanceller, HTransport.BasilConnection pConnection) {
            return new SpaceServerCC(pContext, pCanceller, pConnection);
        }
    }

    public class SpaceServerCC : SpaceServerLayer {
        // Creation of an instance for a specific client.
        // Note: this canceller is for the individual session.
        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller,
                                        HTransport.BasilConnection pBasilConnection) 
                        : base(pContext, pCanceller, "SpaceServerCC", pBasilConnection) {

            // This assignment directs the space server message calls to this ISpaceServer instance.
            _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = this;

            // The thing to call to make requests to the Basil server
            _client = new HTransport.BasilClient(pBasilConnection);
        }

        // This one client has disconnected
        public override void Shutdown() {
            _canceller.Cancel();
            if (_client != null) {
                _client = null;
            }
            if (_clientConnection != null) {
                _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = null;
                _clientConnection = null;
            }
        }

        // Request from Basil to open a SpaceServer session
        public override SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            _context.log.DebugFormat("{0} OpenSession.", _logHeader);

            var ret = new SpaceServer.OpenSessionResp();

            // DEBUG DEBUG
            _context.log.DebugFormat("{0} OpenSession Features:", _logHeader);
            foreach (var kvp in pReq.Features) {
                _context.log.DebugFormat("{0}     {1}: {2}", _logHeader, kvp.Key, kvp.Value);
            };

            // Check if this is a test connection. We cannot handle those.
            // Respond with an error message.
            if (CheckIfTestConnection(pReq, ref ret)) {
                return ret;
            }

            // Set the processor for the new client go.
            // This sends the connections for the layers to Basil.
            Task.Run( HandleBasilConnection, _canceller.Token );

            Dictionary<string, string> props = new Dictionary<string, string>() {
                { "SessionKey", _context.sessionKey },
                // For the moment, fake an asset access key
                { "AssetKey", _context.assetAccessKey },
                { "AssetKeyExpiration", _context.assetKeyExpiration.ToString("O") },
                { "AssetBase", RaguAssetService.Instance.AssetServiceURL }
            };
            ret.Properties.Add(props);
            return ret;
        }

        // Request from Basil to close the SpaceServer session
        public override SpaceServer.CloseSessionResp CloseSession(SpaceServer.CloseSessionReq pReq) {
            throw new NotImplementedException();
        }

        // Request from Basil to move the camera.
        public override SpaceServer.CameraViewResp CameraView(SpaceServer.CameraViewReq pReq) {
            throw new NotImplementedException();
        }

        // Received an OpenSession from a Basil client.
        // Connect it to the other layers.
        private async Task HandleBasilConnection() {
            try {
                _context.log.DebugFormat("{0} HandleBasilConnection", _logHeader);
                BasilType.AccessAuthorization auth = null;
                if (_context.layerStatic != null) {
                    Dictionary<string, string> props = new Dictionary<string, string>() {
                        { "Service", "SpaceServerClient" },
                        { "TransportURL", _context.layerStatic.RemoteConnectionURL },
                        { "ServiceHint", "static" },
                    };
                    await _client.MakeConnectionAsync(auth, props);
                }

                if (_context.layerDynamic != null) {
                    Dictionary<string, string> props = new Dictionary<string, string>() {
                        { "Service", "SpaceServerClient" },
                        { "TransportURL", _context.layerDynamic.RemoteConnectionURL },
                        { "ServiceHint", "dynamic" },
                    };
                    await _client.MakeConnectionAsync(auth, props);
                }

                if (_context.layerActors != null) {
                    Dictionary<string, string> props = new Dictionary<string, string>() {
                        { "Service", "SpaceServerClient" },
                        { "TransportURL", _context.layerActors.RemoteConnectionURL },
                        { "ServiceHint", "actors" },
                    };
                    await _client.MakeConnectionAsync(auth, props);
                }
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }
    }
}
