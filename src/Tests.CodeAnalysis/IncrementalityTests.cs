using System.Linq;
using Devlooped.CloudActors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Devlooped.CloudActors.IncrementalityTestHelpers;

namespace Tests;

/// <summary>
/// Tests verifying the incremental behavior of CloudActors source generators.
/// Following the patterns established by dotnet/runtime and StructId.
/// </summary>
public class IncrementalityTests
{
    const string SimpleActor = """
        using System.Runtime.Serialization;
        using Devlooped.CloudActors;
        
        [Actor]
        public partial class Account(string id)
        {
            [IgnoreDataMember]
            public string Id => id;
            public decimal Balance { get; set; }
        }
        """;

    const string ActorWithCommand = """
        using System.Runtime.Serialization;
        using Devlooped.CloudActors;
        
        [Actor]
        public partial class Customer(string id)
        {
            [IgnoreDataMember]
            public string Id => id;
            public string? Name { get; set; }

            public void Execute(Rename command) => Name = command.NewName;
        }

        public partial record Rename(string NewName) : IActorCommand;
        """;

    const string ActorWithPrimitiveId = """
        using System.Runtime.Serialization;
        using Devlooped.CloudActors;
        
        [Actor]
        public partial class Order(long id)
        {
            [IgnoreDataMember]
            public long Id => id;
            public decimal Total { get; set; }
        }
        """;

    const string UnrelatedClass = """
        public class UnrelatedService { public int DoWork() => 42; }
        """;

    #region ActorStateGenerator

