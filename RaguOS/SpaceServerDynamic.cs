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

using HT = org.herbal3d.transport;
using BT = org.herbal3d.basil.protocol.BasilType;

using org.herbal3d.cs.CommonEntitiesUtil;
using org.herbal3d.OSAuth;

using Google.Protobuf;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public class SpaceServerDynamic : HT.SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerDynamic]";

        private readonly RaguContext _context;

        public SpaceServerDynamic(RaguContext pContext, CancellationTokenSource pCanceller,
                                        HT.BasilConnection pBasilConnection,
                                        OSAuthToken pOpenSessionAuth) 
                        : base(pCanceller, pBasilConnection, "Dynamic") {
            _context = pContext;
            SessionAuth = pOpenSessionAuth;
        }

        protected override void DoShutdownWork() {
            return;
        }

        /// <summary>
        ///  The client does an OpenSession with 'login' information. Authorize the
        ///  logged in user.
        /// </summary>
        /// <param name="pUserToken">UserAuth token sent from the client making the OpenSession
        ///     which authenticates the access.</param>
        /// <returns>"true" if the user is authorized</returns>
        protected override bool VerifyClientAuthentication(OSAuthToken pUserToken) {
            // Verify this is good login info bafore accepting login
            return pUserToken.Matches(SessionAuth);
        }

        protected override void DoOpenSessionWork(HT.BasilConnection pConnection, HT.BasilComm pClient, BT.Props pParms) {
            _context.log.DebugFormat("{0} DoOpenSessionWork: ", _logHeader);
            // TODO: the right thing
            // Task.Run(() => {
            //    HandleBasilConnection(pConnection, pClient, pParms);
            //});
        }

        // I don't have anything to do for a CloseSession
        protected override void DoCloseSessionWork() {
            _context.log.DebugFormat("{0} DoCloseSessionWork: ", _logHeader);
            return;
        }
    }
}
