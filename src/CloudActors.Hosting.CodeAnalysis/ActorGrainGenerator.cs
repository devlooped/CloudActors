using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scriban;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
class ActorGrainGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.ActorGrain.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var config = context.GetOrleansConfig();

        // Use CompilationProvider to discover actors across all referenced assemblies
        var actors = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var iParsable = compilation.GetTypeByMetadataName("System.IParsable`1");
                var guidType = compilation.GetTypeByMetadataName("System.Guid");
                var hasCreateVersion7 = guidType?.GetMembers("CreateVersion7")
                    .OfType<IMethodSymbol>()
                    .Any(m => m.IsStatic && m.Parameters.Length == 0) == true;

                var builder = ImmutableArray.CreateBuilder<ActorModel>();
                foreach (var type in compilation.GetAllTypes())
                {
                    if (!type.IsGenericType && type.IsActor())
                    {
                        var model = ModelExtractors.ExtractActorModel(type, iParsable, hasCreateVersion7);
                        if (model.HasValue)
                            builder.Add(model.Value);
                    }
                }
                return new EquatableArray<ActorModel>(builder.ToImmutable());
            })
            .WithTrackingName(TrackingNames.GrainModels);

        context.RegisterImplementationSourceOutput(
            actors.Combine(config).Combine(context.CompilationProvider).Combine(context.ParseOptionsProvider),
            (ctx, source) =>
            {
                var (((actorModels, config), compilation), parseOptions) = source;

                foreach (var actor in actorModels.AsImmutableArray())
                {
                    var model = new
                    {
                        actor.Namespace,
                        actor.Name,
                        actor.StateName,
                        actor.StorageProvider,
                        Version = ThisAssembly.Info.InformationalVersion,
                        Queries = actor.Queries.AsImmutableArray().Select(q => new { q.Name, q.Type, q.IsAsync }).ToArray(),
                        Commands = actor.Commands.AsImmutableArray().Select(c => new { c.Name, c.Type, c.IsAsync }).ToArray(),
                        VoidCommands = actor.VoidCommands.AsImmutableArray().Select(v => new { v.Name, v.Type, v.IsAsync }).ToArray(),
                        QueryAsync = actor.Queries.AsImmutableArray().Any(q => q.IsAsync),
                        ExecuteAsync = actor.Commands.AsImmutableArray().Any(),
                        ExecuteVoidAsync = actor.VoidCommands.AsImmutableArray().Any(),
                    };

                    var output = template.Render(model, member => member.Name);
                    var orleans = OrleansGenerator.GenerateCode(compilation, parseOptions as CSharpParseOptions, config, output, actor.Name, ctx.CancellationToken);

                    ctx.AddSource($"{actor.FileName}.cs", output);
                    ctx.AddSource($"{actor.FileName}.orleans.cs", orleans);
                }
            });
    }
}