    [Fact]
    public void ActorStateGenerator_SameInput_AllCached()
    {
        var compilation = CreateCompilation(SimpleActor);
        var generator = new ActorStateGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);
        var result1 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result1, IncrementalStepRunReason.New,
            TrackingNames.StateModels);

        driver = driver.RunGenerators(compilation);
        var result2 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result2, IncrementalStepRunReason.Cached,
            TrackingNames.StateModels);
    }

    [Fact]
    public void ActorStateGenerator_UnrelatedChange_OutputsCached()
    {
        var compilation = CreateCompilation(SimpleActor);
        var generator = new ActorStateGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);

        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(UnrelatedClass,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: "Unrelated.cs"));

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];
        AssertStepOutputReason(result, TrackingNames.StateModels, IncrementalStepRunReason.Cached);
    }

    #endregion

    #region ActorBusOverloadGenerator

    [Fact]
    public void ActorBusOverloadGenerator_SameInput_AllCached()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var generator = new ActorBusOverloadGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);
        var result1 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result1, IncrementalStepRunReason.New,
            TrackingNames.BusOverloads);

        driver = driver.RunGenerators(compilation);
        var result2 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result2, IncrementalStepRunReason.Cached,
            TrackingNames.BusOverloads);
    }

    [Fact]
    public void ActorBusOverloadGenerator_UnrelatedChange_OutputsCached()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var generator = new ActorBusOverloadGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);

        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(UnrelatedClass,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: "Unrelated.cs"));

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];
        AssertStepOutputReason(result, TrackingNames.BusOverloads, IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void ActorBusOverloadGenerator_FirstActorAdded_EmitsBaseClass()
    {
        var compilation = CreateCompilation("public class Foo {}");
        var driver = CreateDriver(new ActorBusOverloadGenerator());

        driver = driver.RunGenerators(compilation);
        Assert.Empty(driver.GetRunResult().Results[0].GeneratedSources);

        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(ActorWithCommand,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: "Customer.cs"));

        driver = driver.RunGenerators(compilation);
        var sources = driver.GetRunResult().Results[0].GeneratedSources;

        Assert.Contains(sources, s => s.HintName == "ActorBusExtensions.g.cs");
        Assert.Contains(sources, s => s.HintName == "Customer.Bus.g.cs");
    }

    [Fact]
    public void ActorBusOverloadGenerator_LastActorRemoved_RemovesBaseClass()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var driver = CreateDriver(new ActorBusOverloadGenerator());

        driver = driver.RunGenerators(compilation);
        var sourcesBefore = driver.GetRunResult().Results[0].GeneratedSources;
        Assert.Contains(sourcesBefore, s => s.HintName == "ActorBusExtensions.g.cs");

        var actorTree = compilation.SyntaxTrees.First(t => t.GetText().ToString().Contains("Customer"));
        compilation = compilation.ReplaceSyntaxTree(actorTree,
            CSharpSyntaxTree.ParseText("public class Foo {}",
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: actorTree.FilePath));

        driver = driver.RunGenerators(compilation);
        var sourcesAfter = driver.GetRunResult().Results[0].GeneratedSources;

        Assert.Empty(sourcesAfter);
    }

    #endregion

    #region ActorMessageGenerator

    [Fact]
    public void ActorMessageGenerator_SameInput_AllCached()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var generator = new ActorMessageGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);
        var result1 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result1, IncrementalStepRunReason.New,
            TrackingNames.MessageModels);

        driver = driver.RunGenerators(compilation);
        var result2 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result2, IncrementalStepRunReason.Cached,
            TrackingNames.MessageModels);
    }

    [Fact]
    public void ActorMessageGenerator_UnrelatedChange_OutputsCached()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var generator = new ActorMessageGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);

        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(UnrelatedClass,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: "Unrelated.cs"));

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];
        AssertStepOutputReason(result, TrackingNames.MessageModels, IncrementalStepRunReason.Cached);
    }

    #endregion

    #region ActorPrimitiveIdGenerator

    [Fact]
    public void ActorPrimitiveIdGenerator_SameInput_AllCached()
    {
        var compilation = CreateCompilation(ActorWithPrimitiveId);
        var generator = new ActorPrimitiveIdGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);
        var result1 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result1, IncrementalStepRunReason.New,
            TrackingNames.PrimitiveIdModels);

        driver = driver.RunGenerators(compilation);
        var result2 = driver.GetRunResult().Results[0];
        AssertAllOutputReasons(result2, IncrementalStepRunReason.Cached,
            TrackingNames.PrimitiveIdModels);
    }

    [Fact]
    public void ActorPrimitiveIdGenerator_UnrelatedChange_OutputsCached()
    {
        var compilation = CreateCompilation(ActorWithPrimitiveId);
        var generator = new ActorPrimitiveIdGenerator();
        var driver = CreateDriver(generator);

        driver = driver.RunGenerators(compilation);

        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(UnrelatedClass,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: "Unrelated.cs"));

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];
        AssertStepOutputReason(result, TrackingNames.PrimitiveIdModels, IncrementalStepRunReason.Cached);
    }

    #endregion

    #region Pipeline Model Purity

    [Fact]
    public void ActorStateGenerator_PipelineModels_NoSymbolsOrCompilation()
    {
        var compilation = CreateCompilation(SimpleActor);
        var driver = CreateDriver(new ActorStateGenerator());

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];

        AssertTrackedStepValuesClean(result, TrackingNames.StateModels);
    }

    [Fact]
    public void ActorBusOverloadGenerator_PipelineModels_NoSymbolsOrCompilation()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var driver = CreateDriver(new ActorBusOverloadGenerator());

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];

        AssertTrackedStepValuesClean(result, TrackingNames.BusOverloads);
    }

    [Fact]
    public void ActorMessageGenerator_PipelineModels_NoSymbolsOrCompilation()
    {
        var compilation = CreateCompilation(ActorWithCommand);
        var driver = CreateDriver(new ActorMessageGenerator());

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];

        AssertTrackedStepValuesClean(result, TrackingNames.MessageModels);
    }

    [Fact]
    public void ActorPrimitiveIdGenerator_PipelineModels_NoSymbolsOrCompilation()
    {
        var compilation = CreateCompilation(ActorWithPrimitiveId);
        var driver = CreateDriver(new ActorPrimitiveIdGenerator());

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];

        AssertTrackedStepValuesClean(result, TrackingNames.PrimitiveIdModels);
    }

    static void AssertTrackedStepValuesClean(GeneratorRunResult result, string stepName)
    {
        if (result.TrackedSteps.TryGetValue(stepName, out var steps))
        {
            foreach (var step in steps)
            {
                foreach (var output in step.Outputs)
                {
                    AssertNoSymbolsOrCompilation(output.Value);
                }
            }
        }
    }

    #endregion

    #region Whitespace Resilience

    [Fact]
    public void ActorStateGenerator_WhitespaceChange_OutputUnchangedOrCached()
    {
        var compilation = CreateCompilation(SimpleActor);
        var driver = CreateDriver(new ActorStateGenerator());

        driver = driver.RunGenerators(compilation);

        var tree = compilation.SyntaxTrees.First(t =>
            t.GetText().ToString().Contains("Account"));
        var newTree = CSharpSyntaxTree.ParseText(
            SimpleActor + "\n\n// comment\n",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
            path: "Source0.cs");
        compilation = compilation.ReplaceSyntaxTree(tree, newTree);

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results[0];

        var reasons = GetStepOutputReasons(result, TrackingNames.StateModels);
        Assert.NotEmpty(reasons);
        Assert.All(reasons, r => Assert.True(
            r == IncrementalStepRunReason.Unchanged || r == IncrementalStepRunReason.Cached,
            $"Expected Unchanged or Cached, got {r}"));
    }

    #endregion

    #region No Actors = No Output

    [Fact]
    public void ActorStateGenerator_NoActors_NoOutput()
    {
        var compilation = CreateCompilation("public class Foo {}");
        var driver = CreateDriver(new ActorStateGenerator());

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        Assert.Empty(result.Results[0].GeneratedSources);
    }

    [Fact]
    public void ActorBusOverloadGenerator_NoHandlers_NoOutput()
    {
        var compilation = CreateCompilation("public class Foo {}");
        var driver = CreateDriver(new ActorBusOverloadGenerator());

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        Assert.Empty(result.Results[0].GeneratedSources);
    }

    #endregion

    #region New Actor Produces Additional Output

    [Fact]
    public void ActorStateGenerator_NewActor_ProducesAdditionalOutput()
    {
        var compilation = CreateCompilation(SimpleActor);
        var driver = CreateDriver(new ActorStateGenerator());

        driver = driver.RunGenerators(compilation);
        var outputCount1 = driver.GetRunResult().GeneratedTrees.Length;

        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(ActorWithPrimitiveId,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: "Order.cs"));

        driver = driver.RunGenerators(compilation);
        var outputCount2 = driver.GetRunResult().GeneratedTrees.Length;

        Assert.True(outputCount2 > outputCount1,
            $"Expected more outputs after adding actor. Before: {outputCount1}, After: {outputCount2}");
    }

    #endregion
}
