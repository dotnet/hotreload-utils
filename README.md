
Simple roslyn E&C driver.

## How to use it

```
$ mono roslynildiff.exe [-l:Lib.dll ...] [-target:library] file.cs file_v1.cs file_v2.cs ...

```

It saves the baseline file to `file.dll` and `file.pdb` and saves the deltas to `file.dll.1.{dmeta,dil,dpdb}`, `file.dll.2.{dmeta,dil,dpdb}` etc...

The `file_vN.cs` files must include all the unchanged code from `file.cs`, too, not just the changes.

## How to build it

build the project in VS Mac.

It seems like if .NET Core is installed you need this `SQLitePCLRaw.core` nuget.

Tested with `Mono JIT compiler version 6.8.0.123 (2019-10/1d0d939dc30 Thu Mar 12 23:19:08 EDT 2020)`
