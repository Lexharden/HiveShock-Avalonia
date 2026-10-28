namespace HiveShock.Logging;

public static class TaskExtensions
{
    /// <summary>Lanza una tarea sin esperarla pero registrando cualquier fallo en vez de perderlo. La cancelación se ignora.</summary>
    public static void FireAndLog(this Task task, string what)
    {
        _ = ObserveAsync(task, what);
    }

    public static void FireAndLog(this ValueTask task, string what) => task.AsTask().FireAndLog(what);

    private static async Task ObserveAsync(Task task, string what)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"{what}: {ex.Message}");
        }
    }
}
