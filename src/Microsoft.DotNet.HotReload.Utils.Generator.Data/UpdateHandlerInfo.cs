using System.Collections.Immutable;
using System.Text.Json.Serialization;


namespace Microsoft.DotNet.HotReload.Utils.Generator;

public class UpdateHandlerInfo {

    [JsonPropertyName("updatedTypes")]
    public ImmutableArray<int> UpdatedTypes {get; init;}

    public UpdateHandlerInfo(ImmutableArray<int> updatedTypes) {
        UpdatedTypes = updatedTypes;
    }
}
