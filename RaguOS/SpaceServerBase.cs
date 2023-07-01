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

        // The simulator wide Ragu context information
        public readonly RaguContext _RContext;

        // Each SpaceServer instance is associated with a region
        public readonly RaguRegion _RRegion;

        // The UUID of the logged in agent we're working as
        public OMV.UUID AgentUUID;
        public OMV.UUID SessionUUID;

        protected CancellationTokenSource _cancellerSource;
        protected BasilConnection _connection;
        // protected BTransport _transport;
        // protected BProtocol _protocol;

        public SpaceServerBase(RaguContext pContext, RaguRegion pRegion, CancellationTokenSource pCanceller, BasilConnection pConnection) {
            _RContext = pContext;
            _RRegion = pRegion;
            _cancellerSource = pCanceller;
            _connection = pConnection;
            _RContext.addSpaceServer(this);
        }

        // Called after connection is made and things are ready for the SpaceServer to start talking
        // Run on its own thread so this can take as long as it wants.
        public abstract void Start();

        // Called when going down
        public virtual void Stop() {
            _RContext.log.Info("{0} Stop for SpaceServer {1} for user {2}", _logHeader, LayerType, AgentUUID);
            this.ShutdownConnection();
        }

        // When a MakeConnection is sent, the information on what to do with the incoming
        //    OpenSession is received is saved in a WaitingInfo block.
        // Create and return a block of information for the creation of this SpaceServer
        // public abstract WaitingInfo CreateWaitingInfo(OMV.UUID pAgentUUID, OSAuthToken pIncomingAuth);

        // Create the parameters to send for a MakeConnection for this SpaceServer.
        // This will include  the preferred transport and protocol.
        // public static ParamBlock CreateMakeConnectionParams();

        // Called when CloseSession is received
        public virtual void CloseSessionProcessing(BasilConnection pConnection) {
            this.Stop();
        }

        public void ShutdownConnection() {
            _RContext.log.Info("{0} Shutdown for SpaceServer {1} for user {2}", _logHeader, LayerType, AgentUUID);
            // wait a little bit so shutdown messages can make it to the user
            Thread.Sleep(1000);
            // Tell everyone around that we're going down
            _cancellerSource.Cancel();
            // Close and release the connection
            if (_connection != null) {
                _connection.Stop();
                _connection = null;
            }
            _RContext.removeSpaceServer(this);
        }

        // Called when connection state changes.
        // Called with the new state and a reference to the SpaceServer the connection is for
        protected virtual void ProcessConnectionStateChange(BConnectionStates pConnectionState, BasilConnection pConn) {
            switch (pConnectionState) {
                case BConnectionStates.OPEN: {
                    break;
                }
                case BConnectionStates.CLOSED: {
                    _RContext.log.Debug("{0}: ProcessConnectionStateChange CLOSED for {1}. Shutting down",
                                    _logHeader, LayerType);
                    Stop();
                    break;
                }
                case BConnectionStates.ERROR: {
                    _RContext.log.Debug("{0}: ProcessConnectionStateChange ERROR for {1}. Shutting down",
                                    _logHeader, LayerType);
                    Stop();
                    break;
                }
                case BConnectionStates.CLOSING: {
                    Stop();
                    break;
                }
                default: {
                    break;
                }
            }
        }


    }
}
