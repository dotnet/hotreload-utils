// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.DotNet.HotReload.Utils.Generator.Script.Json {
    public class Script {
        public Change[]? Changes {get; init;}

        [System.Text.Json.Serialization.JsonConstructor]
        public Script (Change[]? changes) {
            Changes = changes;
        }
    }

    public class Change {
        public string Document {get; init;}
        public string Update {get; init;}

        [System.Text.Json.Serialization.JsonConstructor]
        public Change (string document, string update) {
            Document = document;
            Update = update;
        }
    }
}
