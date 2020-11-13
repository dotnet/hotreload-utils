
# Roslyn IL diff generator

Simple roslyn E&C driver.

## How to build

Install .NET 5, run `dotnet build`

## How to use it

### Example (bare C# files)

```console
dotnet run -- -out:out -target:library example/TestClass.cs example/TestClass_v1.cs

mdv out/TestClass.dll /il- /g:out/TestClass.dll.1.dmeta
```

We can use [mdv](https://github.com/dotnet/metadata-tools/tree/master/src/mdv) from [dotnet/metadata-tools](https://github.com/dotnet/metadata-tools) to examine the metadata of a baseline assembly and its delta.

### Example (msbuild)

```console
pushd example-msbuild
dotnet build /p:Configuration=Debug
popd

dotnet run -- -msbuild:example-msbuild/TestClass.csproj -p:Configuration=Debug example-msbuild/TestClass.cs  example-msbuild/TestClass_v1.cs example-msbuild/TestClass_v2.cs

mdv example-msbuild/bin/Debug/net5.0/TestClass.dll /il- /g:example-msbuild/bin/Debug/net5.0/TestClass.dll.1.dmeta /g:example-msbuild/bin/Debug/net5.0/TestClass.dll.2.dmeta
```

Use `msbuild` to build the project first, then run `roslyn-ildiff` to generate
a delta with respect to the `msbuild`-produced baseline.

### Details

```console
dotnet roslynildiff.dll [-mono] [-out:DIR] [-bcl:PATH] [-l:Lib.dll ...] [-target:library] file.cs file_v1.cs file_v2.cs ...

```

It saves the baseline file to `DIR/file.dll` and `DIR/file.pdb` and saves the deltas to `DIR/file.dll.1.{dmeta,dil,dpdb}`, `DIR/file.dll.2.{dmeta,dil,dpdb}` etc...

The `file_vN.cs` files must include all the unchanged code from `file.cs`, too, not just the changes.

By default it makes .NET 5 assemblies.  To build something for non-netcore Mono, pass `-mono` and `-bcl:PATH` where `PATH` is the directory containing `mscorlib.dll`, `System.dll`, etc.
