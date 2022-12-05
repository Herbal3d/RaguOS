// Copyright (c) 2022 Robert Adams
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
using org.herbal3d.cs.CommonEntities;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages after we're connected up
    class ProcessActorsIncomingMessages : IncomingMessageProcessor {
        SpaceServerActors _ssContext;
        public ProcessActorsIncomingMessages(SpaceServerActors pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            // this._ssContext.RContext.log.Debug("ProcessActorsIncomingMessages.Process: {0}", pMsg);
            switch (pMsg.Op) {
                case (uint)BMessageOps.UpdatePropertiesReq:
                    if (_ssContext.TryGetPresenceById(pMsg.IId, out PresenceInfo pi)) {
                        // Allow operations only on my avatar
                        if (pi.scenePresence.UUID == _ssContext.AgentUUID) {
                            foreach (var kvp in pMsg.IProps) {
                                // this._ssContext.RContext.log.Debug("ProcessActorsIncomingMessages.Process: key={0}", kvp.Key);
                                switch (kvp.Key) {
                                    case AbOSAvaUpdate.MoveToProp:
                                        _ssContext.AvatarMoveToAction(pi, pMsg);
                                        break;
                                    case AbOSAvaUpdate.ControlFlagProp:
                                        _ssContext.AvatarAction(pi, pMsg);
                                        break;
                                    default:
                                        break;
                                }
                            }
                            pConnection.SendResponse(pMsg);
                        }
                        else {
                            BMessage notUsResp = BasilConnection.MakeResponse(pMsg);
                            notUsResp.Exception = "Unauthorized: attempt to act on non-owned scene presence";
                            pConnection.Send(notUsResp);
                        }
                    }
                    else {
                        BMessage notUsResp = BasilConnection.MakeResponse(pMsg);
                        notUsResp.Exception = "Unknown Id";
                        pConnection.Send(notUsResp);
                    }
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Unsupported operation on SpaceServer" + _ssContext.LayerType;
                    pConnection.Send(resp);
                    break;
            }
        }
    }

    public class SpaceServerActors : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerActors]";

        public static readonly string SpaceServerType = "Actors";

        public SpaceServerActors(RaguContext pContext,
                                CancellationTokenSource pCanceller,
                                WaitingInfo pWaitingInfo,
                                BasilConnection pConnection,
                                BMessage pMsg) 
                        : base(pContext, pCanceller, pConnection) {
            LayerType = SpaceServerType;

            // Remember the UUID of the logged-in/controlling actor
            AgentUUID = pWaitingInfo.agentUUID;

            // Set up processor for received messages.
            // Mostly movement operations for my avatar
            pConnection.SetOpProcessor(new ProcessActorsIncomingMessages(this), ProcessConnectionStateChange);
        }

        public override void Start() {
            AddEventSubscriptions();
            AddExistingPresences();
        }

        public override void Shutdown() {
            RemoveEventSubscriptions();
            base.Shutdown();
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
                spaceServerType = SpaceServerActors.SpaceServerType,
                createSpaceServer = (pC, pW, pConn, pMsgX, pCan) => {
                    return new SpaceServerActors(pC, pCan, pW, pConn, pMsgX);
                }
            };
            pRContext.RememberWaitingForOpenSession(wInfo);

            // Create the MakeConnection and send it
            var pBlock = pRContext.Listener.ParamsForMakeConnection(pRContext.HostnameForExternalAccess, incomingAuth);
            _ = pConn.MakeConnection(pBlock);
        }

        private void AddEventSubscriptions() {
            // When new client (child or root) is added to scene, before OnClientLogin
            // RContext.scene.EventManager.OnNewClient         += Event_OnNewClient;
            // When client is added on login.
            // RContext.scene.EventManager.OnClientLogin       += Event_OnClientLogin;
            // New presence is added to scene. Child, root, and NPC. See Scene.AddNewAgent()
            _RContext.scene.EventManager.OnNewPresence       += Event_OnNewPresence;
            _RContext.scene.EventManager.OnRemovePresence    += Event_OnRemovePresence;
            // update to client position (either this or 'significant')
            _RContext.scene.EventManager.OnClientMovement    += Event_OnClientMovement;
            // "significant" update to client position
            _RContext.scene.EventManager.OnSignificantClientMovement += Event_OnSignificantClientMovement;
            // Gets called for most position/camera/action updates. Seems to be once a second.
            // RContext.scene.EventManager.OnScenePresenceUpdated      += Event_OnScenePresenceUpdated;

            // RContext.scene.EventManager.OnIncomingSceneObject += Event_OnIncomingSceneObject;
            // RContext.scene.EventManager.OnObjectAddedToScene += Event_OnObjectAddedToScene;
            // RContext.scene.EventManager.OnDeRezRequested += Event_OnDeRezRequested;
            // RContext.scene.EventManager.OnObjectBeingRemovedFromScene += Event_OnObjectBeingRemovedFromScene;
            // RContext.scene.EventManager.OnObjectAddedToPhysicalScene += Event_OnObjectAddedToPhysicalScene;
            // RContext.scene.EventManager.OnObjectRemovedToPhysicalScene += Event_OnObjectRemovedToPhysicalScene;

            //When scene is shutting down
            // RContext.scene.EventManager.OnShutdown  += Event_OnShutdown;
        }
        private void RemoveEventSubscriptions() {
            // RContext.scene.EventManager.OnNewClient         -= Event_OnNewClient;
            // RContext.scene.EventManager.OnClientLogin       -= Event_OnClientLogin;
            _RContext.scene.EventManager.OnNewPresence       -= Event_OnNewPresence;
            _RContext.scene.EventManager.OnRemovePresence    -= Event_OnRemovePresence;
            // update to client position (either this or 'significant')
            _RContext.scene.EventManager.OnClientMovement    -= Event_OnClientMovement;
            // "significant" update to client position
            _RContext.scene.EventManager.OnSignificantClientMovement -= Event_OnSignificantClientMovement;
            // Gets called for most position/camera/action updates
            // RContext.scene.EventManager.OnScenePresenceUpdated      -= Event_OnScenePresenceUpdated;

            // RContext.scene.EventManager.OnShutdown  -= Event_OnShutdown;
        }
        private void AddExistingPresences() {
            _RContext.scene.GetScenePresences().ForEach(pres => {
                PresenceInfo pi = new PresenceInfo(pres, _connection, this, _RContext);
                AddPresence(pi);
                pi.AddAppearanceInstance();
            });
        }

        private void Event_OnNewPresence(ScenePresence pPresence) {
            _RContext.log.Debug("{0} Event_OnNewPresence", _logHeader);
            PresenceInfo pi;
            if (TryFindPresence(pPresence.UUID, out pi)) {
                _RContext.log.Error("{0} Event_OnNewPresence: two events for the same presence", _logHeader);
            }
            else {
                pi = new PresenceInfo(pPresence, _connection, this, _RContext);
                AddPresence(pi);
                pi.AddAppearanceInstance();
            }
        }
        private void Event_OnRemovePresence(OMV.UUID pPresenceUUID) {
            _RContext.log.Debug("{0} Event_OnRemovePresence. presenceUUID={1}", _logHeader, pPresenceUUID);
            if (TryFindPresence(pPresenceUUID, out PresenceInfo pi)) {
                pi.RemoveAppearanceInstance();
                RemovePresence(pi);
                _RContext.log.Debug("{0} Event_OnRemovePresence. removed presenceUUID={1}", _logHeader, pPresenceUUID);
            }
        }
        private void Event_OnClientMovement(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnClientMovement", _logHeader);
            if (TryFindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition(false);
            }
            else {
                _RContext.log.Error("{0} Event_OnClientMovement: did not find presence", _logHeader);
            }
        }
        private void Event_OnSignificantClientMovement(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnSignificantClientMovement", _logHeader);
            if (TryFindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition(false);
            }
            else {
                _RContext.log.Error("{0} Event_OnSignificantClientMovement. Did not find presence", _logHeader);
            }
        }
        private void Event_OnScenePresenceUpdated(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnScenePresenceUpdated", _logHeader);
            if (TryFindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition(true);
            }
        }
        
        // Some of the parameters for Abilities are an array of doubles.
        // This converts a property out of a ParamBlock into that array of doubles.
        // THis looks for an array of doubles or an array of floats.
        //     Code is also here for checking if passed value is a string but doesn't yet try to parse.
        private double[] UnpackArrayFromProp(string pPropName, ParamBlock pProps, double[] pDefault) {
            double[] ret = pDefault;
            if (pProps.HasParam(pPropName)) {
                object valObj = pProps.GetObjectValue(pPropName);
                if (valObj != null && valObj.GetType().IsArray) {
                    if (valObj.GetType().GetElementType() == typeof(double)) {
                        ret = (double[])valObj;
                    }
                    // Not sure if this is necessary -- JSON should all be received as doubles
                    if (valObj.GetType().GetElementType() == typeof(float)) {
                        var valArr = (float[])valObj;
                        ret = new double[valArr.Length];
                        for (int i = 0; i < valArr.Length; i++) {
                            ret[i] = (double)valArr[i];
                        }
                    }
                }
                else {
                    if (valObj.GetType() == typeof(string)) {
                        // If it's a string, we could parse a JSON value.
                        // In this case is shouldn't be a string but that might be added for futures.
                        this._RContext.log.Debug("{0}: PackQuaternionFromProp: parm value is string: {1}", _logHeader, valObj);
                        ret = new double[] { 0, 0, 0, 1 };
                    }
                    else {
                        this._RContext.log.Debug("{0}: PackQuaternionFromProp: parm value is not parseable: {1}", _logHeader, valObj.GetType());
                    }
                }
            }
            return ret;
        }

        // Received update to 'moveAction' property
        // The client tells us what action to do and this code does its best to
        //    fake out OpenSimulator to perform that action. Most actions go through
        //    the "AgentUpdate" action as, in the LL protocol, that packet is sent
        //    often to update the avatar's position and view.
        // Other actions use the "move to" or "auto pilot" feature to move the avatar.
        private AgentUpdateArgs _agentUpdateArgs = new AgentUpdateArgs();
        private static float degreesToRads = (float)(Math.PI / 180.0);
        private static float turnRads = 10 * degreesToRads;
        private static double[] doubleDefault = new double[] { 0, 0, 0, 1 };
        public void AvatarAction(PresenceInfo pPi, BMessage pMsg) {
            ParamBlock props = new ParamBlock(pMsg.IProps);
            RaguAvatar clientAPI = pPi.scenePresence.ControllingClient as RaguAvatar;

            // This is called with the update args from last time
            // No known modules subscribe to this event
            clientAPI.FireOnPreAgentUpdate(clientAPI, this._agentUpdateArgs);

            uint cFlags = props.P<uint>(AbOSAvaUpdate.ControlFlagProp);
            float camFar = props.P<float>(AbOSAvaUpdate.FarProp);
            double[] dBodyRot = this.UnpackArrayFromProp(AbOSAvaUpdate.BodyRotProp, props, doubleDefault);
            double[] dHeadRot = this.UnpackArrayFromProp(AbOSAvaUpdate.HeadRotProp, props, doubleDefault);
            OMV.Quaternion bodyRot = BCoord.FromPlanetRot(_RContext.frameOfRef, dBodyRot);
            OMV.Quaternion headRot = BCoord.FromPlanetRot(_RContext.frameOfRef, dBodyRot);

            uint controlFlags = (uint)OMV.AgentManager.ControlFlags.NONE;

            if ((cFlags & (uint)AbOSAvaUpdate.OSAvaUpdateMoveAction.WalkForward) != 0) {
                controlFlags |= (uint)OMV.AgentManager.ControlFlags.AGENT_CONTROL_AT_POS;
            }
            if ((cFlags & (uint)AbOSAvaUpdate.OSAvaUpdateMoveAction.WalkBackward) != 0) {
                controlFlags |= (uint)OMV.AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG;
            }
            if ((cFlags & (uint)AbOSAvaUpdate.OSAvaUpdateMoveAction.TurnLeft) != 0) {
                controlFlags |= (uint)OMV.AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_POS;
                var rotate = OMV.Quaternion.CreateFromAxisAngle(0, 0, 1, turnRads);
                // this.RContext.log.Debug("{0}: AvatarAction.TurnLeft: rot={1}, bRot={2}, after={3}", _logHeader, rotate, bodyRot, bodyRot * rotate);
                bodyRot *= rotate;
                headRot = bodyRot;
            }
            if ((cFlags & (uint)AbOSAvaUpdate.OSAvaUpdateMoveAction.TurnRight) != 0) {
                controlFlags |= (uint)OMV.AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_POS;
                var rotate = OMV.Quaternion.CreateFromAxisAngle(0, 0, 1, -turnRads);
                // this.RContext.log.Debug("{0}: AvatarAction.TurnRight: rot={1}, bRot={2}, after={3}", _logHeader, rotate, bodyRot, bodyRot * rotate);
                bodyRot *= rotate;
                headRot = bodyRot;
            }

            // Build AgentUpdateArgs
            AgentUpdateArgs updateArgs = new AgentUpdateArgs() {
                BodyRotation = bodyRot,
                HeadRotation = headRot,
                ControlFlags = controlFlags,
                Far = camFar,
                Flags = 0,      // None, HideTitle, CliAutoPilot, MuteCollisions (OMV.AgentUpdateFlags)
                State = 0       // None, Typing, Editing (OMV.AgentState)
            };

            // === IMPLEMENTATION NOTES ================================
            // LLClientView remembers the last update packet and passes it on if there are changes
            /* code from LLClientView
            m_thisAgentUpdateArgs.BodyRotation = x.BodyRotation;
            m_thisAgentUpdateArgs.ControlFlags = x.ControlFlags;
            m_thisAgentUpdateArgs.Far = x.Far;
            m_thisAgentUpdateArgs.Flags = x.Flags;
            m_thisAgentUpdateArgs.HeadRotation = x.HeadRotation;
            m_thisAgentUpdateArgs.State = x.State;

            m_thisAgentUpdateArgs.NeedsCameraCollision = !camera;

            OnAgentUpdate?.Invoke(this, m_thisAgentUpdateArgs);
            */
            // ControlFlags are OpenMetaverse.AgentManager.ControlFlags
            //    The mouse info is passed through to the scripts
            // Needs ClientAgentPosition if UseClientAgentPosition is true
            // ScenePresence.HandleAgentUpdate does not use Flags
            // Flags contains HideTitle, CliAutoPilot, MuteCollisions
            //      OpenSim.Region.Framework.Scenes.AgentUpdateFlags
            // State is OpenSimulator.AgentState which includes None, Typing, Editing
            //
            // If UseClientAgentPosition is 'true', ScenePresence.Position <= agentData.ClientAgentPosition
            //
            // Some of the states are very independent:
            //  AGENT_CONTROL_STAND_UP
            //  AGENT_CONTROL_SIT_ON_GROUND
            // === END IMPLEMENTATION NOTES ================================

            // This usually just calls ScenePresence.HandleAgentUpdate
            clientAPI.FireOnAgentUpdate(clientAPI, updateArgs);

            this._agentUpdateArgs = updateArgs;

        }

        // Received update to 'moveTo' property
        public void AvatarMoveToAction(PresenceInfo pPi, BMessage pBMsg) {
            ParamBlock props = new ParamBlock(pBMsg.IProps);
            RaguAvatar clientAPI = pPi.scenePresence.ControllingClient as RaguAvatar;

            double[] dTargetPos = this.UnpackArrayFromProp(AbOSAvaUpdate.MoveToProp, props, doubleDefault);
            OMV.Vector3 targetPos = BCoord.FromPlanetCoord(_RContext.frameOfRef, dTargetPos);

            clientAPI.FireOnAutoPilotGo(clientAPI, targetPos, false, true);
        }

        private List<PresenceInfo> _presences = new List<PresenceInfo>();

        public bool TryGetPresenceById(string pBItemId, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                var pi = _presences.Find(p => p.InstanceId == pBItemId);
                ret = pi != null;
                pFound = pi;
            }
            return ret;
        }

        // Find a presence based on it's ScenePresence instance
        private bool TryFindPresence(ScenePresence pScenePresence, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                try {
                    pFound = _presences.Find(p => p.scenePresence == pScenePresence);
                    ret = true;
                }
                catch {
                    pFound = null;
                }
            }
            return ret;
        }
        // Find a presence using it's UUID
        private bool TryFindPresence(OMV.UUID pPresenceId, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                try {
                    pFound = _presences.Where(p => p.scenePresence.UUID == pPresenceId).First();
                    ret = true;
                }
                catch {
                    pFound = null;
                }
            }
            return ret;
        }
        private void AddPresence(PresenceInfo pPI) {
            lock (_presences) {
                _presences.Add(pPI);
            }
        }
        private void RemovePresence(PresenceInfo pPI) {
            lock (_presences) {
                _presences.Remove(pPI);
            }
        }
        // The connection is going down or something. Remove all presences from the scene.
        private void DeleteAllPresences() {
            lock (_presences) {
                while (_presences.Count > 0) {
                    PresenceInfo pres = _presences.First();
                    _presences.Remove(pres);
                    pres.RemoveAppearanceInstance();
                }
            }
        }
    }

    // Local class for a presence and the operations we do on the Basil display.
    public class PresenceInfo {
        private static readonly string _logHeader = "[PresenceInfo]";

        public ScenePresence scenePresence;
        private RaguContext _context;
        private SpaceServerActors _spaceServer;
        private BasilConnection _connection;

        // BItem.ID of the created 
        public string InstanceId { get; private set; }

        public PresenceInfo(ScenePresence pPresence, BasilConnection pConnection, SpaceServerActors pSpaceServer, RaguContext pContext) {
            scenePresence = pPresence;
            _context = pContext;
            _connection = pConnection;
            _spaceServer = pSpaceServer;
        }
        public async void UpdatePosition(bool pForce = false) {
            if (InstanceId != null) {
                AbPlacement coordParams;
                if (pForce) {
                    coordParams = new AbPlacement() {
                        WorldPos = BCoord.ToPlanetCoord(_context.frameOfRef, scenePresence.AbsolutePosition),
                        WorldRot = BCoord.ToPlanetRot(_context.frameOfRef, scenePresence.GetWorldRotation())
                    };
                }
                else {
                    coordParams = new AbPlacement() {
                        ToWorldPos = BCoord.ToPlanetCoord(_context.frameOfRef, scenePresence.AbsolutePosition),
                        ToWorldRot = BCoord.ToPlanetRot(_context.frameOfRef, scenePresence.GetWorldRotation())
                    };
                }
                await _connection.UpdateProperties(InstanceId, coordParams);
                // _context.log.Debug("{0} UpdatePosition: p={1}, r={2}",
                //             _logHeader, presence.AbsolutePosition, presence.Rotation);
            }
        }
        public void UpdateAppearance() {
            if (InstanceId != null) {
            }
        }
        public void AddAppearanceInstance() {
            _ = Task.Run(async () => {
                try {
                    // TODO: use avatar appearance baking code to build GLTF version of avatar
                    // For the moment, use a canned, static mesh
                    string tempAppearanceURL = "https://files.misterblue.com/BasilTest/gltf/Duck/glTF/Duck.gltf";

                    AbilityList abilProps = new AbilityList();
                    // Avatar has appearance
                    abilProps.Add(
                        new AbAssembly() {
                            AssetURL = tempAppearanceURL,
                            AssetLoader = "gltf",
                            AssetAuth = RaguAssetService.Instance.AccessToken.Token,
                        }
                    );
                    // Avatar appears in the world
                    abilProps.Add(
                        new AbPlacement() {
                            ToWorldPos = BCoord.ToPlanetCoord(_context.frameOfRef, scenePresence.AbsolutePosition),
                            ToWorldRot = BCoord.ToPlanetRot(_context.frameOfRef, scenePresence.GetWorldRotation())
                        }
                    );
                    if (scenePresence.UUID == _spaceServer.AgentUUID) {
                        // If the main client avatar, set for user controlling its actions
                        abilProps.Add( new AbOSAvaUpdate() );
                        // Avatar has control of the camera
                        abilProps.Add(
                            new AbOSCamera() {
                                OSCameraMode = AbOSCamera.OSCameraModes.Third,
                                OSCameraDisplacement = new double[] { 0, 7.0, -3.0 }
                            }
                        );
                    };

                    BMessage resp = await _connection.CreateItem(abilProps);
                    if (resp.Exception != null) {
                        _context.log.Error("{0} AddAppearanceInstance: error creating avatar: {1}: {2}",
                                    _logHeader, scenePresence.UUID, resp.Exception);
                    }
                    else {
                        InstanceId = AbBItem.GetId(resp);
                    }
                }
                catch (Exception e) {
                    _context.log.Error("{0} AddAppearanceInstance: exception adding appearance: {1}",
                                    _logHeader, e);
                };
            });
        }

        public void RemoveAppearanceInstance() {
            Task.Run( async () => {
                if (InstanceId != null) {
                    await _connection.DeleteItem(InstanceId);
                    InstanceId = null;
                };
            });
        }
    }
}
