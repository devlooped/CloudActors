using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scriban;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
public class SiloBuilderGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.SiloBuilder.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var source = context.CompilationProvider
            .SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(x => x.GetAttributes().Any(IsActor))
            .Collect();

        context.RegisterSourceOutput(source, (ctx, item) =>
        {
            var model = new SiloModel(
                Version: ThisAssembly.Info.InformationalVersion,
                Grains: item.Select(x => x.ToDisplayString(FullName) + "Grain"));

            var output = template.Render(model, member => member.Name);

            ctx.AddSource($"CloudActorsExtensions.g.cs", output);
        });
    }

    record SiloModel(string Version, IEnumerable<string> Grains);
}
