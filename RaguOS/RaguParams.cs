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
using System.Reflection;
using System.Text;

using org.herbal3d.cs.CommonEntitiesUtil;

using Nini.Config;

namespace org.herbal3d.Ragu {
    public class RaguParams : IParameters {
        private static readonly string _logHeader = "[LODEN PARAMS]";
        private readonly RaguContext _context;

        public RaguParams(RaguContext pContext) {
            _context = pContext;
            SetParameterDefaultValues(_context);
        }

        // =====================================================================================
        // =====================================================================================
        // List of all of the externally visible parameters.
        // For each parameter, this table maps a text name to getter and setters.
        // To add a new externally referencable/settable parameter, add the paramter storage
        //    location somewhere in the program and make an entry in this table with the
        //    getters and setters.
        // It is easiest to find an existing definition and copy it.
        //
        // A ParameterDefn<T>() takes the following parameters:
        //    -- the text name of the parameter. This is used for console input and ini file.
        //    -- a short text description of the parameter. This shows up in the console listing.
        //    -- a default value
        //    -- a delegate for getting the value
        //    -- a delegate for setting the value
        //    -- an optional delegate to update the value in the world. Most often used to
        //          push the new value to an in-world object.
        //
        // The single letter parameters for the delegates are:
        //    v = value (appropriate type)
        private readonly ParameterDefnBase[] ParameterDefinitions =
        {
            new ParameterDefn<bool>("Enabled", "If false, module is not enabled to operate",
                false ),

            new ParameterDefn<bool>("IsSecure", "Base directory for Loden asset storage",
                false),
            new ParameterDefn<string>("SecureConnectionURL", "URL to use to create inbound connection",
                "wss://0.0.0.0:11440"),
            new ParameterDefn<string>("Certificate", "Certificate to accept for secure inbound connection",
                ""),
            new ParameterDefn<string>("ConnectionURL", "URL to use to create inbound connection",
                "ws://0.0.0.0:11440"),
            new ParameterDefn<bool>("DisableNaglesAlgorithm", "Whether to enable/disable outbound delay",
                true),

            new ParameterDefn<string>("OutputDir", "Base directory for Loden asset storage",
                "./LodenAssets" ),

            new ParameterDefn<bool>("LogBuilding", "log detail BScene/BInstance object building",
                true ),
            new ParameterDefn<bool>("LogGltfBuilding", "output detailed gltf construction details",
                false ),
        };

        // =====================================================================================
        // =====================================================================================

        // Base parameter definition that gets and sets parameter values via a string
        public abstract class ParameterDefnBase
        {
            public string name;         // string name of the parameter
            public string desc;         // a short description of what the parameter means
            public abstract Type GetValueType();
            public string[] symbols;    // extra command line versions of parameter (short form)
            public RaguContext context; // context for setting and getting values
            public ParameterDefnBase(string pName, string pDesc, string[] pSymbols)
            {
                name = pName;
                desc = pDesc;
                symbols = pSymbols;
            }
            // Set the parameter value to the default
            public abstract void AssignDefault();
            // Get the value as a string
            public abstract string GetValue();
            // Set the value to this string value
            public abstract void SetValue(string valAsString);
        }

        // Specific parameter definition for a parameter of a specific type.
        public sealed class ParameterDefn<T> : ParameterDefnBase
        {
            private readonly T defaultValue;
            private T value;

            public ParameterDefn(string pName, string pDesc, T pDefault, params string[] pSymbols)
                : base(pName, pDesc, pSymbols)
            {
                defaultValue = pDefault;
            }
            public T Value() {
                return value;
            }

            public override void AssignDefault() {
                value = defaultValue;
            }
            public override string GetValue()
            {
                string ret = String.Empty;
                if (value != null) {
                    ret = value.ToString();
                }
                return ret;
            }
            public override Type GetValueType() {
                return typeof(T);
            }
            public override void SetValue(String valAsString) {
                // Find the 'Parse' method on that type
                System.Reflection.MethodInfo parser = null;
                try {
                    parser = GetValueType().GetMethod("Parse", new Type[] { typeof(String) });
                }
                catch {
                    parser = null;
                }
                if (parser != null) {
                    // Parse the input string
                    try {
                        T setValue = (T)parser.Invoke(GetValueType(), new Object[] { valAsString });
                        // System.Console.WriteLine("SetValue: setting value on {0} to {1}", this.name, setValue);
                        // Store the parsed value
                        value = setValue;
                        context.log.DebugFormat("{0} SetValue. {1} = {2}", _logHeader, name, setValue);
                    }
                    catch (Exception e) {
                        context.log.ErrorFormat("{0} Failed parsing parameter value '{1}': '{2}'", _logHeader, valAsString, e);
                    }
                }
                else {
                    // If there is not a parser, try doing a conversion
                    try {
                        T setValue = (T)Convert.ChangeType(valAsString, GetValueType());
                        value = setValue;
                        context.log.DebugFormat("{0} SetValue. Converter. {1} = {2}", _logHeader, name, setValue);
                    }
                    catch (Exception e) {
                        context.log.ErrorFormat("{0} Conversion failed for {1}: {2}", _logHeader, this.name, e);
                    }
                }
            }
        }

        // Return a value for the parameter.
        // This is used by most callers to get parameter values.
        // Note that it outputs a console message if not found. Not found means that the caller
        //     used the wrong string name.
        public T P<T>(string paramName) {
            T ret = default(T);
            if (TryGetParameter(paramName, out ParameterDefnBase pbase)) {
                if (pbase is ParameterDefn<T> pdef) {
                    ret = pdef.Value();
                }
                else {
                    _context.log.ErrorFormat("{0} Fetched parameter of wrong type. Param={1}", _logHeader, paramName);
                }
            }
            else {
                _context.log.ErrorFormat("{0} Fetched unknown parameter. Param={1}", _logHeader, paramName);
            }
            return ret;
        }

        // Search through the parameter definitions and return the matching
        //    ParameterDefn structure.
        // Case does not matter as names are compared after converting to lower case.
        // Returns 'false' if the parameter is not found.
        public bool TryGetParameter(string paramName, out ParameterDefnBase defn)
        {
            bool ret = false;
            ParameterDefnBase foundDefn = null;
            string pName = paramName.ToLower();

            foreach (ParameterDefnBase parm in ParameterDefinitions)
            {
                if (pName == parm.name.ToLower())
                {
                    foundDefn = parm;
                    ret = true;
                    break;
                }
            }
            defn = foundDefn;
            return ret;
        }

        // Pass through the settable parameters and set the default values
        public void SetParameterDefaultValues(RaguContext pContext)
        {
            foreach (ParameterDefnBase parm in ParameterDefinitions)
            {
                parm.context = pContext;
                parm.AssignDefault();
            }
        }

        // Get user set values out of the ini file.
        public  void SetParameterConfigurationValues(IConfig cfg, RaguContext pContext)
        {
            foreach (ParameterDefnBase parm in ParameterDefinitions)
            {
                // _context.log.DebugFormat("{0}: parm={1}, desc='{2}'", _logHeader, parm.name, parm.desc);
                parm.context = pContext;
                string configValue = cfg.GetString(parm.name, parm.GetValue());
                if (!String.IsNullOrEmpty(configValue)) {
                    parm.SetValue(cfg.GetString(parm.name, parm.GetValue()));
                }
            }
        }
    }
}
