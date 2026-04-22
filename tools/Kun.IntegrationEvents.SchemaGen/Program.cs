using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Json.Schema;
using Json.Schema.Generation;
using Kun.EventBus;

// Tool CLI:
//     SchemaGen --assembly <path> --builder <Namespace.Type::StaticMethod> --out <dir>
// Optional:
//     --indent  (writes pretty-printed JSON instead of compact)
//     --overwrite  (replace existing files; otherwise fail if target exists)
//
// The --builder argument names a PUBLIC STATIC zero-arg method on a type loaded from
// --assembly that returns an IEventTypeRegistry. The consumer owns that method — the
// tool does not reach into DI or guess conventions. Example:
//
//     public static class KunEventRegistryBuilder
//     {
//         public static IEventTypeRegistry Build()
//             => // ... compose the registry using KunEventBusBuilder, then return it
//     }
//
//     SchemaGen --assembly ./bin/Release/net10.0/MyService.dll \
//               --builder MyService.KunEventRegistryBuilder::Build \
//               --out ./schemas

var parsed = ParseArgs(args);
if (parsed is null) return 1;

var (assemblyPath, builderSpec, outDir, indent, overwrite) = parsed.Value;

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"SchemaGen: assembly not found: {assemblyPath}");
    return 2;
}

Directory.CreateDirectory(outDir);

var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
var (typeName, methodName) = ParseBuilderSpec(builderSpec);
var type = assembly.GetType(typeName, throwOnError: false)
    ?? throw new InvalidOperationException($"Type '{typeName}' not found in {assembly.GetName().Name}");

var method = type.GetMethod(
    methodName,
    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
    types: Type.EmptyTypes)
    ?? throw new InvalidOperationException(
        $"Public static zero-arg method '{methodName}' not found on type '{typeName}'");

var instance = method.Invoke(obj: null, parameters: null)
    ?? throw new InvalidOperationException($"{typeName}::{methodName} returned null");

var registry = instance as IEventTypeRegistry
    ?? throw new InvalidOperationException(
        $"{typeName}::{methodName} returned {instance.GetType().FullName}, which does not implement IEventTypeRegistry.");

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = indent,
};

var writtenCount = 0;
foreach (var (eventId, clrType) in registry.GetAll())
{
    var outPath = Path.Combine(outDir, $"{eventId}.schema.json");
    if (!overwrite && File.Exists(outPath))
    {
        Console.Error.WriteLine($"SchemaGen: refusing to overwrite {outPath} (pass --overwrite to replace)");
        return 3;
    }

    var schema = new JsonSchemaBuilder()
        .FromType(clrType)
        .Build();

    var serialized = JsonSerializer.Serialize(schema, jsonOptions);
    File.WriteAllText(outPath, serialized);
    writtenCount++;

    Console.WriteLine($"SchemaGen: wrote {outPath}");
}

Console.WriteLine($"SchemaGen: exported {writtenCount} schema file(s) to {Path.GetFullPath(outDir)}");
return 0;

[SuppressMessage("Style", "IDE0008:Use explicit type", Justification = "Tuple inference keeps CLI parsing local.")]
static (string Assembly, string Builder, string OutDir, bool Indent, bool Overwrite)? ParseArgs(string[] a)
{
    string? assembly = null;
    string? builder = null;
    string? outDir = null;
    var indent = false;
    var overwrite = false;

    for (var i = 0; i < a.Length; i++)
    {
        switch (a[i])
        {
            case "--assembly":
                assembly = RequireValue(a, ref i, "--assembly"); break;
            case "--builder":
                builder = RequireValue(a, ref i, "--builder"); break;
            case "--out":
                outDir = RequireValue(a, ref i, "--out"); break;
            case "--indent":
                indent = true; break;
            case "--overwrite":
                overwrite = true; break;
            case "--help" or "-h":
                PrintUsage(); return null;
            default:
                Console.Error.WriteLine($"SchemaGen: unrecognised argument '{a[i]}'");
                PrintUsage();
                return null;
        }
    }

    if (assembly is null || builder is null || outDir is null)
    {
        Console.Error.WriteLine("SchemaGen: --assembly, --builder, and --out are all required.");
        PrintUsage();
        return null;
    }

    return (assembly, builder, outDir, indent, overwrite);
}

static string RequireValue(string[] args, ref int i, string flag)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException($"{flag} requires a value");
    }
    return args[++i];
}

static (string TypeName, string MethodName) ParseBuilderSpec(string spec)
{
    var parts = spec.Split("::", count: 2);
    if (parts.Length != 2)
    {
        throw new ArgumentException(
            $"--builder must be formatted as 'Namespace.Type::StaticMethod'; got '{spec}'");
    }
    return (parts[0], parts[1]);
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        SchemaGen --assembly <path-to-dll> --builder <Type::Method> --out <dir> [--indent] [--overwrite]

        Flags:
          --assembly   Consumer assembly that owns the event-type registry builder.
          --builder    Fully-qualified static method that returns an IEventTypeRegistry.
                       Format: Namespace.Type::MethodName. Method must be public + static
                       + zero-argument + return a concrete IEventTypeRegistry.
          --out        Directory to write {eventId}.schema.json files into. Created
                       if missing.
          --indent     Emit pretty-printed JSON (default: compact).
          --overwrite  Replace pre-existing schema files (default: fail).
        """);
}
