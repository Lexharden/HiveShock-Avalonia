namespace HiveShock.Tests.Input;

internal static class Wait
{
    /// <summary>
    /// Espera a que se cumpla la condición (hasta el límite) en vez de dormir un tiempo fijo: un runner de CI
    /// lento no debe volver frágil una prueba que solo necesita que el hilo del runner llegue a procesar.
    /// </summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < until)
        {
            await Task.Delay(10);
        }
    }
}
