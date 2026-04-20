using System;
using System.Threading.Tasks;

namespace Devlooped.CloudActors;

/// <summary>
/// A single-threaded background worker. Multiple <see cref="Notify"/> calls collapse into at most one
/// follow-up <c>Work</c> cycle. Modeled on Orleans' <c>BatchWorker</c> (<c>src/Orleans.Core/Async/BatchWorker.cs</c>):
/// the work cycle runs on the <see cref="TaskScheduler"/> active when <see cref="Notify"/> is first called
/// (the grain scheduler when invoked from grain code), so the queue and per-grain state need no locking.
/// </summary>
abstract class BatchWorker
{
    readonly object gate = new();
    Task? currentWorkCycle;
    bool moreWork;
    TaskCompletionSource<Task>? signal;
    Task? taskToSignal;
    TaskScheduler? scheduler;

    /// <summary>Override to perform a single work cycle. Will be called serially.</summary>
    protected abstract Task Work();

    /// <summary>Notifies the worker that there is work to do.</summary>
    public void Notify()
    {
        lock (gate)
        {
            if (currentWorkCycle != null)
            {
                moreWork = true;
            }
            else
            {
                Start();
            }
        }
    }

    /// <summary>
    /// Returns a task that completes when the work cycle that will service the most-recent <see cref="Notify"/>
    /// finishes (success or failure). If a new cycle is already in flight, awaits it; otherwise awaits the next one.
    /// </summary>
    public Task WaitForCurrentWorkToBeServiced()
    {
        lock (gate)
        {
            if (currentWorkCycle == null)
            {
                if (!moreWork)
                    return Task.CompletedTask;

                Start();
                return currentWorkCycle!;
            }

            if (moreWork)
            {
                signal ??= new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                return signal.Task.Unwrap();
            }

            return currentWorkCycle;
        }
    }

    Task Start()
    {
        // Capture the scheduler from the first Notify so subsequent cycles run on the grain scheduler.
        scheduler ??= TaskScheduler.Current;

        var task = Task.Factory.StartNew(
            static s => ((BatchWorker)s!).Work(),
            this,
            default,
            TaskCreationOptions.None,
            scheduler).Unwrap();

        currentWorkCycle = task;
        task.ContinueWith(static (_, s) => ((BatchWorker)s!).CheckForMoreWork(), this,
            default, TaskContinuationOptions.ExecuteSynchronously, scheduler);
        return task;
    }

    void CheckForMoreWork()
    {
        TaskCompletionSource<Task>? toSignal = null;
        lock (gate)
        {
            if (moreWork)
            {
                moreWork = false;
                taskToSignal = Start();
            }
            else
            {
                currentWorkCycle = null;
                taskToSignal = Task.CompletedTask;
            }

            if (signal != null)
            {
                toSignal = signal;
                signal = null;
            }
        }

        toSignal?.TrySetResult(taskToSignal!);
    }
}

/// <summary>A <see cref="BatchWorker"/> that delegates the work cycle to a function.</summary>
sealed class BatchWorkerFromDelegate(Func<Task> work) : BatchWorker
{
    protected override Task Work() => work();
}
