namespace HiveShock.Voice;

/// <summary>Un comentario del chat en crudo, camino a la cola de Smart TTS.</summary>
public sealed record TtsMessage(string PortId, string Speaker, string Text, DateTime ReceivedUtc);
