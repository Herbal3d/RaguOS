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

using org.herbal3d.cs.CommonUtil;
using org.herbal3d.transport;
using org.herbal3d.OSAuth;

namespace org.herbal3d.Ragu {

    // Function called when a connection is available for a SpaceServer
    public delegate SpaceServerBase CreateSpaceServerProcessor(BTransport pTransport,
                                                    CancellationTokenSource pCanceller
                                                    );
    /**
     * Class that listens for SpaceServer connections and starts
     * the appropriate SPaceServer to listen for a connection.
     */
    public class SpaceServerListener {
        BLogger _log;
        CreateSpaceServerProcessor _creator;

        BTransportParams[] _transportParams;

        // Listen for a connection and call the passed SpaceServer creater when a connection is made.
        // This canceller is for the whole service. A new one is created for the connection.
        public SpaceServerListener(
                        CreateSpaceServerProcessor processor,
                        CancellationTokenSource canceller,
                        BLogger logger,
                        BTransportParams[] transportParams,
                        string layer = "Layer"
            ) {

            _log = logger;
            _creator = processor;
            _transportParams = transportParams;

            // For the moment, we assume there is only the WS transport.
            // Eventually, the parameters will specifiy multiple transports
            //    and this routine will open several listeners.

            foreach (var xportParam in transportParams) {
                if (xportParam is BTransportWSParams) {
                    BTransportWS.ConnectionListener(
                        param: xportParam as BTransportWSParams,
                        logger: _log,
                        connectionProcessor: (pTrans, pCan) => _creator(pTrans, pCan),
                        cancellerSource: canceller
                    );
                }
            }
            // start listening and call SpaceServer creator when a connection is made

        }

        /*
         * Create the block to send in an OpenConnection that will connect back to this listener
         * This happens when a MakeConnection is sent to a client and we're waiting for
         * them to do the responding OpenSession.
         * This routine builds the parameter block that is sent in the MakeConnection
         *    and remembers the information to await for the OpenSession.
         * @returns either ParamBlock of MakeConnection parameters or null if not
         *    to make a connection for this SpaceServer.
         */
        public virtual Dictionary<string,object> ParamsForMakeConnection(string pExternalHostname, OSAuthToken pServiceAuth) {
            
            // Select the preferred transport for this service
            BTransportParams parms = _transportParams.Where(pp => pp.preferred).First();
            // Build the block of parameters needed for  the MakeConnection
            return new Dictionary<string, object>() {
                { "transport",    parms.transport },
                { "transportURL", parms.ExternalURL(pExternalHostname) },
                { "protocol",     parms.protocol },
                { "service",      "SpaceServer" },
                { "serviceAuth",  pServiceAuth.Token }
            };
        }
    }
}