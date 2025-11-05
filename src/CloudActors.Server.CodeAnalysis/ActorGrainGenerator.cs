using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scriban;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
public class ActorGrainGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.ActorGrain.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.GetOrleansOptions();

        var actors = context.CompilationProvider
            .SelectMany((x, _) => x.GetAllTypes())
            .Where(t => t.IsActor());

        context.RegisterImplementationSourceOutput(actors.Combine(options), (ctx, source) =>
        {
            var (actor, options) = source;

            var attribute = actor.GetAttributes().First(x => x.IsActor());
            var state = default(string);
            var storage = default(string);
            if (attribute.ConstructorArguments.Length >= 1)
                state = attribute.ConstructorArguments[0].Value?.ToString() ?? null;
            if (attribute.ConstructorArguments.Length == 2)
                storage = attribute.ConstructorArguments[1].Value?.ToString() ?? null;

            var model = new GrainModel(
                Namespace: actor.ContainingNamespace.ToDisplayString(),
                Name: actor.Name,
                StateName: state,
                StorageName: storage,
                Version: ThisAssembly.Info.InformationalVersion,
                Queries: actor.GetMembers().OfType<IMethodSymbol>().Where(IsQuery).Select(ToOperation),
                Commands: actor.GetMembers().OfType<IMethodSymbol>().Where(IsCommand).Select(ToOperation),
                VoidCommands: actor.GetMembers().OfType<IMethodSymbol>().Where(IsVoidCommand).Select(ToOperation));

            var output = template.Render(model, member => member.Name);
            var orleans = OrleansGenerator.GenerateCode(options, output, actor.Name, ctx.CancellationToken);

            ctx.AddSource($"{actor.ToFileName()}.cs", output);
            ctx.AddSource($"{actor.ToFileName()}.orleans.cs", orleans);
        });
    }

    static GrainOperation ToOperation(IMethodSymbol method) => new(
        method.Name,
        method.Parameters[0].Type.ToDisplayString(FullName),
        method.ReturnType.ToDisplayString(FullName).StartsWith("System.Threading.Tasks.Task"));

    static bool IsQuery(IMethodSymbol method) => method.Parameters.Length == 1 && method.Parameters[0].Type.IsActorQuery();
    static bool IsCommand(IMethodSymbol method) => method.Parameters.Length == 1 && method.Parameters[0].Type.IsActorCommand();
    static bool IsVoidCommand(IMethodSymbol method) => method.Parameters.Length == 1 && method.Parameters[0].Type.IsActorVoidCommand();

    record GrainOperation(string Name, string Type, bool IsAsync);

    record GrainModel(string Namespace, string Name, string? StateName, string? StorageName, string Version,
        IEnumerable<GrainOperation> Queries, IEnumerable<GrainOperation> Commands, IEnumerable<GrainOperation> VoidCommands)
    {
        public bool QueryAsync => Queries.Any(x => x.IsAsync);
        public bool ExecuteAsync => Commands.Any();
        public bool ExecuteVoidAsync => VoidCommands.Any();
    }
}
