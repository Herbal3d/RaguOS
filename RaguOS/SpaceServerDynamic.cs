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
using org.herbal3d.cs.CommonEntitiesUtil;
using org.herbal3d.OSAuth;

using Google.Protobuf;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public class SpaceServerDynamicListener : SpaceServerLayer {

        // Initial SpaceServerDynamic invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        // Note: this canceller is for the overall layer.
        public SpaceServerDynamicListener(RaguContext pContext, CancellationTokenSource pCanceller)
                        : base(pContext, pCanceller, "SpaceServerDynamic") {
        }

        // Return an instance of me
        protected override SpaceServerLayer InstanceFactory(RaguContext pContext,
                        CancellationTokenSource pCanceller, HTransport.BasilConnection pConnection) {
            return new SpaceServerDynamic(pContext, pCanceller, this, pConnection);
        }
    }

    public class SpaceServerDynamic : SpaceServerLayer {
        // Creation of an instance for a specific client.
        // Note: this canceller is for the individual session.
        public SpaceServerDynamic(RaguContext pContext, CancellationTokenSource pCanceller,
                                        SpaceServerLayer pListener, HTransport.BasilConnection pBasilConnection) 
                        : base(pContext, pCanceller, pListener, "SpaceServerDynamic", pBasilConnection) {

            _context.log.DebugFormat("{0} Instance Constructor", _logHeader);

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
            base.Shutdown();
        }

        // Request from Basil to open a SpaceServer session
        public override SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            _context.log.DebugFormat("{0} OpenSession.", _logHeader);

            var ret = new SpaceServer.OpenSessionResp();

            // DEBUG DEBUG
            if (pReq.Features != null && pReq.Features.Count > 0) {
                _context.log.DebugFormat("{0} OpenSession Features:", _logHeader);
                foreach (var kvp in pReq.Features) {
                    _context.log.DebugFormat("{0}     {1}: {2}", _logHeader, kvp.Key, kvp.Value);
                };
            }
            // END DEBUG DEBUG

            // Check if this is a test connection. We cannot handle those.
            // Respond with an error message.
            if (CheckIfTestConnection(pReq, ref ret)) {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Test session not acceptable"
                };
                return ret;
            }

            if (base.ValidateOpenAuth(pReq.Auth)) {

                // Use common processing routine for all the SpaceServer layers
                ret = base.HandleOpenSession(pReq);

                // Start sending stuff to our new Basil friend.
                // HandleBasilConnection();
            }
            else {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Not authorized"
                };
            }
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
