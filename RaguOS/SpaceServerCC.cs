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
using org.herbal3d.cs.CommonEntitiesUtil;

using Google.Protobuf;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public class SpaceServerCC : SpaceServerLayer {

        // Initial SpaceServerCC invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller)
                        : base(pContext, pCanceller, "SpaceServerCC") {
        }

        // Creation of an instance for a specific client.
        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller,
                                HTransport.BasilConnection pBasilConnection) 
                        : base(pContext, pCanceller, "SpaceServerCC", pBasilConnection) {

            // This assignment directs the space server message calls to this ISpaceServer instance.
            _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = this;
        }

        // Process a new Basil connection
        protected override void Event_NewBasilConnection(HTransport.BasilConnection pBasilConnection) {
            SpaceServerCC ccHandler = new SpaceServerCC(_context, _canceller, pBasilConnection);
            _client = new HTransport.BasilClient(pBasilConnection);
        }

        protected override void Event_DisconnectBasilConnection(HTransport.TransportConnection pTransport) {
            _canceller.Cancel();
            if (_client != null) {
                _client = null;
            }
            if (_clientConnection != null) {
                _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = null;
                _clientConnection = null;
            }
            if (_transport != null) {
                _transport.OnBasilConnect -= Event_NewBasilConnection;
                _transport.OnDisconnect -= Event_DisconnectBasilConnection;
                _transport = null;
            }
        }

        // Request from Basil to open a SpaceServer session
        public override SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            _context.log.DebugFormat("{0} OpenSession.", _logHeader);
            if (pReq.Features != null) {
            }
            Dictionary<string, string> props = new Dictionary<string, string>() {
                { "SessionKey", _context.sessionKey },
                // For the moment, fake an asset access key
                { "AssetKey", _context.assetKey },
                { "AssetKeyExpiration", DateTime.UtcNow.AddHours(2).ToString("O") },
                { "AssetBase", RaguAssetService.Instance.AssetServiceURL }
            };


            var ret = new SpaceServer.OpenSessionResp() {
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
    }
}
