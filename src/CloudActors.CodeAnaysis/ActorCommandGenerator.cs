using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[Generator]
public class ActorCommandGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var x = context.CompilationProvider.SelectMany((x, _) => x.Assembly.GetAllTypes())
            .Where(x => x.GetAttributes().Any(IsActor))
            .Combine(context.CompilationProvider)
            .Select((x, _) =>
            {
                if (x.Left is not INamedTypeSymbol command ||
                    !IsPartial(command) ||
                    command.GetAttributes().FirstOrDefault(IsActor) is not AttributeData attr ||
                    attr.ApplicationSyntaxReference?.GetSyntax() is not SyntaxNode syntax ||
                    x.Right.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax) is not IAttributeOperation operation ||
                    operation.Operation.Type is not INamedTypeSymbol type)
                    return default;

                return new { Command = command, Attribute = type };
            });

        context.RegisterSourceOutput(x, (ctx, x) =>
        {
            if (x is null)
                return;

            if (x.Attribute.IsGenericType)
            {
                var result = x.Attribute.TypeArguments[0];
                ctx.AddSource($"{x.Command.ToDisplayString(FullName)}.g.cs",
                    $$"""
                    using Devlooped.CloudActors;
                    
                    namespace {{x.Command.ContainingNamespace.ToDisplayString(FullName)}}
                    {
                        partial record {{x.Command.Name}} : IActorCommand<{{result.ToDisplayString(FullName)}}>;
                    }
                    """);
            }
            else
            {
                ctx.AddSource($"{x.Command.ToDisplayString(FullName)}.g.cs",
                    $$"""
                    using Devlooped.CloudActors;

                    namespace {{x.Command.ContainingNamespace.ToDisplayString(FullName)}}
                    {
                        partial record {{x.Command.Name}} : IActorCommand;
                    }
                    """);
            }
        });

        //// Find all invocations to ExecuteAsync and SendAsync and get the type of the command
        //// to generate a partial record for it.
        //var nodes = context.SyntaxProvider.CreateSyntaxProvider(
        //    (node, _) => node is InvocationExpressionSyntax invocation &&
        //        (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ExecuteAsync" } ||
        //        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SendAsync" }), 
        //    (ctx, c) =>
        //    {
        //        var invocation = (InvocationExpressionSyntax)ctx.Node;
        //        var arg = invocation.ArgumentList.Arguments.Last();
        //        var operation = ctx.SemanticModel.GetOperation(arg.Expression);

        //        if (operation != null)
        //            return operation.Type;

        //        return null;
        //    });

        //context.RegisterImplementationSourceOutput(nodes, (ctx, node) =>
        //{
        //    if (node == null)
        //        return;

        //    if (!IsPartial(node))
        //    {
        //        ctx.ReportDiagnostic(Diagnostic.Create(
        //            "DCA001", "Design", "Command must be a partial class or record",
        //            DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 0,
        //            location: node.DeclaringSyntaxReferences.First().GetSyntax().GetLocation()));
        //    }

        //    Debugger.Log(0, "SerializableGenerator", $"Found invocation: {node}\n");
        //});
    }
}
