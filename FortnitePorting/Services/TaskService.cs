using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FortnitePorting.Services;

public static class TaskService
{
    public static event ExceptionDelegate? Exception;
    public delegate void ExceptionDelegate(Exception exception);

    public static Task Run(Func<Task> function)
    {
        return Task.Run(async () =>
        {
            try
            {
                await function().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
            }
        });
    }

    public static async Task RunAsync(Func<Task> function)
    {
        await Task.Run(async () =>
        {
            try
            {
                await function().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
            }
        });
    }

    public static Task Run(Action function)
    {
        return Task.Run(() =>
        {
            try
            {
                function();
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
            }
        });
    }

    public static async Task RunAsync(Action function)
    {
        await Task.Run(() =>
        {
            try
            {
                function();
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
            }
        });
    }

    public static void RunDispatcher(Func<Task> function, DispatcherPriority priority = default)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await function();
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
            }
        }, priority);
    }

    public static async Task RunDispatcherAsync(Func<Task> function, DispatcherPriority priority = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await function();
                completion.TrySetResult();
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
                completion.TrySetResult();
            }
        }, priority);

        await completion.Task;
    }

    public static void RunDispatcher(Action function, DispatcherPriority priority = default)
    {
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                try
                {
                    function();
                }
                catch (Exception e)
                {
                    Exception?.Invoke(e);
                }
            }, priority);
        }
        catch (Exception e)
        {
            Exception?.Invoke(e);
        }
    }

    public static void PostDispatcher(Action function, DispatcherPriority priority = default)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                function();
            }
            catch (Exception e)
            {
                Exception?.Invoke(e);
            }
        }, priority);
    }

    public static async Task RunDispatcherAsync(Action function, DispatcherPriority priority = default)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    function();
                }
                catch (Exception e)
                {
                    Exception?.Invoke(e);
                }
            }, priority);
        }
        catch (Exception e)
        {
            Exception?.Invoke(e);
        }
    }
}

public static class TaskExtensions
{
    extension(Task task)
    {
        public void RunAsynchronously()
        {
            task.Start();
        }
    }
}
