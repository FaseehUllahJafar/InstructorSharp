using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InstructorSharp.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation. Subscribe to the source and meter named here to see how
/// often the repair loop is actually firing in production.
/// </summary>
/// <remarks>
/// The attempt histogram is the number worth alerting on: a service whose extractions usually
/// succeed on the first attempt and starts averaging two has had a model or prompt regression,
/// and that shows up here long before it shows up as an error rate.
/// </remarks>
public static class InstructorDiagnostics
{
    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource"/> this library emits spans on.</summary>
    public const string ActivitySourceName = "InstructorSharp";

    /// <summary>Name of the <see cref="System.Diagnostics.Metrics.Meter"/> this library emits metrics on.</summary>
    public const string MeterName = "InstructorSharp";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, ThisAssemblyVersion);

    private static readonly Meter Meter = new(MeterName, ThisAssemblyVersion);

    private static readonly Counter<long> Extractions =
        Meter.CreateCounter<long>("instructorsharp.extractions", "{extraction}", "Completed extractions.");

    private static readonly Histogram<int> AttemptsPerExtraction =
        Meter.CreateHistogram<int>("instructorsharp.attempts", "{attempt}", "Model calls per extraction.");

    private static string ThisAssemblyVersion =>
        typeof(InstructorDiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    internal static Activity? StartExtraction(Type targetType, ExtractionMode mode)
    {
        Activity? activity = ActivitySource.StartActivity("instructorsharp.extract", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("instructorsharp.target_type", targetType.FullName);
            activity.SetTag("instructorsharp.mode", mode.ToString());
        }

        return activity;
    }

    internal static void RecordCancelled(Activity? activity, int attempts)
    {
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("instructorsharp.attempts", attempts);
            activity.SetTag("instructorsharp.succeeded", false);
            activity.SetStatus(ActivityStatusCode.Error, "cancelled");
        }

        var outcome = new KeyValuePair<string, object?>("outcome", "cancelled");
        Extractions.Add(1, outcome);
        AttemptsPerExtraction.Record(attempts, outcome);
    }

    internal static void RecordOutcome(Activity? activity, bool succeeded, int attempts)
    {
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("instructorsharp.attempts", attempts);
            activity.SetTag("instructorsharp.succeeded", succeeded);
            activity.SetStatus(succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var outcome = new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "failure");
        Extractions.Add(1, outcome);
        AttemptsPerExtraction.Record(attempts, outcome);
    }
}
