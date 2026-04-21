using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scriban;

namespace Devlooped.CloudActors;

[Generator(LanguageNames.CSharp)]
class StreamstoneCustomStorageGenerator : IIncrementalGenerator
{
    static readonly Template grainTemplate = Template.Parse(ThisAssembly.Resources.StreamstoneCustomStorage.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var actors = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                if (compilation.GetTypeByMetadataName("Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2") is null)
                    return new EquatableArray<ActorModel>(ImmutableArray<ActorModel>.Empty);

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
                        if (model is { } actor && actor.IsJournaled)
                            builder.Add(actor);
                    }
                }

                return new EquatableArray<ActorModel>(builder.ToImmutable());
            })
            .WithTrackingName(TrackingNames.JournaledModels);

        context.RegisterSourceOutput(actors, (ctx, source) =>
        {
            foreach (var actor in source.AsImmutableArray())
            {
                var output = grainTemplate.Render(new
                {
                    actor.Namespace,
                    actor.Name,
                    Version = ThisAssembly.Info.InformationalVersion,
                }, member => member.Name);

                ctx.AddSource($"{actor.FileName}.Streamstone.g.cs", output);
            }
        });
    }
}
