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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using org.herbal3d.OSAuth;
using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.Tiles;
using org.herbal3d.cs.CommonUtil;

using org.herbal3d.Loden;
using org.herbal3d.cs.CommonEntities;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages after we're connected up
    class ProcessStaticIncomingMessages : IncomingMessageProcessor {
        SpaceServerStatic _ssContext;
        public ProcessStaticIncomingMessages(SpaceServerStatic pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            switch (pMsg.Op) {
                case (uint)BMessageOps.UpdatePropertiesReq:
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Unsupported operation on SpaceServer" + _ssContext.LayerType;
                    pConnection.Send(resp);
                    break;
            }
        }
    }

    public class SpaceServerStatic : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerStatic]";

        public static readonly string SpaceServerType = "Static";

        public SpaceServerStatic(RaguContext pContext,
                            CancellationTokenSource pCanceller,
                            WaitingInfo pWaitingInfo,
                            BasilConnection pConnection,
                            BMessage pMsg) 
                        : base(pContext, pCanceller, pConnection) {

            LayerType = SpaceServerType;

            pConnection.SetOpProcessor(new ProcessStaticIncomingMessages(this), ProcessConnectionStateChange);
        }

        public override void Start() {
            // Send the fellow some content.
            try {

                // Get region tile definition
                LodenRegion lodenRegion = _RContext.scene.RequestModuleInterface<LodenRegion>();
                string regionSpecURL = RaguAssetService.Instance.CreateAccessURL(lodenRegion.RegionTopLevelSpecURL);

                // Get the top level description of the region
                TileSet regionSpec;
                try {
                    using (WebClient wClient = new WebClient()) {
                        wClient.UseDefaultCredentials = false;
                        wClient.Headers.Add("Authorization", RaguAssetService.Instance.AccessToken.Token);
                        string specString = wClient.DownloadString(regionSpecURL);
                        regionSpec = TileSet.FromString(specString);
                    }
                }
                catch (Exception e) {
                    _RContext.log.Error("{0} HandleBasilConnection: Failure reading region spec '{1}': {2}",
                                    _logHeader, regionSpecURL, e);
                    // There is nothing more we can do
                    return;
                }
                if (regionSpec == null) {
                    _RContext.log.Error("{0} HandleBasilConnection: Could not read regionSpec", _logHeader);
                    return;
                }

                // Gather all the URIs for the region's contents
                List<string> regionURIs = new List<string>();
                if (regionSpec.root.content.uris != null) {
                    regionURIs.AddRange(regionSpec.root.content.uris.Select(uri => {
                        return RaguAssetService.Instance.CreateAccessURL(uri);
                    }));
                };
                if (!string.IsNullOrEmpty(regionSpec.root.content.uri)) {
                    regionURIs.Add(RaguAssetService.Instance.CreateAccessURL(regionSpec.root.content.uri));
                };

                // Tell the Basil server to load all of the region's contents
                regionURIs.ForEach(async uri => {
                    AbilityList props = new AbilityList();
                    props.Add(
                        new AbAssembly() {
                            AssetURL = uri,
                            AssetLoader = "gltf",
                            AssetAuth = RaguAssetService.Instance.AccessToken.Token,
                        }
                    );

                    props.Add(new AbPlacement() {
                        WorldPos = BCoord.ToPlanetCoord(_RContext.frameOfRef, OMV.Vector3.Zero),
                        WorldRot = BCoord.ToPlanetRot(_RContext.frameOfRef, OMV.Quaternion.Identity)
                    } );
                    BMessage resp = await _connection.CreateItem(props);
                    string instanceId = AbBItem.GetId(resp);

                    _RContext.log.Debug("{0} HandleBasilConnection: Created instance {1}",
                                    _logHeader, instanceId);
                });
            }
            catch (Exception e) {
                _RContext.log.Error("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }

        public override void Stop() {
            base.Stop();
        }

        // Send a MakeConnection for connecting to a SpaceServer of this type.
        public static void MakeConnectionToSpaceServer(BasilConnection pConn,
                                                    OMV.UUID pAgentUUID,
                                                    RaguContext pRContext) {

            // The authentication token that the client will send with the OpenSession
            // OSAuthToken incomingAuth = new OSAuthToken();
            OSAuthToken incomingAuth = OSAuthToken.SimpleToken();

            // Information that will be used to process the incoming OpenSession
            var wInfo = new WaitingInfo() {
                agentUUID = pAgentUUID,
                incomingAuth = incomingAuth,
                spaceServerType = SpaceServerStatic.SpaceServerType,
                createSpaceServer = (pC, pW, pConn, pMsgX, pCan) => {
                    return new SpaceServerStatic(pC, pCan, pW, pConn, pMsgX);
                }
            };
            pRContext.RememberWaitingForOpenSession(wInfo);

            // Create the MakeConnection and send it
            var pBlock = pRContext.Listener.ParamsForMakeConnection(pRContext.HostnameForExternalAccess, incomingAuth);
            _ = pConn.MakeConnection(pBlock);
        }

    }
}
