using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scriban;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

/// <summary>
/// A source generator that creates a [JsonConstructor] containing the id plus 
/// all properties with private constructors, so restoring their values when snapshots 
/// are read does not require any additional attributes applied to them.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class ActorSnapshotGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.ActorSnapshot.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var source = context.CompilationProvider
            .SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(x => x.GetAttributes().Any(IsActor));

        context.RegisterImplementationSourceOutput(source, (ctx, actor) =>
        {
            var props = actor.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(x =>
                    x.CanBeReferencedByName &&
                    !x.IsIndexer &&
                    !x.IsAbstract &&
                    x.SetMethod is { } setter &&
                    setter.DeclaredAccessibility == Accessibility.Private)
                .ToArray();

            if (props.Length > 0)
            {
                var model = new SnapshotModel(
                    Namespace: actor.ContainingNamespace.ToDisplayString(),
                    Name: actor.Name,
                    Parameters: props.Select(x => new Parameter(
                        char.ToLowerInvariant(x.Name[0]) + new string(x.Name.Skip(1).ToArray()),
                        x.Type.ToDisplayString(FullName), x.Name)));

                var output = template.Render(model, member => member.Name);

                ctx.AddSource($"{actor.ToFileName()}.g.cs", output);
            }
        });
    }

    record Parameter(string Name, string Type, string Property);

    record SnapshotModel(string Namespace, string Name, IEnumerable<Parameter> Parameters);
}
