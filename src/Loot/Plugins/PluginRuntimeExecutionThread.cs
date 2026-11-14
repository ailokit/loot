using System.Collections.Concurrent;

namespace Loot.Plugins;

/// <summary>Runs all calls for one QuickJS runtime on its owning OS thread.</summary>
internal sealed class PluginRuntimeExecutionThread
{
    private const int StackSizeBytes = 4 * 1024 * 1024;
    private readonly BlockingCollection<Action> _work = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private Task? _stopTask;

    public PluginRuntimeExecutionThread()
    {
        _thread = new Thread(Run, StackSizeBytes)
        {
            IsBackground = true,
            Name = "Loot plugin QuickJS"
        };
        _thread.Start();
    }

    public Task RunAsync(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopTask is not null)
                return Task.FromException(new ObjectDisposedException(nameof(PluginRuntimeExecutionThread)));

            _work.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        }

        return completion.Task;
    }

    public Task StopAsync(Action finalAction)
    {
        ArgumentNullException.ThrowIfNull(finalAction);
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add(() =>
            {
                try
                {
                    finalAction();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            _work.CompleteAdding();
            _stopTask = JoinAfterAsync(completion.Task);
            return _stopTask;
        }
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable())
        {
            try { work(); }
            catch { /* Work items own and complete their task even when they fail. */ }
        }
    }

    private async Task JoinAfterAsync(Task finalAction)
    {
        try { await finalAction.ConfigureAwait(false); }
        finally
        {
            await Task.Run(_thread.Join).ConfigureAwait(false);
            _work.Dispose();
        }
    }
}
