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
        var source = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes())
            .Where(x => x is INamedTypeSymbol named && x.GetAttributes().Any(IsActor) && 
                // Only those actors that are event-sourced
                named.AllInterfaces.Any(i => i.ToDisplayString(FullName) == "Devlooped.CloudActors.IEventSourced") && 
                // Only if users haven't already overriden the given method (unlikely)
                !named.GetMembers("Apply").OfType<IMethodSymbol>().Any(a => 
                    a.IsOverride && 
                    a.Parameters.Length == 1 && 
                    a.Parameters[0].Type.SpecialType == SpecialType.System_Object));

        context.RegisterSourceOutput(source, (ctx, actor) =>
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
