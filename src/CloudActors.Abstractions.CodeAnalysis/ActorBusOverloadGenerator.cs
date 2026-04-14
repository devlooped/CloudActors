using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
class ActorBusOverloadGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaces = context.CompilationProvider
            .Select((c, _) => (
                VoidCommand: c.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand")?.ToDisplayString(FullName),
                Command: c.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand`1")?.ToDisplayString(FullName),
                Query: c.GetTypeByMetadataName("Devlooped.CloudActors.IActorQuery`1")?.ToDisplayString(FullName)))
            .WithTrackingName(TrackingNames.BusInterfaces);

        var messages = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) =>
                node is TypeDeclarationSyntax tds &&
                tds.BaseList?.Types.Count > 0,
            transform: static (ctx, ct) =>
            {
                if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol type)
                    return default;

                if (!type.IsActorMessage())
                    return default;

                var voidCommand = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand");
                var command = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand`1");
                var query = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorQuery`1");

                return ModelExtractors.ExtractActorMessageModel(type, voidCommand, command, query);
            })
            .Where(static x => x != null)
            .Select(static (x, _) => x!.Value)
            .WithTrackingName(TrackingNames.BusOverloads);

        context.RegisterSourceOutput(messages, (ctx, model) =>
        {
            var file = $"{model.FileName}.g.cs";

            switch (model.Kind)
            {
                case ActorMessageKind.VoidCommand:
                    ctx.AddSource(file,
                        $$"""
                        using System.Threading.Tasks;
                        using Devlooped.CloudActors;
                                            
                        static partial class ActorBusExtensions
                        {
                            public static Task ExecuteAsync(this IActorBus bus, string id, {{model.FullName}} command)
                                => bus.ExecuteAsync(id, (IActorCommand)command);
                        }
                        """);
                    break;

                case ActorMessageKind.Command:
                    ctx.AddSource(file,
                        $$"""
                        using System.Threading.Tasks;
                        using Devlooped.CloudActors;

                        static partial class ActorBusExtensions
                        {
                            public static Task<{{model.ReturnTypeFullName}}> ExecuteAsync(this IActorBus bus, string id, {{model.FullName}} command)
                                => bus.ExecuteAsync<{{model.ReturnTypeFullName}}>(id, command);
                        }
                        """);
                    break;

                case ActorMessageKind.Query:
                    ctx.AddSource(file,
                        $$"""
                        using System.Threading.Tasks;
                        using Devlooped.CloudActors;

                        static partial class ActorBusExtensions
                        {
                            public static Task<{{model.ReturnTypeFullName}}> QueryAsync(this IActorBus bus, string id, {{model.FullName}} query)
                                => bus.QueryAsync<{{model.ReturnTypeFullName}}>(id, query);
                        }
                        """);
                    break;
            }
        });
    }
}
