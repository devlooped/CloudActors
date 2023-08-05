using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scriban;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
public class EventSourcedGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.EventSourced.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var actors = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(x => x.GetAttributes().Any(IsActor) && x.AllInterfaces.Any(i => i.ToDisplayString(FullName) == "Devlooped.CloudActors.IEventSourced"))
            .Combine(context.CompilationProvider.Select((c, _) => c.GetTypeByMetadataName("Devlooped.CloudActors.IEventSourced")))
            .Where(x => x.Right != null && !x.Right
                // Only if users haven't already implemented *any* members of the interface
                .GetMembers()
                .Select(es => x.Left.FindImplementationForInterfaceMember(es))
                .Where(x => x != null)
                .Any())
            .Select((x, _) => x.Left);

        context.RegisterSourceOutput(actors, (ctx, actor) =>
        {
            var model = new EventSourcedModel(
                Namespace: actor.ContainingNamespace.ToDisplayString(),
                Name: actor.Name,
                Version: ThisAssembly.Info.InformationalVersion,
                Events: actor.GetMembers().OfType<IMethodSymbol>().Where(x => x.Name == "Apply" && x.Parameters.Length == 1)
                    .Select(x => x.Parameters[0].Type.ToDisplayString(FullName)));

            var output = template.Render(model, member => member.Name);

            ctx.AddSource($"{actor.ToFileName()}.g.cs", output);
        });
    }

    record EventSourcedModel(string Namespace, string Name, string Version, IEnumerable<string> Events);
}
