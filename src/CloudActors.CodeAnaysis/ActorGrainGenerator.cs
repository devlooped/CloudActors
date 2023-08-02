using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scriban;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
public class ActorGrainGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.ActorGrain.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var source = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes())
            .Where(x => x is INamedTypeSymbol && x.GetAttributes().Any(IsActor));

        context.RegisterSourceOutput(source, (ctx, actor) =>
        {
            var model = new GrainModel(
                Namespace: actor.ContainingNamespace.ToDisplayString(),
                Name: actor.Name,
                Version: ThisAssembly.Info.InformationalVersion,
                Queries: actor.GetMembers().OfType<IMethodSymbol>().Where(IsQuery).Select(ToOperation),
                Commands: actor.GetMembers().OfType<IMethodSymbol>().Where(IsCommand).Select(ToOperation),
                VoidCommands: actor.GetMembers().OfType<IMethodSymbol>().Where(IsVoidCommand).Select(ToOperation));

            var output = template.Render(model, member => member.Name);

            ctx.AddSource($"{actor.ToFileName()}.g.cs", output);
        });
    }

    static GrainOperation ToOperation(IMethodSymbol method) => new(
        method.Name,
        method.Parameters[0].Type.ToDisplayString(FullName),
        method.ReturnType.ToDisplayString(FullName).StartsWith("System.Threading.Tasks.Task"));

    static bool IsQuery(IMethodSymbol method) => method.Parameters.Length == 1 && method.Parameters[0].Type.GetAttributes().Any(IsActorQuery);
    static bool IsCommand(IMethodSymbol method) => method.Parameters.Length == 1 && method.Parameters[0].Type.GetAttributes().Any(IsActorCommand);
    static bool IsVoidCommand(IMethodSymbol method) => method.Parameters.Length == 1 && method.Parameters[0].Type.GetAttributes().Any(IsActorVoidCommand);

    record GrainOperation(string Name, string Type, bool IsAsync);

    record GrainModel(string Namespace, string Name, string Version,
        IEnumerable<GrainOperation> Queries, IEnumerable<GrainOperation> Commands, IEnumerable<GrainOperation> VoidCommands)
    {
        public bool QueryAsync => Queries.Any(x => x.IsAsync);
        public bool ExecuteAsync => Commands.Any();
        public bool ExecuteVoidAsync => VoidCommands.Any();
    }
}
