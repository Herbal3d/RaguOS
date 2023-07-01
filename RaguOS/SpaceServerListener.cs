// Copyright (c) 2021 Robert Adams
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
using System.Threading.Tasks;

using org.herbal3d.b.protocol;
using org.herbal3d.transport;
using org.herbal3d.OSAuth;
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

namespace org.herbal3d.Ragu {

    // Function called when a connection is available for a SpaceServer
    public delegate SpaceServerBase CreateSpaceServerProcessor(RaguContext pContext,
                                                    RaguRegion pRegion,
                                                    WaitingInfo pWaitingInfo,
                                                    BasilConnection pConnection,
                                                    BMessage pOpenSessionMsg,
                                                    CancellationTokenSource pCanceller
                                                    );

    /**
     * Class that listens on the specified transports. When connections are received,
     * this waits for an OpenSession BMessage request. The OpenSession authentication key
     * is looked up in the 'waitingForMakeConnection' list and, if found, the correct
     * SpaceServer is created and control is passed there.
     *
     * If a 'waitingForMakeConnection' entry is not found, the authentication is checked
     * for service login. If the login info is correct, a SpaceServerCC is created to
     * handle the session.
     *
     * Note that the initial protocol used for the OpenSession is BProtocolJSON. Both
     * the transport and the protocol can be changed based on the parameters in the
     * OpenSession. This feature is not yet implemented. TODO:
     */
    public abstract class SpaceServerListener {
        private static string _logHeader = "[SpaceServerListener]";
        protected RaguContext _RContext;
        protected RaguRegion _RRegion;

        public BTransportParams TransportParams;

        protected CancellationTokenSource _canceller;

        // The URL that is used to connect to this server.
        public abstract string ExternalURL { get; protected set; }

        // Listen for a connection and call the passed SpaceServer creater when a connection is made.
        // This canceller is for the whole service. A new one is created for the connection.
        public SpaceServerListener(
                        BTransportParams pTransportParams,
                        CancellationTokenSource pCanceller,
                        RaguContext pContext,
                        RaguRegion pRegion  // the region this listener is for
            ) {

            TransportParams = pTransportParams;
            _canceller = pCanceller;
            _RContext = pContext;
            _RRegion = pRegion;
            // _RContext.log.Debug("{0} SpaceServerListener: _RContext.sessionKey={1}", _logHeader, _RContext.sessionKey); // DEBUG DEBUG

            /*
            // Start an HTTP listener for the Ragu configuration request.
            string HandlerPath = "/Ragu/Config";
            var handlerKeys = MainServer.Instance.GetHTTPHandlerKeys();
            string thisHandler = "GET:" + HandlerPath;
            if (!handlerKeys.Contains(thisHandler)) {
                _RContext.log.Debug("{0} Creating GET handler for path '{1}'",
                                    _logHeader, HandlerPath);
                _configGetHandler = new RaguConfigGETStreamHandler(_RContext, this, HandlerPath);

                MainServer.Instance.AddStreamHandler(_configGetHandler);
            }
            else {
                _RContext.log.Debug("{0} GET handler already exists. Not creating.", _logHeader);
            }
            */
        }

