namespace HiveShock.Input;

/// <summary>
/// Entrada hacia la ventana del juego. <see cref="BeginAsync"/> trae el juego al frente y la sesión devuelta
/// restaura la ventana anterior al desecharse.
/// </summary>
public interface IGameInput
{
    WindowStatus GetStatus(WindowTarget target);

    /// <summary>Null si no se pudo poner el juego en primer plano.</summary>
    Task<IInputSession?> BeginAsync(WindowTarget target, CancellationToken ct);
}

public interface IInputSession : IDisposable
{
    void Send(KeyCode key, bool down);
}

/// <summary>Plataformas sin soporte (Android, Linux, macOS): nunca pulsa nada.</summary>
public sealed class NullGameInput : IGameInput
{
    public WindowStatus GetStatus(WindowTarget target) => WindowStatus.Unsupported;

    public Task<IInputSession?> BeginAsync(WindowTarget target, CancellationToken ct) =>
        Task.FromResult<IInputSession?>(null);
}
