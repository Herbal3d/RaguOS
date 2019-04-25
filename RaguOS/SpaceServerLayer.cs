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
using System.Threading;

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;
using BasilType = org.herbal3d.basil.protocol.BasilType;
using org.herbal3d.cs.CommonEntitiesUtil;

namespace org.herbal3d.Ragu {

    // Parent class for different SpaceServer layer implementations.
    // Common code for accepting a Basil connection and acting like a SpaceServer.
    // Children are SpaceServerCC, SpaceServerStatic, SpaceServerAvatar, ...
    // NOTE:
    // This is created in two different ways:
    //      Without a client connection in which case this is the instance that creates the incoming
    //          connection and
    //      With a client connection in which case this is a per-client instance
    public abstract class SpaceServerLayer : HTransport.ISpaceServer {
        protected string _logHeader = "[SpaceServerLayer]";

        // The URL for external clients to connect to this layer
        public string RemoteConnectionURL;

        // Canceller for the layer service. Other cancellers are created for each client.
        protected readonly CancellationTokenSource _canceller;
        protected readonly RaguContext _context;

        // Instance is the per-layer SpaceServer controller
        protected List<SpaceServerLayer> _clients;

        // Instance is a per-client SpaceServer connection
        protected HTransport.HerbalTransport _transport;
        protected HTransport.BasilConnection _clientConnection;
        protected HTransport.BasilClient _client;

        // Initial SpaceServerCC invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller, string pLayerName) {
            _context = pContext;
            _canceller = pCanceller;
            _logHeader = "[" + pLayerName + "]";

            // The Basil servers we have connected to
            _clients = new List<SpaceServerLayer>();

            // Create the parameter block for this type of layer
            IParameters ccParams = CreateTransportParams(_context, pLayerName);
            try {
                _context.log.DebugFormat("{0} Initializing transport", _logHeader);
                _transport = new HTransport.HerbalTransport(ccParams, _context.log);
                _transport.OnBasilConnect += Event_NewBasilConnection;
                _transport.OnDisconnect += Event_DisconnectBasilConnection;
                _transport.Start(_canceller);
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Exception creating transport: {1}", _logHeader, e);
            }

            // Build a URI for external hosts to access this layer
            UriBuilder connectionUri = new UriBuilder(ccParams.P<string>("connectionurl")) {
                Host = RaguRegion.HostnameForExternalAccess
            };
            RemoteConnectionURL = connectionUri.ToString();
        }

        // A per client instance of the layer.
        // NOTE: this does not subscribe to the Event_* so these will not happen in per-client instances.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller,
                        string pLayerName, HTransport.BasilConnection pConnection) {
            _context = pContext;
            _canceller = pCanceller;
            _logHeader = "[" + pLayerName + "]";

            _clientConnection = pConnection;
        }

        // Process a new Basil connection
        private void Event_NewBasilConnection(HTransport.BasilConnection pBasilConnection) {
            _context.log.DebugFormat("{0} Event_NewBasilConnection", _logHeader);
            // Cancellation token for this client connection
            CancellationTokenSource sessionCanceller = new CancellationTokenSource();
            _clients.Add(InstanceFactory(_context, sessionCanceller, pBasilConnection));
        }

        // Create an instance of the underlying class
        protected virtual SpaceServerLayer InstanceFactory(RaguContext pContext, CancellationTokenSource pCanceller,
                        HTransport.BasilConnection pConnection) {
            throw new NotImplementedException();
        }

        // One of the Basil servers has disconnected
        private void Event_DisconnectBasilConnection(HTransport.BasilConnection pBasilConnection) {
            _context.log.DebugFormat("{0} Event_DisconnectBasilConnection", _logHeader);

            // Find the client that is disconnected
            try {
                SpaceServerLayer disconnectedClient = _clients.Where(c => c._clientConnection == pBasilConnection).First();
                _clients.Remove(disconnectedClient);
                // Pass the error to the individual client
                disconnectedClient.Shutdown();
            }
            catch (InvalidOperationException ie) {
                var xx = ie; // get rid of the unreferenced var warning
                _context.log.ErrorFormat("{0} Event_DisconnectBasilConnection: did not find the closed client", _logHeader);
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Event_DisconnectBasilConnection: exception disconnecting client", _logHeader, e);
            }
        }

        // Stop what this instance is doing
        public virtual void Shutdown() {
            _canceller.Cancel();
        }

        // Create a ParameterCollection for this specific layer.
        // The name of the layer is appended to parameter names and looked up to create parameter names.
        protected IParameters CreateTransportParams(RaguContext pContext, string pLayerName) {
            string mod = pLayerName + ".";
            return new ParameterCollection()
                .Add("ConnectionURL", pContext.parms.P<string>(mod + "ConnectionURL"))
                .Add("IsSecure", pContext.parms.P<bool>(mod + "IsSecure"))
                .Add("SecureConnectionURL", pContext.parms.P<string>(mod + "SecureConnectionURL"))
                .Add("Certificate", pContext.parms.P<string>(mod + "Certificate"))
                .Add("DisableNaglesAlgorithm", pContext.parms.P<bool>(mod + "DisableNaglesAlgorithm"));
        }

        // Request from Basil to open a SpaceServer session
        public virtual SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            throw new NotImplementedException();
        }

        // Request from Basil to close the SpaceServer session
        public virtual SpaceServer.CloseSessionResp CloseSession(SpaceServer.CloseSessionReq pReq) {
            throw new NotImplementedException();
        }

        // Request from Basil to move the camera.
        public virtual SpaceServer.CameraViewResp CameraView(SpaceServer.CameraViewReq pReq) {
            throw new NotImplementedException();
        }

        // Look at the request and make sure it is not a test connection request.
        // Return 'true' if a test connection and update the response with error info.
        protected bool CheckIfTestConnection(SpaceServer.OpenSessionReq pReq, ref SpaceServer.OpenSessionResp pResp) {
            bool ret = false;
            if (pReq.Features != null) {
                if (pReq.Features.TryGetValue("TestConnection", out string testConnectionValue)) {
                    try {
                        if (bool.Parse(testConnectionValue)) {
                            // For some reason, a test connection is being made
                            pResp.Exception = new BasilType.BasilException() {
                                Reason = "Cannot make test connection to SpaceServer"
                            };
                            ret = true;
                        }
                    }
                    catch (Exception e) {
                        _context.log.ErrorFormat("{0} exception parsing value of TestConnection ({1}) : {2}",
                                    _logHeader, testConnectionValue, e);
                        pResp.Exception = new BasilType.BasilException() {
                            Reason = "Unparsable value of Feature parameter TestConnection: " + testConnectionValue
                        };
                        ret = true;
                    }
                }
            }
            return ret;
        }


    }
}
