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
using System.Text;

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;
using BasilType = org.herbal3d.basil.protocol.BasilType;
using org.herbal3d.cs.CommonEntitiesUtil;
using org.herbal3d.OSAuth;

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

        // A name for this layer that is unique in this region
        public string LayerName;

        // The URL for external clients to connect to this layer
        public string RemoteConnectionURL;

        // The authorization token to use to access this layer
        public OSAuthToken AccessToken;

        // Authorization token provided by the client when it connects
        // TODO: this should be a get/set into the renewable tokens in the authorization module
        public OSAuthToken ClientAuth;

        // Canceller for the layer service. Other cancellers are created for each client.
        protected readonly CancellationTokenSource _canceller;
        protected readonly RaguContext _context;

        // Instance is the per-layer SpaceServer controller
        protected List<SpaceServerLayer> _clients;

        // Instance is a per-client SpaceServer connection
        protected HTransport.ServerListener _transport;
        protected HTransport.BasilConnection _clientConnection;
        protected HTransport.BasilClient _client;
        public HTransport.BasilClient Client {
            get { return _client; }
        }

        // Initial SpaceServerCC invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller, string pLayerName) {
            _context = pContext;
            _canceller = pCanceller;
            LayerName = pLayerName;
            _logHeader = "[" + pLayerName + "]";

            // The Basil servers we have connected to
            _clients = new List<SpaceServerLayer>();

            // Create the parameter block for this type of layer
            ParamBlock ccParams = CreateTransportParams(_context, pLayerName);
            try {
                _context.log.DebugFormat("{0} Initializing transport", _logHeader);
                _transport = new HTransport.ServerListener(ccParams, _context.log);
                _transport.OnBasilConnect += Event_NewBasilConnection;
                _transport.OnDisconnect += Event_DisconnectBasilConnection;
                _transport.Start(_canceller);
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Exception creating transport: {1}", _logHeader, e);
            }

            // Build a URI for external hosts to access this layer
            _context.log.DebugFormat("{0} Create. Layername = {1}", _logHeader, pLayerName);
            foreach (var kvp in ccParams.Params) {
                _context.log.DebugFormat("     {0} -> {1}", kvp.Key, kvp.Value);
            }
            UriBuilder connectionUri = new UriBuilder(ccParams.P<string>("ConnectionURL")) {
                Host = RaguRegion.HostnameForExternalAccess
            };
            RemoteConnectionURL = connectionUri.ToString();

            OSAuthModule auther = _context.scene.RequestModuleInterface<OSAuthModule>();
            if (auther != null) {
                AccessToken = auther.CreateAuthForService(LayerName);
            }
            else {
                _context.log.ErrorFormat("{0} OSAuthModule not found. Cannot create authorization token", _logHeader);
            }
        }

        // A per client instance of the layer.
        // NOTE: this does not subscribe to the Event_* so these will not happen in per-client instances.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller,
                        string pLayerName, HTransport.BasilConnection pConnection) {
            _context = pContext;
            _canceller = pCanceller;
            LayerName = pLayerName + "-" + Util.RandomString(10);
            _logHeader = "[" + pLayerName + "]";

            _clientConnection = pConnection;

            OSAuthModule auther = _context.scene.RequestModuleInterface<OSAuthModule>();
            if (auther != null) {
                AccessToken = auther.CreateAuthForService(LayerName);
            }
            else {
                _context.log.ErrorFormat("{0} OSAuthModule not found. Cannot create authorization token", _logHeader);
            }
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
        protected ParamBlock CreateTransportParams(RaguContext pContext, string pLayerName) {
            return new ParamBlock(new Dictionary<string, object>() {
                    {  "ConnectionURL", GetRegionParamValue<string>(pContext, pLayerName, "ConnectionURL") },
                    {  "IsSecure", GetRegionParamValue<bool>(pContext, pLayerName, "IsSecure") },
                    {  "SecureConnectionURL", GetRegionParamValue<string>(pContext, pLayerName, "SecureConnectionURL") },
                    {  "Certificate", GetRegionParamValue<string>(pContext, pLayerName, "Certificate") },
                    {  "DisableNaglesAlgorithm", GetRegionParamValue<bool>(pContext, pLayerName, "DisableNaglesAlgorithm") }
            });
        }
        // Get the region specific parameter value.
        // Checks to see if the parameter has been set in the Regions.ini file. If not, use the
        //    value set in RaguParams. This allows setting region specific values.
        private T GetRegionParamValue<T>(RaguContext pContext, string pLayerName, string pParam) {
            T ret = default(T);
            string mod = pLayerName + "." + pParam;
            // Try to get the value from the 'extra' values that are set from Regions.ini
            string val = pContext.scene.RegionInfo.GetSetting(mod);
            if (val == null) {
                // There is no value in Regions.ini so use the RaguParams value
                ret = pContext.parms.P<T>(mod);
            }
            else {
                // RegionInfo.GetSettings always returns a string so convert it to the type needed.
                ret = ParamBlock.ConvertTo<T>(val);
            }
            return ret;
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


        // Validates the 'UserAuth' field in a BasilType.AccessAuthorization structure.
        // Checks if Ragu parameter "ShouldEnforceUserAuth" is true. If false, user is authorized.
        // Returns 'true' if it checks out, 'false' otherwise.
        // Also returns the authorizer module and the flag itself for later use.
        protected bool ValidateUserAuth(BasilType.AccessAuthorization pAuth,
                                    out OSAuthModule pAuthModule, out bool pIsAuthorized) {
            OSAuthModule auther = null;
            bool isAuthorized = false;
            try {
                auther = _context.scene.RequestModuleInterface<OSAuthModule>();
                if (_context.parms.P<bool>("ShouldEnforceUserAuth")) {
                    if (auther != null && pAuth != null) {
                        if (pAuth.AccessProperties.TryGetValue("Auth", out string userAuth)) {
                            if (auther.Validate(userAuth, ClientAuth)) {
                                isAuthorized = true;
                            }
                        }
                    }
                }
                else {
                    isAuthorized = true;
                }
            }
            catch (Exception e) {
                isAuthorized = false;
                _context.log.ErrorFormat("{0} Exception checking login authentication: e={1}", _logHeader, e);
            }
            pAuthModule = auther;
            pIsAuthorized = isAuthorized;
            return (auther != null) && isAuthorized;
        }

        // Do all the common processing for handling an OpenSession request.
        // Presumes that the request has beeen validated and can be setup and initialized.
        // Creates a new 'connection' and authorization keys for the communication.
        // Returns the response message.
        protected SpaceServer.OpenSessionResp HandleOpenSession(SpaceServer.OpenSessionReq pReq, OSAuthModule pAuther,
                                    out string pSessionKey, out string pConnectionKey) {
            SpaceServer.OpenSessionResp ret = new SpaceServer.OpenSessionResp();

            // This connection gets a unique handle
            string connectionKey = Guid.NewGuid().ToString();

            // The caller gave us a session key to use
            pReq.Auth.AccessProperties.TryGetValue("SessionKey", out string sessionKey);
            if (sessionKey == null) {
                // Generate a unique session key since the client didn't give us one
                sessionKey = Guid.NewGuid().ToString();
            }

            // Get a new authorization token for this session.
            // THis will authorize talking to me over this connection.
            pAuther.RemoveServiceAuth(connectionKey);
            OSAuthToken sessionAuth = pAuther.CreateAuthForService(connectionKey);
            pAuther.RegisterAuthForService(connectionKey, sessionAuth);

            // The client should have given us some authorization for our requests to her
            OSAuthToken clientToken = null;
            try {
                pReq.Auth.AccessProperties.TryGetValue("ClientAuth", out string clientAuth);
                pReq.Auth.AccessProperties.TryGetValue("ClientAuthExpiration", out string clientAuthExp);
                if (String.IsNullOrWhiteSpace(clientAuthExp)) clientAuthExp = "2299-12-31T23:59:59Z";
                if (clientAuth != null) {
                    clientToken = new OSAuthToken(connectionKey) {
                        Token = clientAuth,
                        Expiration = DateTime.Parse(clientAuthExp)
                    };
                }
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Exception parsing client auth info: {1}", _logHeader, e);
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Client authentication info mis-formed"
                };
                ret.Exception.Hints.Add("Exception", e.ToString());
                ret.Exception.Hints.Add("ClientAuthInfo", pReq.Auth.ToString());
                clientToken = null;
            }

            if (clientToken != null) {
                ClientAuth = clientToken;
                pAuther.RegisterAuthForClient(connectionKey, clientToken);

                // This initial connection tells the client about the asset service
                StringBuilder services = new StringBuilder();
                services.Append("[");
                services.Append(pAuther.GetServiceAuth(RaguAssetService.ServiceName).ToJSON(new Dictionary<string, string>() {
                    {  "Url", RaguAssetService.Instance.AssetServiceURL }
                }));
                services.Append("]");

                Dictionary<string, string> props = new Dictionary<string, string>() {
                    { "SessionAuth", sessionAuth.Token },
                    { "SessionAuthExpiration", sessionAuth.ExpirationString() },
                    { "SessionKey", sessionKey },
                    { "ConnectionKey", connectionKey },
                    { "Services", services.ToString() }
                };
                ret.Properties.Add(props);
            }
            else {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Client authorization info not present"
                };
            }

            pSessionKey = sessionKey;
            pConnectionKey = connectionKey;
            return ret;
        }

        // Build a BasilType.AccessAuthorization structure given an authorization token.
        // If the token is 'null' or zero length, return 'null' so auth section is not
        //     generated in the Basil messsage.
        protected BasilType.AccessAuthorization CreatBasilAccessAuth(string pAuthTokenString) {
            BasilType.AccessAuthorization ret = new BasilType.AccessAuthorization();
            if (String.IsNullOrEmpty(pAuthTokenString)) {
                ret = null;
            }
            else {
                ret.AccessProperties.Add("Auth", pAuthTokenString);
            }
            return ret;
        }


    }
}
