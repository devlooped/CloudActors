using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Devlooped.CloudActors.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Devlooped.CloudActors;

public class CodeFixers
{
    [Fact]
    public void GenerateActorState()
    {
        var compilation = CSharpCompilation.Create("TestProject",
            [CSharpSyntaxTree.ParseText(
                /* lang=c#-test */
                """
                using Devlooped.CloudActors;

                [Actor]
                public partial class Account
                {
                  bool isNew = true;
                  public bool IsClosed { get; private set; }
                }
                """)],
            ReferenceAssemblies.Net.Net80
                .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")])
                .Assemblies.Select(x => MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new ActorStateGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        // Run the generator
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

    }

    [Fact]
    public async Task AddPartial()
    {
        var context = new CSharpCodeFixTest<PartialAnalyzer, TypeMustBePartial, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            
            [Actor]
            public class [|MyActor|] { }
            """;

        context.FixedCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            
            [Actor]
            public partial class MyActor { }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task AddPartialMessage()
    {
        var context = new CSharpCodeFixTest<PartialAnalyzer, TypeMustBePartial, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;

            namespace Tests;

            public record {|DCA001:GetBalance|}() : IActorQuery<decimal>;
            """;

        context.FixedCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            
            namespace Tests;

            public partial record GetBalance() : IActorQuery<decimal>;
            """;

        await context.RunAsync();
    }


    [Fact]
    public async Task ReportPartialIndirectMessage()
    {
        // Can't verify the codefix due to being reported for another node.
        var context = new CSharpAnalyzerTest<PartialAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;

            namespace Tests;

            public record {|DCA001:Address|}(string Street, string City, string State, string Zip);

            public partial record SetAddress(Address Address) : IActorCommand;
            """;

        //context.FixedCode =
        //    /* lang=c#-test */
        //    """
        //    using Devlooped.CloudActors;

        //    namespace Tests;

        //    public partial record Address(string Street, string City, string State, string Zip);

        //    public partial record SetAddress(Address Address) : IActorCommand;
        //    """;

        await context.RunAsync();
    }

    [Fact]
    public async Task NoGenerateSerializer()
    {
        var context = new CSharpAnalyzerTest<ActorMessageAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([
                new PackageIdentity("Devlooped.CloudActors", "0.4.0"),
                new PackageIdentity("Microsoft.Orleans.Serialization.Abstractions", "8.2.0"),
            ]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using Orleans;

            namespace Tests;

            [GenerateSerializer]
            public record {|DCA002:GetBalance|}() : IActorQuery<decimal>;
            """;

        await context.RunAsync();
    }

}
