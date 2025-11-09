using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
class ActorMessageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.GetOrleansOptions();
        var messages = context.CompilationProvider
            .SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(t => t.IsActorMessage())
            .Where(t => t.IsPartial());

        var additionalTypes = messages.SelectMany((x, _) =>
            x.GetMembers().OfType<IPropertySymbol>()
            // Generated serializers only expose public members.
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p => p.Type)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.IsPartial())
            .Concat(x.GetMembers()
            .OfType<IMethodSymbol>()
            // Generated serializers only expose public members.
            .Where(m => m.DeclaredAccessibility == Accessibility.Public)
            .SelectMany(m => m.Parameters)
            .Select(p => p.Type)
            .OfType<INamedTypeSymbol>()))
            // We already generate separately for actor messages.
            .Where(t => !t.IsActorMessage() && t.IsPartial())
            .Collect();

        context.RegisterImplementationSourceOutput(messages.Combine(options), (ctx, source) => ctx.GenerateCode(source));
        context.RegisterImplementationSourceOutput(additionalTypes.Combine(options), (ctx, source) =>
        {
            var (messages, options) = source;
            var distinct = new HashSet<INamedTypeSymbol>(messages, SymbolEqualityComparer.Default);
            foreach (var message in distinct)
                ctx.GenerateCode((message, options));
        });

        context.RegisterImplementationSourceOutput(options.Combine(context.CompilationProvider), (ctx, source) =>
        {
            var (options, compilation) = source;
            if (options.ProduceReferenceAssembly)
                ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoReferenceAssemblies, compilation.Assembly.Locations.FirstOrDefault()));
        });
    }
}