# SignalR.AspNetCore package migration

The maintained ASP.NET Core adapter is [Akka.Streams.SignalR](SignalR.md). The older Akka.Streams.SignalR.AspNetCore package used a different package ID, assembly and namespace. See [the migration requirements](LegacyPackages.md#signalraspnetcore).

The old package included netcoreapp3.1 and net6.0 assets; the current package ships net6.0. Applications on netcoreapp3.1 need a framework migration before changing packages. Keep the existing artifact until that migration is tested. This page remains at its historical URL; the current package README contains the maintained examples.
