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
                                                    CancellationTokenSource pCanceller,
                                                    ParamBlock pListenerParams);
    /**
     * Class that listens for SpaceServer connections and starts
     * the appropriate SPaceServer to listen for a connection.
     */
    public class SpaceServerListener {
        BLogger _log;
        string _spaceServerType;
        CreateSpaceServerProcessor _creator;
        ParamBlock _params;

        // Listen for a connection and call the passed SpaceServer creater when a connection is made.
        // This canceller is for the whole service. A new one is created for the connection.
        public SpaceServerListener(ParamBlock pParams, CancellationTokenSource pCanceller,
                                        BLogger pDebugLogger, CreateSpaceServerProcessor pProcessor) {
            _log = pDebugLogger;
            _creator = pProcessor;

            // We expect these parameters
            _params = new ParamBlock(null, pParams,
                    new ParamBlock(new Dictionary<string, object>() {
                        {  "ConnectionURL",          "" },
                        {  "Protocol",               "Basil-JSON" },
                        {  "Transport",              "WS" },
                        {  "Layer",                  "Layer" },
                        {  "IsSecure",               false},
                        {  "SecureConnectionURL",    ""},
                        {  "Certificate",            null},
                        {  "DisableNaglesAlgorithm", true},
                    }));

            // For the moment, we assume there is only the WS transport.
            // Eventually, the parameters will specifiy multiple transports
            //    and this routine will open several listeners.

            // start listening and call SpaceServer creator when a connection is made
            BTransportWS.ConnectionListener(_params,
                    (pTrans, pCan, pParm) => pProcessor(pTrans, pCan, pParm),
                    pCanceller, pDebugLogger);
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
        public virtual ParamBlock ParamsForMakeConnection(OSAuthToken pServiceAuth) {
            return new ParamBlock(new Dictionary<string, object>() {
                { "transport",    _params.P<string>("Transport") },
                { "transportURL", _params.P<string>("ConnectionURL") },
                { "protocol",     _params.P<string>("Protocol") },
                { "service",      "SpaceServer" },
                { "serviceAuth",   pServiceAuth.Token }
            });
        }

    }
}