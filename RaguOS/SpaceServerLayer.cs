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

    // Parent class for different SpaceServer layer implementations.
    // Common code for accepting a Basil connection and acting like a SpaceServer.
    // Children are SpaceServerCC, SpaceServerStatic, SpaceServerAvatar, ...
    public abstract class SpaceServerLayer : HTransport.ISpaceServer {
        protected string _logHeader = "[SpaceServerLayer]";

        protected readonly CancellationTokenSource _canceller;
        protected readonly RaguContext _context;
        protected HTransport.HerbalTransport _transport;
        protected HTransport.BasilConnection _clientConnection;
        protected HTransport.BasilClient _client;

        // Initial SpaceServerCC invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller, string pModule) {
            _context = pContext;
            _canceller = pCanceller;
            _logHeader = "[" + pModule + "]";

            try {
                _context.log.DebugFormat("{0} Initializing transport", _logHeader);
                IParameters ccParams = CreateTransportParams(_context, pModule);
                _transport = new HTransport.HerbalTransport(this, ccParams, _context.log);
                _transport.OnBasilConnect += Event_NewBasilConnection;
                _transport.OnDisconnect += Event_DisconnectBasilConnection;
                _transport.Start(_canceller);
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Exception creating transport: {1}", _logHeader, e);
            }
        }

        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller,
                        string pModule, HTransport.BasilConnection pConnection) {
            _context = pContext;
            _canceller = pCanceller;
            _logHeader = "[" + pModule + "]";

            _clientConnection = pConnection;
        }

        // Process a new Basil connection
        protected abstract void Event_NewBasilConnection(HTransport.BasilConnection pBasilConnection);

        protected abstract void Event_DisconnectBasilConnection(HTransport.TransportConnection pTransport);

        protected IParameters CreateTransportParams(RaguContext pContext, string pModule) {
            string mod = pModule + ".";
            return new ParameterCollection()
                .Add("ConnectionURL", pContext.parms.P<string>(mod + "ConnectionURL"))
                .Add("IsSecure", pContext.parms.P<bool>(mod + "IsSecure"))
                .Add("SecureConnectionURL", pContext.parms.P<string>(mod + "SecureConnectionURL"))
                .Add("Certificate", pContext.parms.P<string>(mod + "Certificate"))
                .Add("DisableNaglesAlgorithm", pContext.parms.P<bool>(mod + "DisableNaglesAlgorithm"));
        }

        // Request from Basil to open a SpaceServer session
        public abstract SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq);

        // Request from Basil to close the SpaceServer session
        public abstract SpaceServer.CloseSessionResp CloseSession(SpaceServer.CloseSessionReq pReq);

        // Request from Basil to move the camera.
        public abstract SpaceServer.CameraViewResp CameraView(SpaceServer.CameraViewReq pReq);
    }
}
