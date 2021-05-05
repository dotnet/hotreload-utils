// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;


namespace Microsoft.DotNet.HotReload.Utils.Generator.OutputSummary
{
    public class OutputSummary {
        [JsonPropertyName("deltas")]
        public Delta[] Deltas {get; }

        public OutputSummary(Delta[] deltas) {
            Deltas = deltas;
        }
    }

    public class Delta {
        [JsonPropertyName("assembly")]
        public string Assembly {get; }
        [JsonPropertyName("metadata")]
        public string Metadata {get; }
        [JsonPropertyName("il")]
        public string IL {get; }
        [JsonPropertyName("pdb")]
        public string Pdb {get; }

        public Delta (string assembly, string metadata, string il, string pdb) {
            Assembly = assembly;
            Metadata = metadata;
            IL = il;
            Pdb = pdb;
        }
    }
}
