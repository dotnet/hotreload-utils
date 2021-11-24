using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.HotReload.Utils.Generator.Script;

public class ParsedScript {
    /// null if the script didn't have any capabilities, or they were all knowns
    public EnC.EditAndContinueCapabilities? Capabilities {get; init;}

    public IEnumerable<Plan.Change<string,string>> Changes {get; init;}

    public IEnumerable<string> UnknownCapabilities {get; init;}
    public ParsedScript () {
        Capabilities = EnC.EditAndContinueCapabilities.None;
        Changes = Array.Empty<Plan.Change<string,string>>();
        UnknownCapabilities = Array.Empty<string>();
    }

    public static ParsedScript Empty => new ();

    public static ParsedScript Make(IEnumerable<Plan.Change<string,string>> changes, EnC.EditAndContinueCapabilities? capabilities, IEnumerable<string> unknownCapabilities)
    {
        return new ParsedScript {
            Capabilities = capabilities,
            Changes = changes,
            UnknownCapabilities = unknownCapabilities,
        };
    }
}
