using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Devlooped.CloudActors;

static class Telemetry
{
    static readonly ActivitySource tracer = new(nameof(CloudActors), ThisAssembly.Info.Version);
    static Meter Meter { get; } = new Meter(nameof(CloudActors), ThisAssembly.Info.Version);

    static readonly Counter<long> commands;
    static readonly Counter<long> queries;

    /// <summary>Duration of commands executed by the CloudActors bus.</summary>
    public static Histogram<long> Processing { get; } =
        Meter.CreateHistogram<long>("Processing", unit: "ms", description: "Duration of commands executed by the CloudActors bus.");

    /// <summary>Duration of queries send to the CloudActors bus.</summary>
    public static Histogram<long> Sending { get; } =
        Meter.CreateHistogram<long>("Sending", unit: "ms", description: "Duration of queries sent to the CloudActors bus.");

    static Telemetry()
    {
        commands = Meter.CreateCounter<long>("Commands", description: "Commands executed by the CloudActors bus.");
        queries = Meter.CreateCounter<long>("Queries", description: "Queries run by the CloudActors bus.");
    }

    // process: A message that was previously received from a destination is processed by a message consumer/server.
    public const string Process = "process";

    // send: A one-way message (query) is sent to a destination.
    public const string Send = "send";

    public static Activity? StartCommandActivity(object command, string? callerName, string? callerFile, int? callerLine)
        => StartActivity(Process, command, callerName, callerFile, callerLine);

    public static Activity? StartQueryActivity(object query, string? callerName, string? callerFile, int? callerLine)
        => StartActivity(Send, query, callerName, callerFile, callerLine);

    public static Activity? StartActivity(string operation, object command, string? callerName, string? callerFile, int? callerLine)
    {
        ThrowIfNull(command);
        var type = command.GetType();

        if (operation == Send)
            queries.Add(1, new KeyValuePair<string, object?>("Name", type.FullName));
        else if (operation == Process)
            commands.Add(1, new KeyValuePair<string, object?>("Name", type.FullName));

        // Span name convention should be: {messaging.operation.name} {destination} (see https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#span-name)
        // Requirement is that the destination has low cardinality.
        var activity = tracer.CreateActivity($"process {type.FullName}", ActivityKind.Producer)
            ?.SetTag("code.function", callerName)
            ?.SetTag("code.filepath", callerFile)
            ?.SetTag("code.lineno", callerLine)
            ?.SetTag("messaging.system", "cloudactors")
            ?.SetTag("messaging.destination.name", type.FullName)
            ?.SetTag("messaging.destination.kind", "topic")
            ?.SetTag("messaging.operation", "process")
            ?.SetTag("messaging.protocol.name", type.Assembly.GetName().Name)
            ?.SetTag("messaging.protocol.version", type.Assembly.GetName().Version?.ToString() ?? "unknown");

        if (command != null &&
            // Additional optimization so we don't incur allocation of activity custom props storage 
            // unless someone is actually requesting the data. See https://github.com/open-telemetry/opentelemetry-dotnet/issues/1397 
            activity?.IsAllDataRequested == true)
            activity.SetCustomProperty(operation == Send ? "query" : "command", command);

        activity?.Start();

        return activity;
    }

    public static void SetException(this Activity? activity, Exception e)
    {
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, e.Message);

            activity.SetTag("otel.status_code", "ERROR");
            activity.SetTag("otel.status_description", e.Message);

            // See https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/exceptions.md
            activity.AddEvent(new ActivityEvent("exception", tags: new()
            {
                { "exception.message", e.Message },
                { "exception.type", e.GetType().FullName },
                { "exception.stacktrace", e.ToString() },
            }));
        }
    }
}
