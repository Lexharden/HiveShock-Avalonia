using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Hosting;

public enum DeathCounterMode
{
    /// <summary>Empieza en 0 y suma 1 por muerte.</summary>
    CountUp,
    /// <summary>Empieza en StartingValue (vidas) y resta 1 por muerte.</summary>
    Lives,
}

/// <summary>Contador de partida en el bridge (el juego solo avisa “hubo muerte”).</summary>
public sealed class DeathCounter
{
    public DeathCounterMode Mode { get; private set; } = DeathCounterMode.CountUp;
    public int StartingValue { get; private set; }
    public int Value { get; private set; }

    public string Title => Mode == DeathCounterMode.Lives ? "Vidas" : "Muertes";

    public event EventHandler? Changed;

    public void Configure(DeathCounterMode mode, int startingValue, bool resetValue = true)
    {
        Mode = mode;
        StartingValue = mode == DeathCounterMode.Lives
            ? Math.Max(1, startingValue)
            : Math.Max(0, startingValue);

        if (resetValue)
        {
            Reset();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Reset()
    {
        Value = Mode == DeathCounterMode.Lives
            ? Math.Max(1, StartingValue)
            : Math.Max(0, StartingValue);

        Changed?.Invoke(this, EventArgs.Empty);
        BridgeLog.Info($"Contador {Title} = {Value} (modo {(Mode == DeathCounterMode.Lives ? "vidas" : "muertes")})");
    }

    /// <summary>Restaura un valor persistido sin usar StartingValue (salvo clamp).</summary>
    public void Restore(int value)
    {
        if (Mode == DeathCounterMode.Lives)
        {
            var max = Math.Max(StartingValue, value);
            Value = Math.Clamp(value, 0, Math.Max(0, max));
        }
        else
        {
            Value = Math.Max(0, value);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        BridgeLog.Info($"Contador restaurado {Title} = {Value}");
    }

    public void RegisterDeath()
    {
        if (Mode == DeathCounterMode.Lives)
        {
            Value = Math.Max(0, Value - 1);
            BridgeLog.Info($"Muerte → quedan {Value} vidas");
        }
        else
        {
            Value++;
            BridgeLog.Info($"Muerte → total {Value}");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
