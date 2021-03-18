# Hot Reload Utilities #

Testing utilities for .NET hot reload.

## What's in here ##

- [hotreload-delta-gen](src/hotreload-delta-gen/) - script driven delta generator ([README.md](src/hotreload-delta-gen/README.md))
- [DeltaGeneratorTask](src/Microsoft.DotNet.HotReload.Utils.DeltaGeneratorTask/) - MSBuild task that runs `hotreload-delta-gen` and collects its output into an msbuild item.
- [Microsoft.DotNet.HotReload.Utils.Generator](src/Microsoft.DotNet.HotReload.Utils.Generator/) - a library for creating tools that generate hot reload deltas
