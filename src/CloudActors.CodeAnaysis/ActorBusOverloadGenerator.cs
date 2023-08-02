using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
public class ActorBusOverloadGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var source = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes())
            .Where(x => x.GetAttributes().Any(IsActorOperation))
            .Combine(context.CompilationProvider)
            .Select((x, _) =>
            {
                if (x.Left is not INamedTypeSymbol command ||
                    !IsPartial(command) ||
                    command.GetAttributes().FirstOrDefault(IsActorOperation) is not AttributeData attr ||
                    attr.ApplicationSyntaxReference?.GetSyntax() is not SyntaxNode syntax ||
                    x.Right.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax) is not IAttributeOperation operation ||
                    operation.Operation.Type is not INamedTypeSymbol type)
                    return default;

                return new { Operation = command, Attribute = type };
            });

        context.RegisterSourceOutput(source, (ctx, item) =>
        {
            if (item is null)
                return;

            if (item.Attribute.IsGenericType)
            {
                var result = item.Attribute.TypeArguments[0];
                var flavor = item.Attribute.Name == "ActorQueryAttribute" ? "Query" : "Execute";
                var arg = flavor == "Query" ? "query" : "command";

                ctx.AddSource($"{item.Operation.ToFileName()}.g.cs",
                    $$"""
                    using System.Threading.Tasks;
                    using Devlooped.CloudActors;

                    static partial class ActorBusExtensions
                    {
                        public static Task<{{result.ToDisplayString(FullName)}}> {{flavor}}Async(this IActorBus bus, string id, {{item.Operation.ToDisplayString(FullName)}} {{arg}})
                            => bus.{{flavor}}Async<{{result.ToDisplayString(FullName)}}>(id, {{arg}});
                    }
                    """);
            }
            else
            {
                ctx.AddSource($"{item.Operation.ToFileName()}.g.cs",
                    $$"""
                    using System.Threading.Tasks;
                    using Devlooped.CloudActors;
                                        
                    static partial class ActorBusExtensions
                    {
                        public static Task ExecuteAsync(this IActorBus bus, string id, {{item.Operation.ToDisplayString(FullName)}} command)
                            => bus.ExecuteAsync(id, (IActorCommand)command);
                    }
                    """);
            }
        });
    }
}
