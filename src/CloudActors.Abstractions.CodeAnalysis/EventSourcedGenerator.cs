using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scriban;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
class EventSourcedGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.EventSourced.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.GetOrleansOptions();
        var actors = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(x => x.GetAttributes().Any(a => a.IsActor()) && x.AllInterfaces.Any(i => i.ToDisplayString(FullName) == "Devlooped.CloudActors.IEventSourced"))
            .Combine(context.CompilationProvider.Select((c, _) => c.GetTypeByMetadataName("Devlooped.CloudActors.IEventSourced")))
            .Where(x => x.Right != null && !x.Right
                // Only if users haven't already implemented *any* members of the interface
                .GetMembers()
                .Select(es => x.Left.FindImplementationForInterfaceMember(es))
                .Where(x => x != null)
                .Any())
            .Select((x, _) => x.Left)
            .Combine(context.CompilationProvider.Combine(context.ParseOptionsProvider));

        context.RegisterSourceOutput(actors.Combine(options), (ctx, source) =>
        {
            var ((actor, (compilation, parse)), options) = source;
            var ns = actor.ContainingNamespace.ToDisplayString();
            var model = new EventSourcedModel(
                Namespace: ns,
                Name: actor.Name,
                []);

            var output = template.Render(model, member => member.Name);
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(output, parse as CSharpParseOptions));
            var symbol = compilation.GetTypeByMetadataName(actor.ToDisplayString(FullName));
            Debug.Assert(symbol != null);

            var syntax = symbol!.DeclaringSyntaxReferences.First();
            var events = EventLocator.FindRaisedEvents(compilation, syntax.SyntaxTree);

            model = model with { Events = events.Select(e => e.GetTypeName(ns)) };
            output = template.Render(model, member => member.Name);

            ctx.AddSource($"{actor.ToFileName()}.g.cs", output);

            foreach (var ev in events)
                ctx.GenerateCode((ev, options));
        });
    }

    record EventSourcedModel(string Namespace, string Name, IEnumerable<string> Events, string Version = ThisAssembly.Info.InformationalVersion);
}
