using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator]
public class ExecuteGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all invocations to ExecuteAsync and SendAsync and get the type of the command
        // to generate a partial record for it.
        var nodes = context.SyntaxProvider.CreateSyntaxProvider(
            (node, _) => node is InvocationExpressionSyntax invocation &&
                (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ExecuteAsync" } ||
                invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SendAsync" }),
            (ctx, c) =>
            {
                var invocation = (InvocationExpressionSyntax)ctx.Node;
                var arg = invocation.ArgumentList.Arguments.Last();
                var operation = ctx.SemanticModel.GetOperation(arg.Expression);
                if (operation?.Type is INamedTypeSymbol named)
                    return named;

                return null;
            });

        context.RegisterSourceOutput(nodes.Combine(context.CompilationProvider), (ctx, item) =>
        {
            if (item.Right is null || item.Left is null)
                return;

            if (item.Left.GetAttributes().FirstOrDefault(IsActor) is not AttributeData attr ||
                attr.ApplicationSyntaxReference?.GetSyntax() is not SyntaxNode syntax ||
                item.Right.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax) is not IAttributeOperation operation ||
                operation.Operation.Type is not INamedTypeSymbol attribute)
                return;

            if (attribute.IsGenericType)
            {
                var result = attribute.TypeArguments[0];
                ctx.AddSource($"{item.Left.ToDisplayString(FullName)}.g.cs",
                    $$"""
                    using System.Threading.Tasks;
                    using Devlooped.CloudActors;

                    static partial class ActorBusExtensions
                    {
                        public static Task<{{result.ToDisplayString(FullName)}}> ExecuteAsync(this IActorBus bus, string id, {{item.Left.ToDisplayString(FullName)}} command)
                            => bus.ExecuteAsync<{{result.ToDisplayString(FullName)}}>(id, command);
                    }
                    """);
            }
            else
            {
                ctx.AddSource($"{item.Left.ToDisplayString(FullName)}.g.cs",
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
        });
    }
}
