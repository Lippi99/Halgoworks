using Halgoworks.Core;

namespace Halgoworks.Components.Lab;

/// <summary>An algorithm that can join a race.</summary>
public sealed record RaceEntry(string Key, string Name);

/// <summary>One racer's full trace, and how to read a score off any of its steps (null when there's nothing to show yet).</summary>
public sealed record RaceLane<TStep>(string Key, string Name, IReadOnlyList<TStep> Steps, Func<TStep, double?> Score) where TStep : Step;

/// <summary>What a lane shows right now: the step at the shared timeline position, clamped to the lane's own length.</summary>
public sealed record RaceFrame<TStep>(RaceLane<TStep> Lane, TStep Step, int Index, bool Done) where TStep : Step;

/// <summary>An algorithm's outcome in the Scale view.</summary>
/// <param name="Value">The objective: distance, bins, makespan, flow, ...</param>
/// <param name="Solution">Whatever the page needs to draw this result.</param>
/// <param name="History">Optional convergence: current (or average) objective per snapshot.</param>
/// <param name="BestHistory">Optional convergence: best objective so far per snapshot.</param>
public sealed record ScaleResult(double Value, object? Solution, string? Note = null, IReadOnlyList<double>? History = null, IReadOnlyList<double>? BestHistory = null);

/// <summary>An algorithm to run in the Scale view. <paramref name="Run"/> is timed, so it should do only the solving.</summary>
public sealed record ScaleJob(string Name, Func<ScaleResult> Run);

public sealed record ScaleRun(string Name, ScaleResult Result, double Milliseconds);
