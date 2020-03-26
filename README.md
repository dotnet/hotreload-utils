
Simple roslyn E&C driver.

## How to use it

```
$ mono roslynildiff.exe file.cs

# make some changes to the source file
# <enter>
```

It saves the baseline file to `Output/file.dll` and `Output/file.pdb` and saves the delta to `Output/file.dll.1.{dmeta,dil,dpdb}`

## How to build it

build the project in VS Mac.

It seems like if .NET Core is installed you need this `SQLitePCLRaw.core` nuget.

Tested with `Mono JIT compiler version 6.8.0.123 (2019-10/1d0d939dc30 Thu Mar 12 23:19:08 EDT 2020)`
