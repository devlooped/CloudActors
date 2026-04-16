using System.Linq;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Devlooped.CloudActors.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Tests;

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
    public async Task AddPartialEvent()
    {
        var context = new CSharpCodeFixTest<PartialAnalyzer, TypeMustBePartial, DefaultVerifier>();
        context.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            
            public record [|Deposited|](decimal Amount);

            [Actor]
            public partial class MyActor 
            { 
                public void Deposit(decimal amount)
                {
                    Raise(new Deposited(amount));
                }

                void Raise<T>(T @event) where T : notnull { }
            }
            """;

        context.FixedCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            
            public partial record Deposited(decimal Amount);

            [Actor]
            public partial class MyActor 
            { 
                public void Deposit(decimal amount)
                {
                    Raise(new Deposited(amount));
                }

                void Raise<T>(T @event) where T : notnull { }
            }
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

    [Fact]
    public async Task ReportNonSerializableStateType()
    {
        var context = new CSharpAnalyzerTest<ActorStateSerializableAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            public record {|DCA005:Transaction|}(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public decimal Balance { get; private set; }
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task ReportNestedNonSerializableStateType()
    {
        var context = new CSharpAnalyzerTest<ActorStateSerializableAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            public record {|DCA005:Money|}(decimal Amount, string Currency);
            public record {|DCA005:Transaction|}(string Type, Money Value, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task NoReportOnPartialStateType()
    {
        var context = new CSharpAnalyzerTest<ActorStateSerializableAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            public partial record Transaction(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task NoReportOnGenerateSerializerStateType()
    {
        var context = new CSharpAnalyzerTest<ActorStateSerializableAnalyzer, DefaultVerifier>();
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
            using System;
            using System.Collections.Generic;

            namespace Tests;

            [GenerateSerializer]
            public record Transaction(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task NoReportOnPrimitiveAndSystemTypes()
    {
        var context = new CSharpAnalyzerTest<ActorStateSerializableAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            [Actor]
            public partial class Account
            {
                public string Name { get; private set; }
                public int Balance { get; private set; }
                public decimal Amount { get; private set; }
                public DateTimeOffset Created { get; private set; }
                public List<string> Tags { get; private set; } = new();
                public Dictionary<string, int> Metadata { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task NoReportOnEnum()
    {
        var context = new CSharpAnalyzerTest<ActorStateSerializableAnalyzer, DefaultVerifier>();
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;

            namespace Tests;

            public enum AccountStatus { Active, Closed }

            [Actor]
            public partial class Account
            {
                public AccountStatus Status { get; private set; }
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task FixStateTypeAddPartial()
    {
        var context = new CSharpCodeFixTest<ActorStateSerializableAnalyzer, TypeMustBeSerializable, DefaultVerifier>();
        context.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);
        context.CodeActionIndex = 0; // First fix: Add partial modifier

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            public record {|DCA005:Transaction|}(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        context.FixedCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            public partial record Transaction(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

    [Fact]
    public async Task FixStateTypeAddGenerateSerializer()
    {
        var context = new CSharpCodeFixTest<ActorStateSerializableAnalyzer, TypeMustBeSerializable, DefaultVerifier>();
        context.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;
        context.ReferenceAssemblies = ReferenceAssemblies.Net.Net80
            .AddPackages([new PackageIdentity("Devlooped.CloudActors", "0.4.0")]);
        context.CodeActionIndex = 1; // Second fix: Add [GenerateSerializer] attribute

        context.TestCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            public record {|DCA005:Transaction|}(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        context.FixedCode =
            /* lang=c#-test */
            """
            using Devlooped.CloudActors;
            using System;
            using System.Collections.Generic;

            namespace Tests;

            [Orleans.GenerateSerializer]
            public record Transaction(string Type, decimal Amount, DateTimeOffset Timestamp);

            [Actor]
            public partial class Wallet
            {
                public List<Transaction> Transactions { get; private set; } = new();
            }
            """;

        await context.RunAsync();
    }

}
