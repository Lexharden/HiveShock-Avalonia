using HiveShock.Input;

namespace HiveShock.Tests.Input;

internal sealed class FakeGameInput : IGameInput
{
    public List<string> Events { get; } = [];
    public WindowStatus Status { get; set; } = WindowStatus.Ready;
    public int Sessions { get; private set; }
    public int Disposed { get; private set; }

    public WindowStatus GetStatus(WindowTarget target) => Status;

    public Task<IInputSession?> BeginAsync(WindowTarget target, CancellationToken ct)
    {
        Sessions++;
        return Task.FromResult<IInputSession?>(new Session(this));
    }

    private sealed class Session(FakeGameInput owner) : IInputSession
    {
        public void Send(KeyCode key, bool down) => owner.Events.Add($"{(down ? "+" : "-")}{key.Name}");

        public void Dispose() => owner.Disposed++;
    }
}
