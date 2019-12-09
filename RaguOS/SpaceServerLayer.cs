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

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;
using BasilType = org.herbal3d.basil.protocol.BasilType;
using org.herbal3d.cs.CommonEntitiesUtil;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

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

        // When we have a session going, it has a key and auth tokens for input and output
        public string SessionKey;
        public OSAuthToken SessionAuth;
        public OSAuthToken ClientAuth;


        // Canceller for the layer service. Other cancellers are created for each client.
        protected readonly CancellationTokenSource _canceller;
        protected readonly RaguContext _context;

        // Each of the connections to a SpaceServer is a 'Session'
        protected List<SpaceServerLayer> Sessions;

        // Instance is a per-client SpaceServer connection
        protected HTransport.ServerListener _transport;
        protected HTransport.BasilConnection _clientConnection;
        protected HTransport.BasilClient _client;
        public HTransport.BasilClient Client {
            get { return _client; }
        }

        // Initial SpaceServerLayer invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller, string pLayerName) {
            _context = pContext;
            _canceller = pCanceller;
            LayerName = pLayerName;
            _logHeader = "[" + pLayerName + "]";

            // The Basil servers we have connected to
            Sessions = new List<SpaceServerLayer>();

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

            // There is an authorization for making the initial connection
            SessionAuth = new OSAuthToken() {
                Srv = "SessionAuth",
                Sid = org.herbal3d.cs.CommonEntitiesUtil.Util.RandomString(10)
            };
            _context.log.DebugFormat("{0} SpaceServerLayer: SessionAuth={1}", _logHeader, SessionAuth.TokenJSON);
        }

        // A per client instance of the layer.
        // NOTE: this does not subscribe to the Event_* so these will not happen in per-client instances.
        public SpaceServerLayer(RaguContext pContext, CancellationTokenSource pCanceller,
                        string pLayerName, HTransport.BasilConnection pConnection) {
            _context = pContext;
            _canceller = pCanceller;
            LayerName = pLayerName + "-" + org.herbal3d.cs.CommonEntitiesUtil.Util.RandomString(10);
            _logHeader = "[" + pLayerName + "]";

            _clientConnection = pConnection;
        }

        // Process a new Basil connection
        private void Event_NewBasilConnection(HTransport.BasilConnection pBasilConnection) {
            _context.log.DebugFormat("{0} Event_NewBasilConnection", _logHeader);
            // Cancellation token for this client connection
            CancellationTokenSource sessionCanceller = new CancellationTokenSource();
            Sessions.Add(InstanceFactory(_context, sessionCanceller, pBasilConnection));
        }

        // Create an instance of the underlying class.
        // Each child class will override this method with proper logic to create the SpaceServer session
        protected virtual SpaceServerLayer InstanceFactory(RaguContext pContext, CancellationTokenSource pCanceller,
                        HTransport.BasilConnection pConnection) {
            throw new NotImplementedException();
        }

        // One of the Basil servers has disconnected
        private void Event_DisconnectBasilConnection(HTransport.BasilConnection pBasilConnection) {
            _context.log.DebugFormat("{0} Event_DisconnectBasilConnection", _logHeader);

            // Find the client that is disconnected
            try {
                SpaceServerLayer disconnectedClient = Sessions.Where(c => c._clientConnection == pBasilConnection).First();
                Sessions.Remove(disconnectedClient);
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
                                out OSAuthModule pAuthModule, out OSAuthToken pUserAuth) {
            OSAuthModule auther = null;
            OSAuthToken userAuthToken = null;
            bool isAuthorized = false;
            try {
                auther = _context.scene.RequestModuleInterface<OSAuthModule>();
                if (_context.parms.P<bool>("ShouldEnforceUserAuth")) {
                    if (auther != null && pAuth != null) {
                        if (pAuth.AccessProperties.TryGetValue("Auth", out string userAuth)) {
                            userAuthToken = OSAuthToken.FromString(userAuth);
                            if (this.SessionAuth != null) {
                                _context.log.DebugFormat("{0} ValidateUserAuth: userAuthJSON={1}, SessionAuth={2}",
                                    _logHeader, userAuthToken.TokenJSON, SessionAuth);
                                // There is a session open so the auth string should be the one for accessing this session
                                if (this.SessionAuth.Token == userAuth) {
                                    isAuthorized = true;
                                }
                                else {
                                    _context.log.DebugFormat("{0} ValidateUserAuth: Failed.", _logHeader);
                                    isAuthorized = false;
                                }
                            }
                            else {
                                // There is no session so this auth string is a global user auth
                                // Verify that is a connection with codes that were setup at login.
                                _context.log.DebugFormat("{0} ValidateUserAuth: userAuthJSON={1}",
                                            _logHeader, userAuthToken.TokenJSON);

                                try {
                                    string agentId = userAuthToken.GetProperty("AgentId");
                                    OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
                                    OMV.UUID sessionID = OMV.UUID.Parse(userAuthToken.GetProperty("SessionID"));
                                    OMV.UUID secureSessionID = OMV.UUID.Parse(userAuthToken.GetProperty("SSID"));
                                    uint circuitCode = UInt32.Parse(userAuthToken.GetProperty("CC"));

                                    AgentCircuitData acd = _context.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);
                                    if (acd != null) {
                                        if (acd.circuitcode == circuitCode) {
                                            if (acd.SessionID == sessionID) {
                                                if (acd.SecureSessionID == secureSessionID) {
                                                    isAuthorized = true;
                                                }
                                                else {
                                                    _context.log.DebugFormat("{0} ValidateUserAuth: Failed secureSessionID test. AgentId={1}",
                                                                _logHeader, agentId);
                                                }
                                            }
                                            else {
                                                _context.log.DebugFormat("{0} ValidateUserAuth: Failed sessionId test. AgentId={1}",
                                                            _logHeader, agentId);
                                            }
                                        }
                                        else {
                                            _context.log.DebugFormat("{0} ValidateUserAuth: Failed circuitCode test. AgentId={1}",
                                                        _logHeader, agentId);
                                        }
                                    }
                                }
                                catch (Exception e) {
                                    _context.log.ErrorFormat("{0} ValidateUserAuth: exception authorizing: {1}",
                                                _logHeader, e);
                                    isAuthorized = false;
                                }
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
            pUserAuth = userAuthToken;
            return (auther != null) && isAuthorized;
        }

        // Do all the common processing for handling an OpenSession request.
        // Presumes that the request has beeen validated and can be setup and initialized.
        // Creates a new 'connection' and authorization keys for the communication.
        // Returns the response message.
        protected SpaceServer.OpenSessionResp HandleOpenSession(SpaceServer.OpenSessionReq pReq, OSAuthModule pAuther) {
            SpaceServer.OpenSessionResp ret = new SpaceServer.OpenSessionResp();

            // This connection gets a unique handle
            string connectionKey = Guid.NewGuid().ToString();

            // The client gives us a token that authenticates to all our requests to the client
            OSAuthToken clientToken = null;

            if (pReq.Auth == null || pReq.Auth.AccessProperties == null) {
                _context.log.ErrorFormat("{0} OpenSession request has no AccessProperties", _logHeader);
                ret.Exception = new BasilType.BasilException() {
                    Reason = "OpenSession did not specify AccessProperties"
                };
                ret.Exception.Hints.Add("ClientAuthInfo", pReq.Auth.ToString());
                clientToken = null;
            }
            else {

                // The caller gave us a session key to use
                pReq.Auth.AccessProperties.TryGetValue("SessionKey", out this.SessionKey);
                if (this.SessionKey == null) {
                    // Generate a unique session key since the client didn't give us one
                    this.SessionKey = Guid.NewGuid().ToString();
                }

                // The client should have given us some authorization for our requests to her.
                // Collect auth information for accessing the client and build an OSAuthToken
                //     to be used when sending messages to the client.
                try {
                    pReq.Auth.AccessProperties.TryGetValue("ClientAuth", out string clientAuth);
                    if (clientAuth != null) {
                        clientToken = OSAuthToken.FromString(clientAuth);
                        clientToken.Srv = "client";
                        clientToken.Sid = this.SessionKey;
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
            }

            // The rest of the communication uses this per-user/per-session token
            SessionAuth = new OSAuthToken() {
                Srv = this.LayerName,
                Sid = this.SessionKey
            };
            // The client told use the token to use to talk to it
            this.ClientAuth = clientToken;

            RaguAssetService assetService = RaguAssetService.Instance;

            if (clientToken != null) {
                Dictionary<string, string> props = new Dictionary<string, string>() {
                    { "SessionAuth", this.SessionAuth.ToString() },
                    { "SessionKey", this.SessionKey },
                    { "ConnectionKey", connectionKey },
                    { "Services", "[]" }
                };
                ret.Properties.Add(props);
            }
            else {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Client authorization info not present"
                };
            }

            return ret;
        }

        // Build a BasilType.AccessAuthorization structure given an authorization token.
        // If the token is 'null' or zero length, return 'null' so auth section is not
        //     generated in the Basil messsage.
        protected BasilType.AccessAuthorization CreateAccessAuthorization(OSAuthToken pAuthToken) {
            BasilType.AccessAuthorization ret = new BasilType.AccessAuthorization();
            if (pAuthToken == null) {
                ret = null;
            }
            else {
                ret.AccessProperties.Add("Auth", pAuthToken.ToString());
            }
            return ret;
        }


    }
}