        // A connection has been received.
        // Listen for an OpenSession.
        public static void AcceptConnection(BTransport pTrans,
                                            RaguContext pRContext,
                                            SpaceServerListener pListener,
                                            CancellationTokenSource pCanceller) {

            // The protocol for the initial OpenSession is always JSON
            ParamBlock pb = new ParamBlock(new Dictionary<string, object>() {
                { "logMsgSent", pRContext.parms.LogProtocolMsgSent },
                { "logMsgRcvd", pRContext.parms.LogProtocolMsgRcvd }
            });
            BProtocolJSON protocol = new BProtocolJSON(null, pTrans, pRContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            BasilConnection connection = new BasilConnection(protocol, pRContext.log);
            connection.SetOpProcessor(new ProcessMessagesOpenConnection(pListener, pRContext),
                                    pListener.ProcessConnectionStateChange);
            connection.Start();
            // pRContext.log.Debug("{0} AcceptConnection. xportT={1}, protoT={2}", _logHeader, pTrans.TransportType, protocol.ProtocolType);
        }

        // Called when the state of the connection changes.
        // TODO: How to recover from disconnect/reconnect?
        public void ProcessConnectionStateChange(BConnectionStates pConnectionState, BasilConnection pConn) {

        }

        // A message processor for for waiting for the OpenSession.
        // The OpenSession auth info is used to select the SpaceServer  that should be created.
        class ProcessMessagesOpenConnection : IncomingMessageProcessor {
            RaguContext _RContext;
            SpaceServerListener _listener;
            public ProcessMessagesOpenConnection(SpaceServerListener pListener, RaguContext pRContext) : base(null) {
                _RContext = pRContext;
                _listener = pListener;
            }
            public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
                // _RContext.log.Debug("SpaceServerListener.Process: msg received: {0}", pMsg);    // DEBUG DEBUG
                // _RContext.log.Debug("SpaceServerListener.Process: _RContext.sessionKey={0}", _RContext.sessionKey); // DEBUG DEBUG
                switch (pMsg.Op) {
                    case (uint)BMessageOps.OpenSessionReq:
                        _listener.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
                        break;
                    case (uint)BMessageOps.MakeConnectionResp:
                        // We will get responses from our MakeConnections
                        break;
                    default: {
                        BMessage resp = BasilConnection.MakeResponse(pMsg);
                        resp.Exception = "Session is not open. AA";
                        pConnection.Send(resp);
                        break;
                    }
                }
            }
        }

        // Have received the OpenSession request.
        // Check the authentication stuff and, if good, create the SpaceServer for this connection.
        public void ProcessOpenSessionReq(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            // _RContext.log.Debug("{0} ProcessOpenSession: _RContext.sessionKey={1}", _logHeader, _RContext.sessionKey); // DEBUG DEBUG
            string errorReason = "";
            // Get the login information from the OpenSession
            OSAuthToken clientAuth = OpenSessionResp.GetClientAuth(pMsg);
            if (clientAuth != null) {
                string incomingAuthString = pMsg.Auth;
                if (incomingAuthString != null) {
                    OSAuthToken loginAuth = OSAuthToken.FromString(incomingAuthString);

                    // Create a new auth token for communication into this.
                    // That is, all future communication will send messages with the 'clientAuth'
                    //    that was received in this OpenSession and messages received will
                    //    expect to have this 'incomingAuth' token.
                    // This 'incomingAuth' is sent with the OpenSession response to be used
                    //     by the client for future communication
                    OSAuthToken incomingAuth = OSAuthToken.SimpleToken();
                    // 'clientAuth' is the token sent by the client that this should send
                    //     with future messages to authenticate me.
                    OSAuthToken outgoingAuth = clientAuth;
                    pConnection.SetAuthorizations(incomingAuth, outgoingAuth);

                    // Verify this initial incoming authorization.
                    // If waiting for this auth from an OpenConnection, create the proper
                    //    SpaceServer to handle this session.
                    if (ValidateLoginAuth(loginAuth, out WaitingInfo waitingInfo)) {

                        // Create SpaceServer for this OpenSession
                        try {
                            SpaceServerBase ss = waitingInfo.createSpaceServer(
                                    waitingInfo.rContext, waitingInfo.rRegion, waitingInfo, pConnection, pMsg, _canceller);

                            // Construct the success response
                            var openSessionRespParams = new OpenSessionResp() {
                                ServerVersion = VersionInfo.longVersion,
                                ServerAuth = incomingAuth.Token
                            };
                            pConnection.SendResponse(pMsg, openSessionRespParams);

                            // Start the SpaceServer
                            _ = Task.Run( () => {
                                ss.Start();
                            });
                        }
                        catch (Exception ee) {
                            errorReason = String.Format("SpaceServerListener.ProcessOpenSessionReq: exception creating SpaceServer {0}: {1}",
                                                waitingInfo.spaceServerType, ee);
                            _RContext.log.Error("{0} {1}", _logHeader, errorReason);
                        }
                    }
                    else {
                        // The OpenSession is not from a MakeConnection.
                        // See if this is an initial session with all the user information
                        _RContext.log.Error("{0} OpenSession authorization failed: loginQuth={1}", _logHeader, loginAuth.Token);
                        errorReason = String.Format("Login credentials not valid ({0})", waitingInfo.spaceServerType);
                    }
                }
                else {
                    errorReason = String.Format("Login credentials not supplied (serviceAuth)");
                }
            }
            else {
                errorReason = String.Format("Connection auth not supplied (clientAuth)");
            }

            // If an error happened, return error response
            if (errorReason.Length > 0) {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = errorReason;
                pConnection.Send(resp);
            }

        }
        // Login auth check for OpenSessions that were started with a MakeConnection request.
        // SpaceServerCC overrides this function to do actual account login check.
        protected virtual bool ValidateLoginAuth(OSAuthToken pUserAuth, out WaitingInfo pWaitingInfo) {
            // _RContext.log.Debug("{0}: ValidateLoginAuth: pUserAuth={1}", _logHeader, pUserAuth.Dump());
            bool isAuthorized = false;
            string auth = pUserAuth.Token;
            WaitingInfo waitingInfo = _RContext.GetWaitingForOpenSession(auth);
            if (waitingInfo != null) {
                // Return whether the authentication info matches (always true since the auth is the key)
                isAuthorized = waitingInfo.incomingAuth.Equals(pUserAuth);
                if (!isAuthorized) {
                    _RContext.log.Error("{0}: ValidateLoginAuth: Failed with waitingInfo. incominAuth={1], pUserAuth={2}",
                                        _logHeader, waitingInfo.incomingAuth.Dump(), pUserAuth.Dump());
                }
            }
            else {
                // There is no WaitingInfo. Maybe this is a login rather than a MakeConnection
                // _RContext.log.Debug("{0}: OpenSession with unknown token. Token: {1}", _logHeader, auth);
                // Create a 'waitingInfo' as if we'd been waiting for this initial login OpenSession
                waitingInfo = SpaceServerCC.CreateWaitingInfo(_RContext, _RRegion, OMV.UUID.Zero, pUserAuth);
                try {
                    isAuthorized = ValidateInitialLoginToken(pUserAuth, waitingInfo, out RaguRegion useDestRegion);
                    // The user information was found on one of the regions. Use that region as the home region.
                    waitingInfo.rRegion = useDestRegion;
                }
                catch (Exception e) {
                    _RContext.log.Error("{0} ValidateUserAuth: exception authorizing: {1}", _logHeader, e);
                    isAuthorized = false;
                }
            }
            pWaitingInfo = waitingInfo;
            return isAuthorized;
        }

