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

using org.herbal3d.OSAuth;
using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages after OpenSession -- regular messages
    class ProcessCCIncomingMessages : IncomingMessageProcessor {
        SpaceServerCC _ssContext;
        public ProcessCCIncomingMessages(SpaceServerCC pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            switch (pMsg.Op) {
                case (uint)BMessageOps.UpdatePropertiesReq: {
                    // TODO:
                    break;
                }
                case (uint)BMessageOps.CloseSessionReq: {
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    pConnection.Send(resp);
                    _ssContext.CloseSessionProcessing(pConnection);
                    break;
                }
                default: {
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Unsupported operation on SpaceServer" + _ssContext.LayerType;
                    pConnection.Send(resp);
                    break;
                }
            }
        }
    }

    public class SpaceServerCC : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerCC]";

        public static readonly string SpaceServerType = "CC";

        // When initialized, an OSAuthToken is passed that has all the encoded user login info
        OSAuthToken userInfo;
        // Token for incoming connection to this instance
        OSAuthToken incomingAuth;

        // SpaceServer created on the initial, authorized OpenSession from a user
        // Handles the in-world presence of the world and causes the other SpaceServers to be connected
        //    to the client.
        public SpaceServerCC(RaguContext pContext,
                            CancellationTokenSource pCanceller,
                            WaitingInfo pWaitingInfo,
                            BasilConnection pConnection,
                            BMessage pMsg) 
                        : base(pContext, pCanceller, pConnection) {
            LayerType = SpaceServerType;

            // Save some connection parameters for use by Start()
            userInfo = pWaitingInfo.incomingAuth;
            incomingAuth = pWaitingInfo.incomingAuth;

            pConnection.SetOpProcessor(new ProcessCCIncomingMessages(this), ProcessConnectionStateChange);
        }

        public override void Start() {
            try {
                // Create the Circuit and ScenePresence for the user
                CreateOpenSimPresence(userInfo);

                // For each of the layers, set up the listeners to expect an OpenConnection
                //    and send a MakeConnection to the new client to send an OpenConnection
                //    to the listeners. The WaitingInfo saves the authentication information.

                SpaceServerStatic.MakeConnectionToSpaceServer(_connection, AgentUUID, _RContext);

                SpaceServerActors.MakeConnectionToSpaceServer(_connection, AgentUUID, _RContext);

                SpaceServerDynamic.MakeConnectionToSpaceServer(_connection, AgentUUID, _RContext);

                SpaceServerEnviron.MakeConnectionToSpaceServer(_connection, AgentUUID, _RContext);

            }
            catch (Exception e) {
                _RContext.log.Error("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }

        public static WaitingInfo CreateWaitingInfo(OMV.UUID pAgentUUID, OSAuthToken pIncomingAuth) {
            return new WaitingInfo() {
                agentUUID = pAgentUUID,
                incomingAuth = pIncomingAuth,
                spaceServerType = SpaceServerCC.SpaceServerType,
                createSpaceServer = (pC, pW, pConn, pMsgX, pCan) => {
                    return new SpaceServerCC(pC, pCan, pW, pConn, pMsgX);
                }
            };
        }

        // Create  the OpenSimulator ScenePResence and the associated structures
        //    that fake out the simulator so it thinks it is connected to a viewer.
        // Code cribbed from NPC (INPCModule.cs), DSG (http://github.com/intelvwi/DSG),
        //    and LoginService (LLLoginService.cs).
        // The user OSAuthToken contains extra login information needed for creating
        //    the circuit, etc.
        // See code in OpenSim.Tests.Common.SceneHelpers.AddScenePresence() for steps needed
        private bool CreateOpenSimPresence(OSAuthToken pUserAuth) {
            bool ret = false;

            try {
                // Get identifying info from the auth
                string agentId = pUserAuth.GetProperty("aId");
                OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
                OMV.UUID sessionUUID = OMV.UUID.Parse(pUserAuth.GetProperty("sId"));
                string firstName = pUserAuth.GetProperty("FN");
                string lastName = pUserAuth.GetProperty("LN");
                uint circuitCode = UInt32.Parse(pUserAuth.GetProperty("CC"));

                // The login operation created the initial circuit
                AgentCircuitData acd = _RContext.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);

                if (acd != null) {
                    if (acd.SessionID == sessionUUID) {
                        // The login operation has:
                        //      called IPresenceService.LoginAgent()
                        //      created the AgentCircuitData
                        //      called IGridUserService.LoggedIn()
                        // This code needs to create the IClientAPI and add it to the scene

                        IClientAPI newClient = new RaguAvatar(firstName, lastName,
                                                        agentUUID,
                                                        sessionUUID,
                                                        acd.startpos,   /* initial position */
                                                        OMV.UUID.Zero,  /* owner */
                                                        true,           /* senseAsAgent */
                                                        _RContext.scene,
                                                        acd.circuitcode,
                                                        _RContext,
                                                        this);

                        // Remember to process things when the user logs out
                        newClient.OnLogout += LogoutHandler;

                        // Start the client by adding it to the scene and doing event subscriptions
                        // This does a Scene.AddNewAgent() and CompleteMovementIntoRegion()
                        newClient.Start();

                        // Get the ScenePresence just to make sure we can
                        if (_RContext.scene.TryGetScenePresence(agentUUID, out ScenePresence sp)) {
                            _RContext.log.Info("{0} Successful login for {1} {2} ({3})",
                                        _logHeader, firstName, lastName, agentId);
                            AgentUUID = agentUUID;
                            SessionUUID = sessionUUID;

                            sp.SendAvatarDataToAllAgents();

                            // Let the other agents know about us
                            ret = true;
                        }
                        else {
                            _RContext.log.Error("{0} Failed to create ScenePresence for {1} {2} ({3})",
                                        _logHeader, firstName, lastName, agentId);
                        }
                    }
                    else {
                        _RContext.log.Error("{0} CreateOpenSimPresence: AgentCircuitData does not match SessionID. AgentID={1}",
                                _logHeader, agentId);
                    }
                }
                else {
                    _RContext.log.Error("{0} CreateOpenSimPresence: presence was not created. AgentID={1}",
                                _logHeader, agentId);
                }
            }
            catch (Exception e) {
                _RContext.log.Error("{0} Exception creating presence: {1}", _logHeader, e);
                ret = false;
            }

            return ret;
        }

        // 
        // This is called before the connections are closed.
        // Attempt is made to cause events to happen that clean up the
        //    connection with the client (like removing the avatar from the scene, etc).
        protected override void ShutdownUserAgent(string pReason) {
            base.ShutdownUserAgent(pReason);
            if (AgentUUID != null) {
                _RContext.scene.CloseAgent(AgentUUID, false);
            }
            else {
                _RContext.log.Error("{0} ShutdownUserAgent: no AgentUUIT", _logHeader);
            }
        }

        // The user has requested a disconnection. Undo the work of creating the presence.
        // This is attached to RaguAvatar.OnLogout to know when Logout operation is done
        public void LogoutHandler(IClientAPI pClient) {
            // We are passed the connection
            RaguAvatar rClient = pClient as RaguAvatar;
            if (rClient != null && !rClient.IsLoggingOut) {
                rClient.IsLoggingOut = true;
                // The connection has a pointer back to the SpaceServerCC that created the presence
                SpaceServerCC handler = rClient.ConnectingSpaceServer as SpaceServerCC;
                if (handler != null) {
                    handler.ShutdownUserAgent("Logout");
                }
            }
        }
    }
}
