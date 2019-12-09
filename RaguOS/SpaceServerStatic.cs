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

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;
using BasilType = org.herbal3d.basil.protocol.BasilType;
using org.herbal3d.cs.CommonEntitiesUtil;
using org.herbal3d.Tiles;

using org.herbal3d.Loden;
using org.herbal3d.OSAuth;

using Google.Protobuf;

using OMV = OpenMetaverse;
using System.Net;

namespace org.herbal3d.Ragu {

    public class SpaceServerStaticLayer : SpaceServerLayer {

        // Initial SpaceServerStatic invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        // Note: this canceller is for the overall layer.
        public SpaceServerStaticLayer(RaguContext pContext, CancellationTokenSource pCanceller)
                        : base(pContext, pCanceller, "SpaceServerStatic") {
        }

        // Return an instance of me
        protected override SpaceServerLayer InstanceFactory(RaguContext pContext,
                        CancellationTokenSource pCanceller, HTransport.BasilConnection pConnection) {
            return new SpaceServerStatic(pContext, pCanceller, pConnection);
        }
    }

    public class SpaceServerStatic : SpaceServerLayer {
        // Handle to the LOD'ized region assets
        LodenRegion _lodenRegion;
        // Asset server for the loden assets
        RaguAssetService _assetService;

        // Creation of an instance for a specific client.
        // Note: this canceller is for the individual session.
        public SpaceServerStatic(RaguContext pContext, CancellationTokenSource pCanceller,
                                        HTransport.BasilConnection pBasilConnection) 
                        : base(pContext, pCanceller, "SpaceServerStatic", pBasilConnection) {

            // This assignment directs the space server message calls to this ISpaceServer instance.
            _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = this;

            // The thing to call to make requests to the Basil server
            _client = new HTransport.BasilClient(pBasilConnection);

            // Our handle to the LOD'ized region assets
            _lodenRegion = _context.scene.RequestModuleInterface<LodenRegion>();
            _assetService = RaguAssetService.Instance;
        }

        // This one client has disconnected
        public override void Shutdown() {
            _canceller.Cancel();
            if (_client != null) {
                _client = null;
            }
            if (_clientConnection != null) {
                _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = null;
                _clientConnection = null;
            }
        }

        // Request from Basil to open a SpaceServer session
        public override SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            _context.log.DebugFormat("{0} OpenSession.", _logHeader);

            var ret = new SpaceServer.OpenSessionResp();

            // DEBUG DEBUG
            _context.log.DebugFormat("{0} OpenSession Features:", _logHeader);
            foreach (var kvp in pReq.Features) {
                _context.log.DebugFormat("{0}     {1}: {2}", _logHeader, kvp.Key, kvp.Value);
            };
            // END DEBUG DEBUG

            // Check if this is a test connection. We cannot handle those.
            // Respond with an error message.
            if (CheckIfTestConnection(pReq, ref ret)) {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Test session not acceptable"
                };
                return ret;
            }

            // Check for an authorized connection
            if (base.ValidateUserAuth(pReq.Auth, out OSAuthModule auther, out OSAuthToken userAuth)) {

                // Use common processing routine for all the SpaceServer layers
                ret = base.HandleOpenSession(pReq, auther);

                // Start sending stuff to our new Basil friend.
                HandleBasilConnection();
            }
            else {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Not authorized"
                };
            }

            return ret;
        }

        // Request from Basil to close the SpaceServer session
        public override SpaceServer.CloseSessionResp CloseSession(SpaceServer.CloseSessionReq pReq) {
            throw new NotImplementedException();
        }

        // Request from Basil to move the camera.
        public override SpaceServer.CameraViewResp CameraView(SpaceServer.CameraViewReq pReq) {
            throw new NotImplementedException();
        }

        // Received an OpenSession from a Basil client.
        // Send the fellow some content.
        private Task HandleBasilConnection() {
            return Task.Run(() => {
                try {
                    _context.log.DebugFormat("{0} HandleBasilConnection", _logHeader);

                    // Get region tile definition
                    string regionSpecURL = _assetService.CreateAccessURL(_lodenRegion.RegionTopLevelSpecURL);

                    // Get the top level description of the region
                    TileSet regionSpec;
                    try {
                        using (WebClient wClient = new WebClient()) {
                            wClient.UseDefaultCredentials = false;
                            wClient.Headers.Add("Authorization", _assetService.AccessToken.Token);
                            string specString = wClient.DownloadString(regionSpecURL);
                            regionSpec = TileSet.FromString(specString);
                        }
                    }
                    catch (Exception e) {
                        _context.log.ErrorFormat("{0} HandleBasilConnection: Failure reading region spec '{1}': {2}",
                                        _logHeader, regionSpecURL, e);
                        // There is nothing more we can do
                        return;
                    }
                    if (regionSpec == null) {
                        _context.log.ErrorFormat("{0} HandleBasilConnection: Could not read regionSpec", _logHeader);
                        return;
                    }

                    BasilType.AccessAuthorization auth = base.CreateAccessAuthorization(base.ClientAuth);

                    // Gather all the URIs for the region's contents
                    List<string> regionURIs = (regionSpec.root.content.uris ?? new string[0]).ToList();
                    if (!string.IsNullOrEmpty(regionSpec.root.content.uri)) {
                        regionURIs.Add(regionSpec.root.content.uri);
                    }
                    // Tell the Basil server to load all of the region's contents
                    regionURIs.ForEach(async uri => {
                        BasilType.AaBoundingBox aabb = null;
                        BasilType.AssetInformation assetInfo = _assetService.CreateAssetInformation(uri);

                        _context.log.DebugFormat("{0} HandleBasilConnection: regionAssetURL=", _logHeader, assetInfo.DisplayInfo.Asset["url"]);
                        var objectResp = await _client.IdentifyDisplayableObjectAsync(auth, assetInfo, aabb);

                        BasilType.InstancePositionInfo instancePositionInfo = new BasilType.InstancePositionInfo() {
                            Pos = new BasilType.CoordPosition() {
                                Pos = new BasilType.Vector3() {
                                    // X = 100, Y = 101, Z = 102
                                    X = 0,
                                    Y = 0,
                                    Z = 0
                                },
                                Rot = new BasilType.Quaternion() {
                                    X = 0,
                                    Y = 0,
                                    Z = 0,
                                    W = 1
                                },
                                PosRef = BasilType.CoordSystem.Wgs86,
                                RotRef = BasilType.RotationSystem.Worldr
                            }
                        };
                        var instanceResp = await _client.CreateObjectInstanceAsync(auth, objectResp.ObjectId, instancePositionInfo);
                    });
                }
                catch (Exception e) {
                    _context.log.ErrorFormat("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
                }
            }, _canceller.Token);
        }
    }
}
