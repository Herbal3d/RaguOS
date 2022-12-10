// Copyright (c) 2012 Robert Adams
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
using System.Threading;
using System.Threading.Tasks;

using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.OSAuth;

using OpenSim.Framework;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages after we're connected up
    class ProcessEnvironIncomingMessages : IncomingMessageProcessor {
        SpaceServerEnviron _ssContext;
        public ProcessEnvironIncomingMessages(SpaceServerEnviron pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            switch (pMsg.Op) {
                case (uint)BMessageOps.UpdatePropertiesReq:
                    // TODO:
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Unsupported operation on SpaceServer" + _ssContext.LayerType;
                    pConnection.Send(resp);
                    break;
            }
        }
    }

    public class SpaceServerEnviron : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerEnviron]";

        public static readonly string SpaceServerType = "Environ";

        public string TopMenuId;
        public string StatusDialogId;
        public string ChatDialogId;

        public SpaceServerEnviron(RaguContext pContext,
                                CancellationTokenSource pCanceller,
                                WaitingInfo pWaitingInfo,
                                BasilConnection pConnection,
                                BMessage pMsg) 
                        : base(pContext, pCanceller, pConnection) {
            LayerType = SpaceServerType;

            pConnection.SetOpProcessor(new ProcessEnvironIncomingMessages(this), ProcessConnectionStateChange);
        }

        public override void Start() {
            // Set up the UI
            Task.Run(async () => {
                StartEnviron(_connection);
                await StartUI(_connection);
            });
        }
        public override async void Stop() {
            StopEnviron(this._connection);
            await StopUI(this._connection);
            base.Stop();
        }


        // Send a MakeConnection for connecting to a SpaceServer of this type.
        public static void MakeConnectionToSpaceServer(BasilConnection pConn,
                                                    OMV.UUID pAgentUUID,
                                                    RaguContext pRContext) {

            // The authentication token that the client will send with the OpenSession
            OSAuthToken incomingAuth = OSAuthToken.SimpleToken();

            // Information that will be used to process the incoming OpenSession
            var wInfo = new WaitingInfo() {
                agentUUID = pAgentUUID,
                incomingAuth = incomingAuth,
                spaceServerType = SpaceServerEnviron.SpaceServerType,
                createSpaceServer = (pC, pW, pConn, pMsgX, pCan) => {
                    return new SpaceServerEnviron(pC, pCan, pW, pConn, pMsgX);
                }
            };
            pRContext.RememberWaitingForOpenSession(wInfo);

            // Create the MakeConnection and send it
            var pBlock = pRContext.Listener.ParamsForMakeConnection(pRContext.HostnameForExternalAccess, incomingAuth);
            _ = pConn.MakeConnection(pBlock);
        }

        // Start the sun, moon, and sky
        private void StartEnviron(BasilConnection pConn) {
        }
        private void StopEnviron(BasilConnection pConn) {
        }
        private void StartChatDialog(BasilConnection pConn) {
            var em = _RContext.scene.EventManager;
            em.OnChatFromWorld += Event_OnChatFromWorld;
            em.OnChatFromClient += Event_OnChatFromClient;
            em.OnChatBroadcast += Event_OnChatBroadcast;
            em.OnIncomingInstantMessage += Event_OnIncomingInstantMessage;
            em.OnUnhandledInstantMessage += Event_OnUnhandledInstantMessage;
        }
        private void StopChatDialog(BasilConnection pConn) {
            var em = _RContext.scene.EventManager;
            em.OnChatFromWorld -= Event_OnChatFromWorld;
            em.OnChatFromClient -= Event_OnChatFromClient;
            em.OnChatBroadcast -= Event_OnChatBroadcast;
            em.OnIncomingInstantMessage -= Event_OnIncomingInstantMessage;
            em.OnUnhandledInstantMessage -= Event_OnUnhandledInstantMessage;
        }

        // Setup and initialize the user interface
        private async Task StartUI(BasilConnection pConn) {
            // Create the first top menu
            TopMenuId = await CreateUIItem(pConn, "topMenu", "./Dialogs/topMenu.html", "menu");

            // Floating dialog with rendering statistics
            if (_RContext.parms.UIStatusDialog) {
                StatusDialogId = await CreateUIItem(pConn, "Status", "./Dialogs/status.html", "bottom right");
            };

            // Floating dialog with chat
            if (_RContext.parms.UIChatDialog) {
                StartChatDialog(pConn);
                ChatDialogId = await CreateUIItem(pConn, "chat", "./Dialogs/chatDialog.html", "bottom left");
            }
        }

        private async Task StopUI(BasilConnection pConn) {
            if (ChatDialogId is not null) {
                StopChatDialog(pConn);
                await pConn.DeleteItem(ChatDialogId);
            }
            if (StatusDialogId is not null) {
                await pConn.DeleteItem(StatusDialogId);
            }
            await pConn.DeleteItem(TopMenuId);
        }

        private async Task<string> CreateUIItem(BasilConnection pConn, string pName, string pUrl, string pPlacement) {
            string dialogId = "";
            AbilityList abilProps = new AbilityList();
            abilProps.Add(new AbDialog() {
                DialogUrl = pUrl,
                DialogName = pName,
                DialogPlacement = pPlacement
            });
            BMessage resp = await pConn.CreateItem(abilProps);
            if (String.IsNullOrEmpty(resp.Exception)) {
                dialogId = resp.IId;
                _RContext.log.Debug("{0} created {1}. Id={2}", _logHeader, pName, dialogId);
            }
            else {
                _RContext.log.Error("{0} Error creating {1}: {2}", _logHeader, pName, resp.Exception);
            }
            return dialogId;
        }
        private void Event_OnChatFromWorld(Object pSender, OSChatMessage pChatMessage) {
            _RContext.log.Debug("{0} Event_OnChatFromWorld", _logHeader);
            SendInstantMessage("World", pSender, pChatMessage);
        }
        private void Event_OnChatFromClient(Object pSender, OSChatMessage pChatMessage) {
            _RContext.log.Debug("{0} Event_OnChatFromClient", _logHeader);
            SendInstantMessage("Client", pSender, pChatMessage);
        }
        private void Event_OnChatBroadcast(Object pSender, OSChatMessage pChatMessage) {
            _RContext.log.Debug("{0} Event_OnChatBroadcast", _logHeader);
            SendInstantMessage("Broadcast", pSender, pChatMessage);
        }
        private void Event_OnIncomingInstantMessage(GridInstantMessage pGMsg) {
            SendInstantMessage(pGMsg, false);
        }
        private void Event_OnUnhandledInstantMessage(GridInstantMessage pGMsg) {
            SendInstantMessage(pGMsg, true);
        }
        // The user has received an instant message. Send it to the client.
        private void SendInstantMessage(string pSource, Object pSender, OSChatMessage pCMsg) {
            _RContext.log.Debug("{0} SendInstantMessage. from={1}({2}) to={3} message={4}",
                            _logHeader, pCMsg.SenderUUID, pCMsg.From, pCMsg.Destination, pCMsg.Message);
            IClientAPI client = pSender as IClientAPI;
            string senderName = pCMsg.From;
            string senderUUID = pCMsg.SenderUUID.ToString();
            if (client != null) {
                if (senderName == null || senderName.Length == 0) {
                    senderName = client.Name;
                    senderUUID = client.AgentId.ToString();
                }
            }
            AbilityList abilProps = new AbilityList();
            abilProps.Add(new AbOSChat() {
                OSChatType = AbOSChat.OSChatTypeCodeToString[(uint)pCMsg.Type], 
                OSChatSource = pSource,
                OSChatChannel = pCMsg.Channel,
                OSChatFromAgentName = senderName,
                OSChatFromAgentId = pCMsg.SenderUUID.ToString(),
                OSChatMessage = pCMsg.Message,
                OSChatPosition = new float[] { pCMsg.Position.X, pCMsg.Position.Y, pCMsg.Position.Z },
                OSChatToAgentId = pCMsg.Destination.ToString(),

                OSChatImSessionId = pCMsg.Sender.SessionId.ToString(),

                OSChatTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                OSChatUnhandled = false 
            });
            BMessage bim = new BMessage(BMessageOps.UpdatePropertiesReq);
            SpaceServerEnviron eenv = _RContext.getSpaceServer<SpaceServerEnviron>();
            if (eenv.ChatDialogId != null) {
                bim.IId = eenv.ChatDialogId;
                _connection.Send(bim, abilProps);
            }
        }
        private void SendInstantMessage(GridInstantMessage pGMsg, bool pUnhandled) {
            _RContext.log.Debug("{0} SendInstantMessage. from={1} to={2} message={3}", _logHeader, pGMsg.fromAgentID, pGMsg.toAgentID, pGMsg.message);
            AbilityList abilProps = new AbilityList();
            abilProps.Add(new AbOSChat() {
                OSChatType = "Say",
                OSChatSource = "Grid",
                OSChatChannel = 0,
                OSChatFromAgentName = pGMsg.fromAgentName,
                OSChatFromAgentId = pGMsg.fromAgentID.ToString(),
                OSChatMessage = pGMsg.message,
                OSChatPosition = new float[] { pGMsg.Position.X, pGMsg.Position.Y, pGMsg.Position.Z },
                OSChatToAgentId = pGMsg.toAgentID.ToString(),

                OSChatDialog = pGMsg.dialog,
                OSChatFromGroup = pGMsg.fromGroup,
                OSChatImSessionId = pGMsg.imSessionID.ToString(),
                OSChatOffline = pGMsg.offline,
                OSChatParentEstateId = pGMsg.ParentEstateID.ToString(),
                OSChatRegionId = pGMsg.RegionID.ToString(),
                OSChatTimestamp = pGMsg.timestamp,
                OSChatUnhandled = pUnhandled 
            });
            BMessage bim = new BMessage(BMessageOps.UpdatePropertiesReq);
            SpaceServerEnviron eenv = _RContext.getSpaceServer<SpaceServerEnviron>();
            if (eenv is not null && eenv.ChatDialogId != null) {
                bim.IId = eenv.ChatDialogId;
                _connection.Send(bim, abilProps);
            }
        }
        
    }
}
