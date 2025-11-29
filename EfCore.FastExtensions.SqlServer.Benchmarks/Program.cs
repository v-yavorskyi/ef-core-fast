using BenchmarkDotNet.Running;
using EfCore.FastExtensions.SqlServer.Benchmarks.Benchmarks;

BenchmarkRunner.Run<JoinExtensionsBenchmarks>();
