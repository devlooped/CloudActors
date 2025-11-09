using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

static class EventLocator
{
    public static bool IsActorEvent(INamedTypeSymbol eventType, Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semantic = compilation.GetSemanticModel(tree);
            var walker = new MethodInvocationWalker(semantic);
            walker.Visit(tree.GetRoot());
            if (walker.EventTypes.Contains(eventType))
                return true;
        }
        return false;
    }

    public static HashSet<INamedTypeSymbol> FindRaisedEvents(Compilation compilation, SyntaxTree tree)
    {
        var semantic = compilation.GetSemanticModel(tree);
        var walker = new MethodInvocationWalker(semantic);
        walker.Visit(tree.GetRoot());
        return walker.EventTypes;
    }

    class MethodInvocationWalker(SemanticModel semantic) : CSharpSyntaxWalker
    {
        readonly HashSet<INamedTypeSymbol> events = new(SymbolEqualityComparer.Default);

        public HashSet<INamedTypeSymbol> EventTypes => events;

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (semantic.GetSymbolInfo(node).Symbol is IMethodSymbol method &&
                method.Name == "Raise" &&
                method.IsGenericMethod &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type is INamedTypeSymbol eventType)
            {
                events.Add(eventType);
            }

            base.VisitInvocationExpression(node);
        }
    }
}