        // The initial login token that is sent with the OpenSession request contains a bunch of OpenSimulator
        //    identifiction info coded there-in. In particular, it has the agent UUID, circuit code, secure
        //    session ID and the session ID.
        // This routine checks that the agent UUID is valid and that the session ID and secure session ID
        //    also correspond to a logged in session.
        // Return true if the token is valid.
        public bool ValidateInitialLoginToken(OSAuthToken pUserAuth, WaitingInfo pWaitingInfo, out RaguRegion homeRegion) {
            homeRegion = pWaitingInfo.rRegion;

            bool isAuthorized = false;

            string agentId = pUserAuth.GetProperty("aId");
            if (agentId == null) {
                _RContext.log.Error("{0} ValidateUserAuth: No waiting info but no aId info for login. Auth={1}",
                            _logHeader, pUserAuth.Dump());
                isAuthorized = false;
            }
            else {
                OMV.UUID agentUUID = OMV.UUID.Zero;
                OMV.UUID sessionID = OMV.UUID.Zero;
                OMV.UUID secureSessionID = OMV.UUID.Zero;
                uint circuitCode = 0;
                try {
                    agentUUID = OMV.UUID.Parse(agentId);
                    sessionID = OMV.UUID.Parse(pUserAuth.GetProperty("sId"));
                    secureSessionID = OMV.UUID.Parse(pUserAuth.GetProperty("SSID"));
                    circuitCode = UInt32.Parse(pUserAuth.GetProperty("CC"));
                }
                catch (Exception e) {
                    _RContext.log.Error("{0} ValidateUserAuth: exception parsing validation properties: {1}",
                                _logHeader, e);
                    return false;
                }

                // Put the agent UUID into the waiting info so that the SpaceServer can use it
                pWaitingInfo.agentUUID = agentUUID;

                if (_RContext.parms.ShouldEnforceUserAuth) {
                    // _RContext.log.Debug("{0} ValidateLoginAuth: agentUUID={1}, sessionID={2}, secureSessionID={3}, circuitCode={4}",
                    //             _logHeader, agentUUID, sessionID, secureSessionID, circuitCode);

                    // The user could be in any of the regions on this simulator. Find the one that has the user
                    //     is logged into
                    // If the user logged in via the proper channels, there will be an AgentCircuitData allocated
                    Scene homeScene = null;
                    foreach (var kvp in _RContext.RaguRegions) {
                        Scene sc = kvp.Value.regionScene;
                        // _RContext.log.Debug("{0} ValidateInitialLoginToken: Checking scene {1} for agentId={2}, circuitCode={3}",
                        //             _logHeader, sc.Name, agentId, circuitCode);  
                        if (sc.AuthenticateHandler.GetAgentCircuitData(circuitCode) != null) {
                            // _RContext.log.Debug("{0} ValidateInitialLoginToken: Found circuit code {1} in scene {2}",
                            //         _logHeader, circuitCode, sc.Name);
                            homeScene = sc;
                            // Now that we've found the region the user is logged into, return that info for the caller
                            homeRegion = kvp.Value;
                        }
                    };

                    if (homeScene != null) {
                        AuthenticateResponse authResp = homeScene.AuthenticateHandler.AuthenticateSession(
                                                                sessionID, agentUUID, circuitCode);
                        if (authResp.Authorised) {
                            isAuthorized = true;
                        }
                        else {
                            _RContext.log.Error("{0} ValidateUserAuth: Failed AuthenticateSession test. AgentId={1}",
                                        _logHeader, agentId);
                            isAuthorized = false;
                        }
                    }
                    else {
                        _RContext.log.Error("{0} ValidateUserAuth: Failed to find home scene for agentId={1}",
                                    _logHeader, agentId);
                        isAuthorized = false;
                    }
                }
                else {
                    isAuthorized = true;
                };
            }
            return isAuthorized;
        }

