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

using org.herbal3d.OSAuth;
using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public abstract class SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerBase]";

        public string LayerType = "XX";

        public readonly RaguContext RContext;

        // The UUID of the logged in agent we're working as
        public OMV.UUID AgentUUID;
        public OMV.UUID SessionUUID;

        protected CancellationTokenSource _cancellerSource;
        protected BTransport _transport;
        protected BProtocol _protocol;
        protected BasilConnection _connection;

        public SpaceServerBase(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) {
            RContext = pContext;
            _cancellerSource = pCanceller;
            _transport = pTransport;
            RContext.SpaceServers.Add(this);
        }

        // General OpenSession request processing.
        // The message is parsed and it calls SpaceServerBase.ValidataLoginAuth to validate the session
        //     and, if successful, calls SpaceServerBase.OpenSessionProcessing to do the SpaceServer operations.
        //     Both these functions can be overridden.
        public void ProcessOpenSessionReq(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            string errorReason = "";
            // Get the login information from the OpenConnection
            OSAuthToken clientAuth = OpenSessionResp.GetClientAuth(pMsg);
            if (clientAuth != null) {
                string incomingAuthString = pMsg.Auth;
                if (incomingAuthString != null) {
                    OSAuthToken loginAuth = OSAuthToken.FromString(incomingAuthString);

                    // Create a new auth token for communication into this.
                    // This 'incomingAuth' is sent with the OpenSession response to be used
                    //     by the client for future communication
                    OSAuthToken incomingAuth = OSAuthToken.SimpleToken();
                    // 'clientAuth' is the token sent by the client that this should send
                    //     with future messages to authenticate me.
                    OSAuthToken outgoingAuth = clientAuth;
                    pConnection.SetAuthorizations(incomingAuth, outgoingAuth);

                    // Verify this initial incoming authorization.
                    // If CC, this is a login and is overridden to check account parameters.
                    // Other SpaceServers use the default validater that checks if waiting for OpenSession.
                    if (ValidateLoginAuth(loginAuth, out WaitingInfo waitingInfo)) {

                        // The user checks out so construct the success response
                        var openSessionRespParams = new OpenSessionResp() {
                            ServerVersion = VersionInfo.longVersion,
                            ServerAuth = incomingAuth.Token
                        };
                        pConnection.SendResponse(pMsg, openSessionRespParams);
                        
                        // The waiting info also holds the UUID of the scene presence this is associated with
                        AgentUUID = waitingInfo.agentUUID;


                        // Call the over-ridable function to do any layer specific processing
                        OpenSessionProcessing(pConnection, loginAuth, waitingInfo);
                    }
                    else {
                        errorReason = String.Format("Login credentials not valid ({0})", LayerType);
                    }
                }
                else {
                    errorReason = String.Format("Login credentials not supplied ({0}, serviceAuth)", LayerType);
                }
            }
            else {
                errorReason = String.Format("Connection auth not supplied ({0}, clientAuth)", LayerType);
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
            bool ret = false;
            WaitingInfo waitingInfo = null;
            string auth = pUserAuth.Token;
            lock (RContext.waitingForMakeConnection) {
                if (RContext.waitingForMakeConnection.TryGetValue(auth, out WaitingInfo foundInfo)) {
                    // RContext.log.Debug("{0}: login auth successful. Waited {1} seconds", _logHeader,
                    //     (DateTime.Now - waitingInfo.whenCreated).TotalSeconds);

                    // Remove the found waiting info so clients can only connect once
                    RContext.waitingForMakeConnection.Remove(auth);

                    // Return whether the authentication info matches (always true since the auth is the key)
                    // ret = waitingInfo.incomingAuth.Equals(pUserAuth);
                    waitingInfo = foundInfo;
                    ret = true;
                }
                else {
                    RContext.log.Error("{0}: OpenSession with unknown token. Token: {1}", _logHeader, auth);
                }
            }
            pWaitingInfo = waitingInfo;
            return ret;
        }

        // WHen sending an OpenSession, this remembers the credentials of the request
        //     so the response can be validated.
        protected WaitingInfo RememberWaitingForOpenSession(OMV.UUID pAgentUUID) {
            WaitingInfo waiting = new WaitingInfo(pAgentUUID);
            lock (RContext.waitingForMakeConnection) {
                RContext.waitingForMakeConnection.Add(waiting.incomingAuth.Token, waiting);
                // RContext.log.Debug("SpaceServerBase.RememberWaitingForOpenSession: itoken={0}", waiting.incomingAuth.Token);
            }
            return waiting;
        }

        // Called when OpenSession is received to do any SpaceServer specific processing
        protected abstract void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken pLoginAuth, WaitingInfo pWaitingInfo);

        // Called when CloseSession is received
        public virtual void CloseSessionProcessing(BasilConnection pConnection) {
            Shutdown();
        }
        public virtual void Shutdown() {
            RContext.log.Info("{0} Shutdown for SpaceServer {1} for user {2}", _logHeader, LayerType, AgentUUID);
            // If there is a connected user, disconnect them
            ShutdownUserAgent(LayerType + " shutdown");
            // wait a little bit so shutdown messages can make it to the user
            Thread.Sleep(1000);
            // Tell everyone around that we're going down
            _cancellerSource.Cancel();
            // Close and release the connection
            if (_connection != null) {
                _connection.Stop();
                _connection = null;
            }
            RContext.SpaceServers.Remove(this);
        }


        // Called when connection state changes.
        // Called with the new state and a reference to the SpaceServer the connection is for
        protected virtual void ProcessConnectionStateChange(BConnectionStates pConnectionState, BasilConnection pConn) {
            switch (pConnectionState) {
                case BConnectionStates.OPEN: {
                    break;
                }
                case BConnectionStates.CLOSED: {
                    // RContext.log.Debug("{0}: ProcessConnectionStateChange CLOSED for {1}. Shutting down",
                    //                 _logHeader, LayerType);
                    Shutdown();
                    break;
                }
                case BConnectionStates.ERROR: {
                    // RContext.log.Debug("{0}: ProcessConnectionStateChange ERROR for {1}. Shutting down",
                    //                 _logHeader, LayerType);
                    Shutdown();
                    break;
                }
                case BConnectionStates.CLOSING: {
                    Shutdown();
                    break;
                }
                default: {
                    break;
                }
            }
        }

        // Overridden to processes user account disconnection.
        // Only used by SpaceServerCC.
        protected virtual void ShutdownUserAgent(string pReason) {
            // RContext.log.Info("{0}: {1} user shutdown", _logHeader, LayerType);
        }

    }
    // A message processor for SpaceServer's while they are waiting for an OpenConnection.
    // THis is used initially when a SpaceServer is created and is replaced with a full
    //     message processor when the OpenConnection is successful.
    public class ProcessMessagesOpenConnection : IncomingMessageProcessor {
        SpaceServerBase _ssContext;
        public ProcessMessagesOpenConnection(SpaceServerBase pContext) : base(pContext) {
            _ssContext = pContext;
            // _ssContext.RContext.log.Debug("SpaceServerBase.ProcessMessageOpenConnection: new. Server type = {0}", _ssContext.LayerType);
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            // _ssContext.RContext.log.Debug("SpaceServerBase.ProcessMessageOpenConnection: mgs received. Server type = {0}", _ssContext.LayerType);
            switch (pMsg.Op) {
                case (uint)BMessageOps.OpenSessionReq:
                    _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
                    break;
                case (uint)BMessageOps.CloseSessionReq: {
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    pConnection.Send(resp);
                    _ssContext.Shutdown();
                    break;
                }
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
}
