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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using org.herbal3d.b.protocol;
using org.herbal3d.transport;
using org.herbal3d.OSAuth;
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

using OpenSim.Framework;

namespace org.herbal3d.Ragu {

    // Function called when a connection is available for a SpaceServer
    public delegate SpaceServerBase CreateSpaceServerProcessor(RaguContext pContext,
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
    public class SpaceServerListener {
        private static string _logHeader = "[SpaceServerListener]";
        RaguContext _RContext;
        CreateSpaceServerProcessor _creator;

        BTransportParams[] _transportParams;
        CancellationTokenSource _canceller;

        // Listen for a connection and call the passed SpaceServer creater when a connection is made.
        // This canceller is for the whole service. A new one is created for the connection.
        public SpaceServerListener(
                        BTransportParams[] transportParams,
                        CancellationTokenSource canceller,
                        RaguContext context
            ) {

            _transportParams = transportParams;
            _canceller = canceller;
            _RContext = context;

            // For the moment, we assume there is only the WS transport.
            // Eventually, the parameters will specifiy multiple transports
            //    and this routine will open several listeners.

            foreach (var xportParam in transportParams) {
                try {
                    _RContext.log.Debug("{0} Creating listener. Transport={1}, Protocol={2}, Port={3}",
                                    _logHeader, xportParam.transport, xportParam.protocol, xportParam.port);
                    if (xportParam is BTransportWSParams) {
                        _ = BTransportWS.ConnectionListener(
                            param: xportParam as BTransportWSParams,
                            logger: _RContext.log,
                            connectionProcessor: (pTrans, pCan) => SpaceServerListener.AcceptConnection(pTrans, _RContext, this, pCan),
                            cancellerSource: _canceller
                        );
                    }
                }
                catch (Exception ee) {
                    _RContext.log.Debug("{0} Exception creating listener for {1}: {2}", _logHeader, xportParam.transport, ee);
                }
            }
            // start listening and call SpaceServer creator when a connection is made
        }
        // A connection has been received.
        // Listen for an OpenSession.
        private static void AcceptConnection(BTransport pTrans,
                                            RaguContext pRContext,
                                            SpaceServerListener pListener,
                                            CancellationTokenSource pCanceller) {

            // The protocol for the initial OpenSession is always JSON
            BProtocolJSON protocol = new BProtocolJSON(null, pTrans, pRContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            BasilConnection connection = new BasilConnection(protocol, pRContext.log);
            connection.SetOpProcessor(new ProcessMessagesOpenConnection(pListener, pRContext),
                                    pListener.ProcessConnectionStateChange);
            connection.Start();
            pRContext.log.Debug("{0} AcceptConnection. xportT={1}, protoT={2}", _logHeader, pTrans.TransportType, protocol.ProtocolType);
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
                _RContext.log.Debug("SpaceServerListenter.Process: msg received: {0}", pMsg);    // DEBUG DEBUG
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
            string errorReason = "";
            // Get the login information from the OpenConnection
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
                            SpaceServerBase ss = waitingInfo.createSpaceServer(_RContext, waitingInfo, pConnection, pMsg, _canceller);
                            _RContext.addSpaceServer(ss);

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
            bool isAuthorized = false;
            string auth = pUserAuth.Token;
            WaitingInfo waitingInfo = _RContext.GetWaitingForOpenSession(auth);
            if (waitingInfo != null) {
                // Return whether the authentication info matches (always true since the auth is the key)
                // ret = waitingInfo.incomingAuth.Equals(pUserAuth);
                isAuthorized = true;
            }
            else {
                // There is no WaitingInfo. Maybe this is a login rather than a MakeConnection
                _RContext.log.Error("{0}: OpenSession with unknown token. Token: {1}", _logHeader, auth);
                waitingInfo = SpaceServerCC.CreateWaitingInfo(OMV.UUID.Zero, pUserAuth);
                try {
                    string agentId = pUserAuth.GetProperty("aId");
                    OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
                    waitingInfo.agentUUID = agentUUID;
                    // Create a 'waitingInfo' as if we'd been waiting for this initial OpenSession
                    if (_RContext.parms.ShouldEnforceUserAuth) {
                        OMV.UUID sessionID = OMV.UUID.Parse(pUserAuth.GetProperty("sId"));
                        OMV.UUID secureSessionID = OMV.UUID.Parse(pUserAuth.GetProperty("SSID"));
                        uint circuitCode = UInt32.Parse(pUserAuth.GetProperty("CC"));
                        // RContext.log.Debug("{0} ValidateLoginAuth: agentUUID={1}, sessionID={2}, secureSessionID={3}, circuitCode={4}",
                        //             _logHeader, agentUUID, sessionID, secureSessionID, circuitCode);

                        // If the user logged in via the proper channels, there will be an AgentCircuitData allocated
                        AgentCircuitData acd = _RContext.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);
                        if (acd != null) {
                            if (acd.circuitcode == circuitCode) {
                                if (acd.SessionID == sessionID) {
                                    if (acd.SecureSessionID == secureSessionID) {
                                        isAuthorized = true;
                                    }
                                    else {
                                        _RContext.log.Error("{0} ValidateUserAuth: Failed secureSessionID test. AgentId={1}",
                                                    _logHeader, agentId);
                                    }
                                }
                                else {
                                    _RContext.log.Error("{0} ValidateUserAuth: Failed sessionId test. AgentId={1}",
                                                _logHeader, agentId);
                                }
                            }
                            else {
                                _RContext.log.Error("{0} ValidateUserAuth: Failed circuitCode test. AgentId={1}",
                                            _logHeader, agentId);
                            }
                        }
                    }
                    else {
                        isAuthorized = true;
                    };
                }
                catch (Exception e) {
                    _RContext.log.Error("{0} ValidateUserAuth: exception authorizing: {1}", _logHeader, e);
                    isAuthorized = false;
                }
            }
            pWaitingInfo = waitingInfo;
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
        public virtual Dictionary<string,object> ParamsForMakeConnection(string pExternalHostname, OSAuthToken pServiceAuth) {
            
            // Select the preferred transport for this service
            BTransportParams parms = _transportParams.Where(pp => pp.preferred).First();
            // Build the block of parameters needed for  the MakeConnection
            return new Dictionary<string, object>() {
                { "transport",    parms.transport },
                { "transportURL", parms.ExternalURL(pExternalHostname) },
                { "protocol",     parms.protocol },
                { "service",      "SpaceServer" },
                { "serviceAuth",  pServiceAuth.Token }
            };
        }
    }
}