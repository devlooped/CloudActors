using System.Linq;
using Microsoft.CodeAnalysis;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
public class ActorBusOverloadGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var source = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(IsActorMessage)
            //.Where(IsPartial)
            .Combine(context.CompilationProvider
            .Select((c, _) => new
            {
                VoidCommand = c.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand"),
                Command = c.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand`1"),
                Query = c.GetTypeByMetadataName("Devlooped.CloudActors.IActorQuery`1"),
            }));

        context.RegisterSourceOutput(source, (ctx, item) =>
        {
            if (item.Right.VoidCommand is null || item.Right.Command is null || item.Right.Query is null)
                return;

            var file = $"{item.Left.ToFileName()}.g.cs";

            if (item.Left.AllInterfaces.Contains(item.Right.VoidCommand, SymbolEqualityComparer.Default))
            {
                ctx.AddSource(file,
                    $$"""
                    using System.Threading.Tasks;
                    using Devlooped.CloudActors;
                                        
                    static partial class ActorBusExtensions
                    {
                        public static Task ExecuteAsync(this IActorBus bus, string id, {{item.Left.ToDisplayString(FullName)}} command)
                            => bus.ExecuteAsync(id, (IActorCommand)command);
                    }
                    """);
            }
            else if (item.Left.AllInterfaces.FirstOrDefault(x => x.IsGenericType && x.ConstructedFrom.Equals(item.Right.Command, SymbolEqualityComparer.Default)) is INamedTypeSymbol command)
            {
                ctx.AddSource(file,
                    $$"""
                    using System.Threading.Tasks;
                    using Devlooped.CloudActors;

                    static partial class ActorBusExtensions
                    {
                        public static Task<{{command.TypeArguments[0].ToDisplayString(FullName)}}> ExecuteAsync(this IActorBus bus, string id, {{item.Left.ToDisplayString(FullName)}} command)
                            => bus.ExecuteAsync<{{command.TypeArguments[0].ToDisplayString(FullName)}}>(id, command);
                    }
                    """);
            }
            else if (item.Left.AllInterfaces.FirstOrDefault(x => x.IsGenericType && x.ConstructedFrom.Equals(item.Right.Query, SymbolEqualityComparer.Default)) is INamedTypeSymbol query)
            {
                ctx.AddSource(file,
                    $$"""
                    using System.Threading.Tasks;
                    using Devlooped.CloudActors;

                    static partial class ActorBusExtensions
                    {
                        public static Task<{{query.TypeArguments[0].ToDisplayString(FullName)}}> QueryAsync(this IActorBus bus, string id, {{item.Left.ToDisplayString(FullName)}} query)
                            => bus.QueryAsync<{{query.TypeArguments[0].ToDisplayString(FullName)}}>(id, query);
                    }
                    """);
            }
        });
    }
}