        /*
         * Create the block to send in an OpenConnection that will connect back to this listener
         * This happens when a MakeConnection is sent to a client and we're waiting for
         * them to do the responding OpenSession.
         * This routine builds the parameter block that is sent in the MakeConnection
         *    and remembers the information to await for the OpenSession.
         * @returns either ParamBlock of MakeConnection parameters or null if not
         *    to make a connection for this SpaceServer.
         */
        public static Dictionary<string,object> ParamsForMakeConnection(RaguContext pContext, RaguRegion pRegion, OSAuthToken pServiceAuth) {
            
            // Select the preferred transport for this service
            SpaceServerListener listener = (pContext.RegionListeners[pRegion.regionScene.Name].Values
                                                        .Where(pp => pp.TransportParams.preferred).First());
            BTransportParams parms = listener.TransportParams;

            var propBlock = new Dictionary<string, object>();

            propBlock.Add("transport", parms.transport);
            propBlock.Add("transportURL", listener.ExternalURL);
            propBlock.Add("protocol", parms.protocol);
            propBlock.Add("service", "SpaceServer");
            propBlock.Add("serviceAuth", pServiceAuth.Token);

            return propBlock;
        }
    }

    /* Code to return "/Ragu/Config" but the functionality isn't needed.
       Code kept as an example of using System.Text.Json.

    public class RaguInstanceInfo {
        public string raguVersion;
        public string lodenVersion;
        public string commonUtilVersion;
        public string commonEntitiesVersion;
    }
    public class OpenSimSpecificInfo {
        public OpenSimRegionInfo region;
    }
    public class OpenSimRegionInfo {
        public string name;
        public string location;
        public uint regionLocX;
        public uint regionLocY;
        public uint httpPort;
        public uint regionSizeX;
        public uint regionSizeY;
        public uint regionSizeZ;
    }

    public class RaguConfigInfo {
        public RaguInstanceInfo ragu;
        public BTransportParams[] listeners;
        public OpenSimSpecificInfo opensim;
    }

    public class RaguConfigGETStreamHandler : BaseStreamHandler {
        private readonly string _logHeader = "[RaguConfigGetStreamHandler]";

        private readonly org.herbal3d.Ragu.RaguContext _context;

        private readonly SpaceServerListener _listener;

        // Handler for the HTTP GET request
        public RaguConfigGETStreamHandler(RaguContext pContext, SpaceServerListener pListener, string pPath)
                        : base("GET", pPath, "RaguGET" , "Ragu asset fetcher") {
            _context = pContext;
            _listener = pListener;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            bool authorized = false;
            if (_context.parms.ShouldEnforceConfigAccessAuthorization) {
                string authValue = ExtractAuthorization(httpRequest);
                if (authValue != null) {
                    OSAuthToken authToken = OSAuthToken.FromString(authValue);
                    WaitingInfo waitingInfo = SpaceServerCC.CreateWaitingInfo(OMV.UUID.Zero, authToken);
                    if (_listener.ValidateInitialLoginToken(authToken, waitingInfo)) {
                        authorized = true;
                    }
                }
            }
            else {
                authorized = true;
            }

            if (authorized) {
                RaguConfigInfo raguInfo = new RaguConfigInfo() {
                    ragu = new RaguInstanceInfo() {
                        raguVersion = org.herbal3d.Ragu.VersionInfo.longVersion,
                        lodenVersion = org.herbal3d.Loden.VersionInfo.longVersion,
                        commonEntitiesVersion = org.herbal3d.cs.CommonEntities.VersionInfo.longVersion,
                        commonUtilVersion = org.herbal3d.cs.CommonUtil.VersionInfo.longVersion
                    },
                    listeners = _listener.TransportParams,
                    opensim = new OpenSimSpecificInfo() {
                        region = new OpenSimRegionInfo() {
                            name = _context.scene.RegionInfo.RegionName,
                            location = _context.scene.RegionInfo.RegionLocX + "," + _context.scene.RegionInfo.RegionLocY,
                            regionLocX = _context.scene.RegionInfo.RegionLocX,
                            regionLocY = _context.scene.RegionInfo.RegionLocY,
                            httpPort = _context.scene.RegionInfo.HttpPort,
                            regionSizeX = _context.scene.RegionInfo.RegionSizeX,
                            regionSizeY = _context.scene.RegionInfo.RegionSizeY,
                            regionSizeZ = _context.scene.RegionInfo.RegionSizeZ,
                        }
                    }
                };

                // _context.log.Debug("{0} ragu version={1}", _logHeader, raguInfo.ragu.raguVersion);
                var options = new JsonSerializerOptions {
                    WriteIndented = true,   // pretty print
                    IncludeFields = true    // normally fields are not serialized
                };
                string jsonString = JsonSerializer.Serialize<RaguConfigInfo>(raguInfo, options);

                int jsonLength = jsonString.Length;
                if (jsonLength > 0) {
                    httpResponse.StatusCode = (int)System.Net.HttpStatusCode.OK;
                    httpResponse.ContentLength = jsonLength;
                    httpResponse.ContentType = "application/json";
                    httpResponse.Body.Write(Encoding.ASCII.GetBytes(jsonString), 0, jsonLength);
                    // _context.log.Debug("{0} Returning asset fn={1}", _logHeader, jsonString);
                }
                else {
                    httpResponse.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                    _context.log.Debug("{0} Failed config fetch", _logHeader);
                }
                // Cross-Origin Resource Sharing with simple requests
                httpResponse.AddHeader("Access-Control-Allow-Origin", "*");
            }
            else {
                httpResponse.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
            }

            return null;
        }

        // The authorization token is either in the 'Authorization' header or in the URL
        private string ExtractAuthorization(IOSHttpRequest pReq) {
            string ret = null;
            // Check for 'Authorization' in the request header
            NameValueCollection headers = pReq.Headers;
            string authValue = headers.GetOne("Authorization");
            if (authValue != null) {
                ret = authValue;
            }
            else {
                // if no 'Authorization', see if an access token was embedded in the URL.
                // Tokens will be for the form ".../bearer-token/..." where "bearer-" is the
                //     flag for the token, and "token" is the actual token string.
                string[] segments = pReq.Url.Segments;
                foreach (string segment in segments.Reverse()) {
                    if (segment.StartsWith("bearer-")) {
                        authValue = segment.Substring(7);
                        if (authValue.EndsWith("/")) {
                            authValue = authValue.Substring(0, authValue.Length - 1);
                        }
                        ret = authValue;
                        break;
                    }
                }
            }
            return ret;
        }
    }
    */
}