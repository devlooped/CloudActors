using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Scriban;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

/// <summary>
/// A source generator that creates the implementation of actor state 
/// retrieval and restoring (memento pattern).
/// </summary>
[Generator(LanguageNames.CSharp)]
class ActorStateGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.ActorState.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var config = context.GetOrleansConfig();

        var actors = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) =>
                node is ClassDeclarationSyntax cds &&
                cds.AttributeLists.Count > 0,
            transform: static (ctx, ct) =>
            {
                if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol symbol)
                    return null;

                if (!symbol.IsActor())
                    return null;

                var iParsable = ctx.SemanticModel.Compilation.GetTypeByMetadataName("System.IParsable`1");
                var guidType = ctx.SemanticModel.Compilation.GetTypeByMetadataName("System.Guid");
                var hasCreateVersion7 = guidType?.GetMembers("CreateVersion7")
                    .OfType<IMethodSymbol>()
                    .Any(m => m.IsStatic && m.Parameters.Length == 0) == true;

                return ModelExtractors.ExtractActorModel(symbol, iParsable, hasCreateVersion7, ctx.SemanticModel.Compilation);
            })
            .Where(static x => x != null)
            .Select(static (x, _) => x!.Value)
            .WithTrackingName(TrackingNames.StateModels);

        context.RegisterImplementationSourceOutput(actors, (ctx, actor) =>
        {
            var members = actor.Properties.AsImmutableArray()
                .AddRange(actor.Fields.AsImmutableArray())
                .Select(m => new { m.Name, m.Type })
                .ToArray();

            var model = new
            {
                actor.Namespace,
                actor.Name,
                Members = members,
                actor.IsEventSourced,
                Version = ThisAssembly.Info.InformationalVersion
            };

            var output = template.Render(model, member => member.Name);
            ctx.AddSource($"{actor.FileName}.State.cs", output);
        });

        // Generate [GenerateSerializer] for partial user-defined types found in actor state
        var stateTypes = actors
            .SelectMany(static (actor, _) => actor.StateTypes.AsImmutableArray())
            .Collect()
            .SelectMany(static (types, _) => types.Distinct().ToImmutableArray())
            .WithTrackingName(TrackingNames.StateSerializableTypes);

        context.RegisterImplementationSourceOutput(
            stateTypes.Combine(config).Combine(context.CompilationProvider).Combine(context.ParseOptionsProvider),
            (ctx, source) =>
            {
                var (((type, config), compilation), parseOptions) = source;

                if (config.ProduceReferenceAssembly)
                    return;

                ctx.GenerateCode(type.Name, type.Namespace, type.FullName, type.IsRecord,
                    config, compilation, parseOptions as CSharpParseOptions);
            });
    }
}
