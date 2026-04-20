using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Devlooped.CloudActors;

/// <summary>
/// Invokes the STJ (System.Text.Json) source generator via reflection to produce 
/// serializer implementations for generated <c>JsonSerializerContext</c> types.
/// Discovers the STJ generator from assemblies loaded in the current build context.
/// </summary>
static class JsonContextGenerator
{
    static readonly Lazy<Type?> generatorType = new(FindGeneratorType);

    /// <summary>Whether the STJ source generator is available in the current build context.</summary>
    public static bool IsAvailable => generatorType.Value != null;

    /// <summary>
    /// Runs the STJ source generator on the provided context source code, returning 
    /// the generated serializer implementation files.
    /// </summary>
    /// <returns>
    /// Generated source files filtered to those matching <paramref name="contextClassName"/>. 
    /// Empty if the STJ generator is not available or fails.
    /// </returns>
    public static ImmutableArray<(string HintName, string Source)> GenerateCode(
        Compilation compilation,
        CSharpParseOptions? parseOptions,
        string contextSource,
        string contextClassName,
        CancellationToken cancellation = default)
    {
        var type = generatorType.Value;
        if (type == null)
            return [];

        try
        {
            if (Activator.CreateInstance(type) is not IIncrementalGenerator generator)
                return [];

            var tree = CSharpSyntaxTree.ParseText(contextSource, parseOptions);
            compilation = compilation.AddSyntaxTrees(tree);

            var driver = CSharpGeneratorDriver.Create(
                generators: [generator.AsSourceGenerator()],
                parseOptions: parseOptions ?? CSharpParseOptions.Default);

            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation, cancellation);
            var result = driver.GetRunResult();

            if (result.Results.Length == 0)
                return [];

            // Only return output related to our context, not any user-defined contexts
            return [.. result.Results[0].GeneratedSources
                .Where(g => g.HintName.Contains(contextClassName))
                .Select(g => (g.HintName, g.SourceText.ToString()))];
        }
        catch
        {
            return [];
        }
    }

    static Type? FindGeneratorType()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "System.Text.Json.SourceGeneration");

            return asm?.GetType("System.Text.Json.SourceGeneration.JsonSourceGenerator");
        }
        catch
        {
            return null;
        }
    }
}
