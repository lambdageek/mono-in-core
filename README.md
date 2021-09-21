# CoreCLR hosting MonoVM

An example of .NET 6 CoreCLR hosting a .NET 6 MonoVM and running a sample application.

## Building and running

You will need a .NET 6 SDK

```console
$ pushd InnerApp
$ dotnet publish --self-contained -r <RID>
$ popd
$ pushd SampleHost
$ dotnet run dotnet run  -- ../InnerApp/bin/Debug/net6.0/<RID>
```
