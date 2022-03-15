// Copyright 2022 Robert Adams
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
//     Unless required by applicable law or agreed to in writing, software
//     distributed under the License is distributed on an "AS IS" BASIS,
//     WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//     See the License for the specific language governing permissions and
//     limitations under the License.

using org.herbal3d.cs.CommonUtil;

using log4net;

namespace org.herbal3d.Ragu {
    class BLoggerLog4Net: BLogger {

        private readonly ILog _log;

        public BLoggerLog4Net(ILog pLog) {
            _log = pLog;
        }

        void BLogger.SetLogLevel(LogLevels pLevel) {
            // throw new NotImplementedException();
        }
        void BLogger.Debug(string pMsg, params object[] pArgs) {
            if (_log.IsDebugEnabled) {
                _log.DebugFormat(pMsg, pArgs);
            }
        }

        void BLogger.Error(string pMsg, params object[] pArgs) {
            if (_log.IsErrorEnabled) {
                _log.ErrorFormat(pMsg, pArgs);
            }
        }

        void BLogger.Info(string pMsg, params object[] pArgs) {
            if (_log.IsInfoEnabled) {
                _log.InfoFormat(pMsg, pArgs);
            }
        }

        void BLogger.Trace(string pMsg, params object[] pArgs) {
            _log.DebugFormat(pMsg, pArgs);
        }

        void BLogger.Warn(string pMsg, params object[] pArgs) {
            if (_log.IsWarnEnabled) {
                _log.WarnFormat(pMsg, pArgs);
            }
        }
    }
}
