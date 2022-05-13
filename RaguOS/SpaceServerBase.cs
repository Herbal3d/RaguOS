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

namespace org.herbal3d.Ragu {

    public abstract class SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerBase]";

        public string LayerType = "XX";

        public readonly RaguContext RContext;
        protected CancellationTokenSource _cancellerSource;
        protected BTransport _transport;
        protected BProtocol _protocol;
        protected BasilConnection _connection;

        public SpaceServerBase(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) {
            RContext = pContext;
            _cancellerSource = pCanceller;
            _transport = pTransport;
        }

        // General OpenSession request processing.
        // The message is parsed and it calls SpaceServerBase.ValidataLoginAuth to validate the session
        //     and, if successful, calls SpaceServerBase.OpenSessionProcessing to do the SpaceServer operations.
        //     Both these functions can be overridden.
        public void ProcessOpenSessionReq(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            string errorReason = "";
            // Get the login information from the OpenConnection
            OSAuthToken clientAuth = AbOpenSession.GetClientAuth(pMsg);
            if (clientAuth != null) {
                string incomingAuthString = pMsg.Auth;
                if (incomingAuthString != null) {
                    OSAuthToken loginAuth = OSAuthToken.FromString(incomingAuthString);

                    OSAuthToken incomingAuth = new OSAuthToken();
                    OSAuthToken outgoingAuth = clientAuth;
                    pConnection.SetAuthorizations(incomingAuth, outgoingAuth);

                    // have the info to try and log the user in
                    if (ValidateLoginAuth(loginAuth)) {

                        // The user checks out so construct the success response
                        BMessage resp = BasilConnection.MakeResponse(pMsg);
                        resp.IProps.Add(AbOpenSession.ServerVersionProp, RContext.ServerVersion);
                        resp.IProps.Add(AbOpenSession.ServerAuthProp, incomingAuth.Token);
                        pConnection.Send(resp);

                        OpenSessionProcessing(pConnection, loginAuth);
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
        protected virtual bool ValidateLoginAuth(OSAuthToken pUserAuth) {
            bool ret = false;
            string auth = pUserAuth.Token;
            lock (RContext.waitingForMakeConnection) {
                if (RContext.waitingForMakeConnection.TryGetValue(auth, out WaitingInfo waitingInfo)) {
                    // RContext.log.Debug("{0}: login auth successful. Waited {1} seconds", _logHeader,
                    //     (DateTime.Now - waitingInfo.whenCreated).TotalSeconds);
                    RContext.waitingForMakeConnection.Remove(auth);
                    ret = true;
                }
                else {
                    RContext.log.Debug("{0}: login auth unsuccessful. Token: {1}", _logHeader, auth);
                }
            }
            return ret;
        }

        // WHen sending an OpenSession, this remembers the credentials of the request
        //     so the response can be validated.
        protected WaitingInfo RememberWaitingForOpenSession() {
            WaitingInfo waiting = new WaitingInfo();
            lock (RContext.waitingForMakeConnection) {
                RContext.waitingForMakeConnection.Add(waiting.incomingAuth.Token, waiting);
                // RContext.log.Debug("SpaceServerBase.RememberWaitingForOpenSession: itoken={0}", waiting.incomingAuth.Token);
            }
            return waiting;
        }

        // Called when OpenSession is received to do any SpaceServer specific processing
        protected abstract void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken pLoginAuth);

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
                case (uint)BMessageOps.MakeConnectionResp:
                    // We will get responses from our MakeConnections
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Session is not open. AA";
                    pProtocol.Send(resp);
                    break;
            }
        }
    }
}
