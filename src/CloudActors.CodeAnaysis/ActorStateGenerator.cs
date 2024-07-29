using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scriban;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

/// <summary>
/// A source generator that creates the implementation of actor state 
/// retrieval and restoring (memento pattern).
/// </summary>
[Generator(LanguageNames.CSharp)]
public class ActorStateGenerator : IIncrementalGenerator
{
    static readonly Template template = Template.Parse(ThisAssembly.Resources.ActorState.Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.GetOrleansOptions();

        var actors = context.CompilationProvider
            .SelectMany((x, _) => x.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
            .Where(x => x.GetAttributes().Any(IsActor));

        context.RegisterImplementationSourceOutput(actors.Combine(options), (ctx, source) =>
        {
            var (actor, options) = source;
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
            if (members.Length > 0)
            {
                var es = actor.AllInterfaces.Any(x => x.ToDisplayString(FullName) == "Devlooped.CloudActors.IEventSourced");

                var model = new StateModel(
                    Namespace: ns,
                    Name: actor.Name,
                    Members: members,
                    EventSourced: es);

                var output = template.Render(model, member => member.Name);
                var orleans = OrleansGenerator.GenerateCode(options, output, $"{actor.Name}.State", ctx.CancellationToken);

                ctx.AddSource($"{actor.ToFileName()}.State.cs", output);
                ctx.AddSource($"{actor.ToFileName()}.State.orleans.cs", orleans);
            }
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
