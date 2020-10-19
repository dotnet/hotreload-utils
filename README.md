
Simple roslyn E&C driver.

## How to use it

```
$ dotnet roslynildiff.dll [-mono] [-bcl:PATH] [-l:Lib.dll ...] [-target:library] file.cs file_v1.cs file_v2.cs ...

```

It saves the baseline file to `file.dll` and `file.pdb` and saves the deltas to `file.dll.1.{dmeta,dil,dpdb}`, `file.dll.2.{dmeta,dil,dpdb}` etc...

The `file_vN.cs` files must include all the unchanged code from `file.cs`, too, not just the changes.

By default it makes .NET 5 assemblies.  To build something for non-netcore Mono, pass `-mono` and `-bcl:PATH` where `PATH` is the directory containing `mscorlib.dll`, `System.dll`, etc.

## How to build it

Install .NET 5, run `dotnet build`

## Example

```console
$ dotnet run -- -out:out -target:library example/TestClass.cs example/TestClass_v1.cs
$ mdv out/TestClass.dll.1.dmeta
```

See [mdv](https://github.com/dotnet/metadata-tools/tree/master/src/mdv) from https://github.com/dotnet/metadata-tools