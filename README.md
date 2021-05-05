# Hot Reload Utilities #

Testing utilities for .NET hot reload.

## What's in here ##

- [hotreload-delta-gen](src/hotreload-delta-gen/) - script driven delta generator ([README.md](src/hotreload-delta-gen/README.md))
- [Microsoft.DotNet.HotReload.Utils.Generator.BuildTool](src/Microsoft.DotNet.HotReload.Utils.Generator.BuildTool/) - MSBuild integration package to generate deltas as part of a build
<!-- FIXME: add this back when it's useful -->
<!-- - [DeltaGeneratorTask](src/Microsoft.DotNet.HotReload.Utils.DeltaGeneratorTask/) - MSBuild task that runs `hotreload-delta-gen` and collects its output into an msbuild item. -->
- [Microsoft.DotNet.HotReload.Utils.Generator](src/Microsoft.DotNet.HotReload.Utils.Generator/) - a library for creating tools that generate hot reload deltas
