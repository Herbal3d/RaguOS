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

using org.herbal3d.Loden;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages when we're waiting for the OpenSession.
    class ProcessStaticIncomingMessages : IncomingMessageProcessor {
        SpaceServerStatic _ssContext;
        public ProcessStaticIncomingMessages(SpaceServerStatic pContext) : base(pContext) {
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

    public class SpaceServerStatic : SpaceServerBase {

        private static readonly string _logHeader = "[SpaceServerStatic]";

        public static readonly string StaticLayerType = "Static";

        // Fuction called to start up the service listener.
        // THis starts listening for network connections and creates instances of the SpaceServer
        //     for each of the incoming connections
        public static SpaceServerListener SpaceServerStaticService(RaguContext pRContext, CancellationTokenSource pCanceller) {
            return new SpaceServerListener(
                transportParams: new BTransportParams[] {
                    new BTransportWSParams() {
                        preferred       = true,
                        isSecure        = pRContext.parms.SpaceServerStatic_IsSecure,
                        port            = pRContext.parms.SpaceServerStatic_WSConnectionPort,
                        certificate     = pRContext.parms.SpaceServerStatic_WSCertificate,
                        disableNaglesAlgorithm = pRContext.parms.SpaceServerStatic_DisableNaglesAlgorithm
                    }
                },
                layer: SpaceServerStatic.StaticLayerType,
                canceller: pCanceller,
                logger: pRContext.log,
                // This method is called when the listener receives a connection but before any
                //     messsages have been exchanged.
                processor: (pTransport, pCancellerP) => {
                    // pRContext.log.Debug("SpaceServerStaticService: creating new SpaceServerStatic");
                    return new SpaceServerStatic(pRContext, pCancellerP, pTransport);
                }
            );
        }

        public SpaceServerStatic(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport, RContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, RContext.log);
            _connection.SetOpProcessor(new ProcessMessagesOpenConnection(this));
            _connection.Start();
        }

        protected override void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken loginAuth) {

            // We also have a full command processor
            pConnection.SetOpProcessor(new ProcessStaticIncomingMessages(this));

            // Send the static region information to the user
            Task.Run(async () => {
                await StartConnection(pConnection, loginAuth);
            });
        }

        // Received an OpenSession from a Basil client.
        // Send the fellow some content.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task StartConnection(BasilConnection pConnection, OSAuthToken pUserAuth) {
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            try {

                // Get region tile definition
                LodenRegion lodenRegion = RContext.scene.RequestModuleInterface<LodenRegion>();
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
                    RContext.log.Error("{0} HandleBasilConnection: Failure reading region spec '{1}': {2}",
                                    _logHeader, regionSpecURL, e);
                    // There is nothing more we can do
                    return;
                }
                if (regionSpec == null) {
                    RContext.log.Error("{0} HandleBasilConnection: Could not read regionSpec", _logHeader);
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
                        new AbilityAssembly() {
                            AssetURL = uri,
                            AssetAuth = RaguAssetService.Instance.AccessToken.Token,
                            // AssetLoader = "GLTF"
                            // AssetServiceType = "RAGU",
                            // DisplayableType = "meshset",
                        }
                    );
                    props.Add(new AbilityInstance() {
                            RefItem = "SELF",
                            WorldPos = new double[] { 0, 0, 0 }
                        }
                    );
                    BMessage resp = await pConnection.CreateItem(props);
                    string instanceId = AbilityBItem.GetId(resp);

                    RContext.log.Debug("{0} HandleBasilConnection: Created instance {1}",
                                    _logHeader, instanceId);
                });
            }
            catch (Exception e) {
                RContext.log.Error("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }
    }
}
