using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
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
        var actors = context.CompilationProvider
            .SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(x => x.GetAttributes().Any(x => x.IsActor()));

        context.RegisterImplementationSourceOutput(actors, (ctx, actor) =>
        {
            var ns = actor.ContainingNamespace.ToDisplayString();

            var props = actor.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(x =>
                    x.CanBeReferencedByName &&
                    !x.IsIndexer &&
                    !x.IsAbstract &&
                    x.SetMethod != null)
                .Select(x => new Member(x.Name, x.Type.GetTypeName(ns)));

            var fields = actor.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(x =>
                    x.CanBeReferencedByName &&
                    !x.IsConst &&
                    !x.IsStatic &&
                    !x.IsReadOnly)
                .Select(x => new Member(x.Name, x.Type.GetTypeName(ns)));

            var members = props.Concat(fields).ToArray();
            var es = actor.AllInterfaces.Any(x => x.ToDisplayString(FullName) == "Devlooped.CloudActors.IEventSourced");
            var model = new StateModel(
                Namespace: ns,
                Name: actor.Name,
                Members: members,
                EventSourced: es);

            var output = template.Render(model, member => member.Name);
            ctx.AddSource($"{actor.ToFileName()}.State.cs", output);
        });
    }

    record Member(string Name, string TypeName)
    {
        public string Type => TypeName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Boolean" => "bool",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Decimal" => "decimal",
            "System.DateTime" => "DateTime",
            "System.Guid" => "Guid",
            "System.TimeSpan" => "TimeSpan",
            "System.Byte" => "byte",
            "System.Byte[]" => "byte[]",
            "System.Char" => "char",
            "System.UInt32" => "uint",
            "System.UInt64" => "ulong",
            "System.SByte" => "sbyte",
            "System.UInt16" => "ushort",
            "System.Int16" => "short",
            "System.Object" => "object",
            _ => TypeName
        };
    }

    record StateModel(string Namespace, string Name, IEnumerable<Member> Members, bool EventSourced, string Version = ThisAssembly.Info.InformationalVersion);
}
