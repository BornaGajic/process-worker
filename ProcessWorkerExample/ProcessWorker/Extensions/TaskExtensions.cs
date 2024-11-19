namespace ProcessWorker.Extensions;

public static class TaskExtensions
{
    // https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs,7e4e93fae326763f
    /// <summary>
    /// Should only be used in most specific cases, mostly by <see cref="TaskCompletionSource.Task"/>
    /// </summary>
    public static void IgnoreUnobservedExceptions(this Task task)
    {
        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                // https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs,0bdc783f2cd45895,references
                // This will internally mark exception as "handled", it does not do anything on its own other than marking it that way, and preventing UnobservedTaskException.
                // The task will still throw if awaited on.
                var _ = task.Exception;
            }

            return;
        }

        task.ContinueWith(t =>
        {
            var _ = t.Exception;
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}