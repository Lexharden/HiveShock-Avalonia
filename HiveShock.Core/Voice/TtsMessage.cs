namespace HiveShock.Voice;

/// <summary>Rol de quien escribe en el live. Se combina (un mod puede ser también suscriptor).</summary>
[Flags]
public enum ChatterRoles
{
    None = 0,
    Broadcaster = 1,
    Moderator = 2,
    Vip = 4,
    Subscriber = 8,
}

public enum TtsMessageKind
{
    Chat,
    Gift,
    Follow,
    Bits,
}

/// <summary>
/// Algo del live camino a la cola de Smart TTS, en crudo: <see cref="TtsMessageFilter"/>
/// decide si se lee y arma la frase final. Para Chat, Text es el comentario; para Gift,
/// el nombre del regalo (Count = cantidad, Diamonds = valor total); para Bits, Count = bits.
/// </summary>
public sealed record TtsMessage(string PortId, string Speaker, string Text, DateTime ReceivedUtc)
{
    public TtsMessageKind Kind { get; init; } = TtsMessageKind.Chat;
    public ChatterRoles Roles { get; init; }

    /// <summary>Id estable del usuario (login/uniqueId) para cooldowns y la lista de ignorados.</summary>
    public string SpeakerKey { get; init; } = "";

    public int Count { get; init; }
    public long Diamonds { get; init; }
}
