using System.Text.Json.Serialization;

namespace Microsoft.DotNet.HotReload.Utils.OutputSummary {
    public class OutputSummary {
        [JsonPropertyName("deltas")]
        public Delta[]? Deltas {get; set; }
        [JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, object>? Extra {get; set;}
    }

    public class Delta {
        [JsonPropertyName("assembly")]
        public string? Assembly {get; set;}

        [JsonPropertyName("metadata")]
        public string? Metadata {get; set; }

        [JsonPropertyName("il")]
        public string? IL {get; set;}

        [JsonPropertyName("pdb")]
        public string? PDB {get; set;}

        [JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, object>? Extra {get; set;}
    }
}
