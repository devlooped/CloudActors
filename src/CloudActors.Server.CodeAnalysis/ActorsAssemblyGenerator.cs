using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

/// <summary>
/// Analyzes referenced assemblies from current compilation and emits an attribute 
/// for each one that instructs the Orleans code generator to inspect the assembly 
/// and generate code for it: GenerateCodeForDeclaringAssembly. 
/// This allows us to avoid polluting the domain/actor assemblies with Orleans-specifics 
/// other than the [GenerateSerializer] attribute and state classes, but which have no 
/// implementations. Grains and everything else are generated in the project referencing 
/// the Orleans.Server package only.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class ActorsAssemblyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.GetOrleansOptions();
        var assemblies = context.CompilationProvider
                    .SelectMany((x, _) => x.GetUsedAssemblyReferences().Select(r => new { Compilation = x, Reference = r }))
                    .Select((x, _) => x.Compilation.GetAssemblyOrModuleSymbol(x.Reference))
                    .Where(x => x is IAssemblySymbol asm && asm.GetAttributes().Any(
                        a => "Devlooped.CloudActorsAttribute".Equals(a.AttributeClass?.ToDisplayString(FullName))))
                    .Select((x, _) => (IAssemblySymbol)x!)
                    .Collect();

        context.RegisterImplementationSourceOutput(assemblies.Combine(options), GenerateCode);
    }

    static void GenerateCode(SourceProductionContext ctx, (ImmutableArray<IAssemblySymbol>, OrleansGeneratorOptions) source)
    {
        var (assemblies, options) = source;

        // Don't duplicate any of the already generated code for the current assembly
        options = options with { Compilation = options.Compilation.RemoveAllSyntaxTrees() };

        var output = new StringBuilder().AppendLine(
            """
            // <auto-generated/>            
            using Orleans;

            """);

        foreach (var assembly in assemblies)
        {
            output.AppendLine($"[assembly: ApplicationPartAttribute(\"{assembly.Name}\")]");
        }

        foreach (var type in assemblies.Select(x => x.GetAllTypes()
            .FirstOrDefault(x => x.ContainingType == null && options.Compilation.IsSymbolAccessibleWithin(x, options.Compilation.Assembly))))
        {
            if (type == null)
                continue;

            output.AppendLine($"[assembly: GenerateCodeForDeclaringAssembly(typeof({type.ToDisplayString(FullName)}))]");
        }

        var orleans = OrleansGenerator.GenerateCode(options, output.ToString(), "References", ctx.CancellationToken);

        ctx.AddSource($"CloudActors.cs", output.ToString());
        ctx.AddSource($"CloudActors.orleans.cs", orleans);
    }
}