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

using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages when we're waiting for the OpenSession.
    class ProcessDynamicOpenConnection : IncomingMessageProcessor {
        SpaceServerStatic _ssContext;
        public ProcessDynamicOpenConnection(SpaceServerStatic pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. Static";
                pProtocol.Send(resp);
            }
        }
    }
    class ProcessDynamicIncomingMessages : IncomingMessageProcessor {
        SpaceServerDynamic _ssContext;
        public ProcessDynamicIncomingMessages(SpaceServerDynamic pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. Static";
                pProtocol.Send(resp);
            }
        }
    }

    public class SpaceServerDynamic : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerDynamic]";

        public static readonly string StaticLayerType = "Dynamic";

        private RaguContext _rContext;

        public SpaceServerDynamic(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            _rContext = pContext;
            LayerType = StaticLayerType;
        }
        public void ProcessOpenSessionReq(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            string errorReason = "";
            // Get the login information from the OpenConnection
            if (pMsg.IProps.TryGetValue("clientAuth", out string clientAuthToken)) {
                if (pMsg.IProps.TryGetValue("Auth", out string serviceAuth)) {
                    // have the info to try and log the user in
                    OSAuthToken loginAuth = OSAuthToken.FromString(serviceAuth);
                    if (ValidateLoginAuth(loginAuth)) {
                        // The user checks out so construct the success response
                        OSAuthToken incomingAuth = new OSAuthToken();
                        OSAuthToken outgoingAuth = OSAuthToken.FromString(clientAuthToken);
                        pConnection.SetAuthorizations(incomingAuth, outgoingAuth);

                        // We also have a full command processor
                        pConnection.SetOpProcessor(new ProcessDynamicIncomingMessages(this));

                        BMessage resp = BasilConnection.MakeResponse(pMsg);
                        resp.IProps.Add("ServerVersion", _context.ServerVersion);
                        resp.IProps.Add("ServerAuth", incomingAuth.Token);
                        pConnection.Send(resp);

                        // Send the static region information to the user
                        Task.Run(() => {
                            StartConnection(pConnection, loginAuth);
                        });
                    }
                    else {
                        errorReason = "Login credentials not valid";
                    }
                }
                else {
                    errorReason = "Login credentials not supplied (serviceAuth)";
                }
            }
            else {
                errorReason = "Connection auth not supplied (clientAuth)";
            }

            // If an error happened, return error response
            if (errorReason.Length > 0) {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = errorReason;
                pConnection.Send(resp);
            }
        }

        private bool ValidateLoginAuth(OSAuthToken pUserAuth) {
            bool ret = false;
            string auth = pUserAuth.Token;
            lock (_context.waitingForMakeConnection) {
                if (_context.waitingForMakeConnection.TryGetValue(auth, out WaitingInfo waitingInfo)) {
                    _context.log.Debug("{0}: login auth successful. Waited {1} seconds", _logHeader,
                        (DateTime.Now - waitingInfo.whenCreated).TotalSeconds);
                    _context.waitingForMakeConnection.Remove(auth);
                    ret = true;
                }
                else {
                    _context.log.Debug("{0}: login auth unsuccessful. Token: {1}", _logHeader, auth);
                }
            }
            return ret;
        }

        private void StartConnection(BasilConnection pConnection, OSAuthToken pUserAuth) {
            return;
        }


    }
}
