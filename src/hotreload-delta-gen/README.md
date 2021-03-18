
# Roslyn Hot Reload delta generator

Simple roslyn E&C driver.

## How to build

Install .NET 5, run `dotnet build src/hotreload-delta-gen.csproj`

### Deploying

Running

```
dotnet publish --self-contained -r <RID> src/hotreload-delta-gen.csproj
```

will create a folder with a self contained executable `../../publish/hotreload-delta-gen/hotreload-delta-gen` for the specified platform.


## How to use it

```console
Usage: hotreload-delta-gen.exe -msbuild:project.csproj [-p:MSBuildProp=Value ...] [-live|-script:script.json [-outputSummary:result.json]]
```

### Example (scripted)

The diff expects that a `.csproj` has already been compiled to generate a baseline, and then
it reads a script of changes to apply to the baseline to generate deltas.  Additional properties
may be passed to `msbuild` (for example to identify the build configuration).

```console
dotnet build example/TestClass.csproj /p:Configuration=Debug

dotnet run --project src/hotreload-delta-gen.csproj -- -msbuild:example/TestClass.csproj -p:Configuration=Debug -script:example/diffscript.json -outputSummary:result.json

mdv ../artifacts/TestClass/bin/Debug/net5.0/TestClass.dll /il- /g:../artifacts/TestClass/bin/Debug/net5.0/TestClass.dll.1.dmeta /g:../artifacts/TestClass/bin/Debug/net5.0/TestClass.dll.2.dmeta
```

Use `msbuild` to build the project first, then run `hotreload-delta-gen` to generate
a delta with respect to the `msbuild`-produced baseline.

The format of the JSON script is

```json
{"changes":
    [
        {"document": "relativePath/to/file.cs", "update": "relativePath/to/file_v1.cs"},
        {"document": "file2.cs", "update": "file2_v2.cs"},
        {"document": "relativePath/to/file.cs", "update": "relativePath/to/file_v3.cs"}
    ]
}
```

The update files can have arbitrary names.  The document files should be part of the specified `.csproj`.  Zero or more updates may be included.

The tool will error out if one of the files contains a rude edit or no edits at all.

Each update file `file_vN.cs` must include the complete cumulative code from `file.cs`, too, not just the latest changes.

The output summary file will be of the form:

```json
{"deltas": [
    {"assembly": "TestClass.dll", "metadata": ".../TestClass.dll.1.dmeta", "il": ".../TestClass.dll.1.dil", "pdb": ".../TestClass.dll.1.dpdb"},
    {"assembly": "TestClass.dll", "metadata": ".../TestClass.dll.2.dmeta", "il": ".../TestClass.dll.2.dil", "pdb": ".../TestClass.dll.2.dpdb"},
]}
```


### Example (live coding)

Pass the `-live` option to start a file system watcher (in the directory of the `.csproj` to watch for changes).  Each time a `.cs` file is saved, the tool will generate a delta

```console
dotnet build example/TestClass.csproj /p:Configuration=Debug

dotnet run --project src/hotreload-delta-gen.csproj -- -msbuild:example/TestClass.csproj -p:Configuration=Debug -live
# make a change to TestClass.cs and save
# tool generates a delta
# Ctrl-C to terminate
```

The tool will error out if the file contains a rude edit, or but if a file is semantically unchanged (e.g. comments edited) it will ignore the change and wait for the next one.

### Output location

If the baseline files are in `DIR/assembly.dll` and `DIR/assembly.pdb`, then `roslynildiff` saves the deltas to `DIR/assembly.dll.1.{dmeta,dil,dpdb}`, `DIR/assembly.dll.2.{dmeta,dil,dpdb}` etc...

