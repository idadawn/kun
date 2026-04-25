using BenchmarkDotNet.Running;

// Entry point: BenchmarkDotNet discovers all classes decorated with its attributes.
// Run with: dotnet run -c Release --project benchmarks/Kun.Benchmarks.Grpc
// Filter: dotnet run -c Release --project benchmarks/Kun.Benchmarks.Grpc -- --filter "*ResultToRpc*"
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Entry class for BenchmarkRunner.</summary>
internal static partial class Program { }
