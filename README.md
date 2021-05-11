# Hot Reload Utilities #

Testing utilities for .NET hot reload.

## What's in here ##

- [Microsoft.DotNet.HotReload.Utils.Generator.BuildTool](src/Microsoft.DotNet.HotReload.Utils.Generator.BuildTool/) - MSBuild integration package to generate deltas from a script of changes.  This is what you want for CI testing. ([README.md](src/Microsoft.DotNet.HotReload.Utils.Generator.BuildTool/README.md))
<!-- FIXME: add this back when it's useful -->
<!-- - [DeltaGeneratorTask](src/Microsoft.DotNet.HotReload.Utils.DeltaGeneratorTask/) - MSBuild task that runs `hotreload-delta-gen` and collects its output into an msbuild item. -->
- [Microsoft.DotNet.HotReload.Utils.Generator](src/Microsoft.DotNet.HotReload.Utils.Generator/) - A library for creating tools that generate hot reload deltas.
- [hotreload-delta-gen](src/hotreload-delta-gen/) - A script-driven delta generator packaged as a `dotnet tool`.  This is mostly useful for experimentation. ([README.md](src/hotreload-delta-gen/README.md))
