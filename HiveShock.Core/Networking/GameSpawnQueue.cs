using System.Text.Json;

namespace HiveShock.Networking;

/// <summary>
/// Estado de la cola de enemigos del juego (evento <c>spawn_queue</c> de HiveShock en Shipwright).
/// Los enemigos que no caben se quedan esperando y entran cuando mueren otros: nada se descarta.
/// </summary>
/// <param name="Pending">Enemigos esperando turno.</param>
/// <param name="Active">Enemigos vivos creados por HiveShock.</param>
/// <param name="Load">Carga actual (suma del peso de los vivos).</param>
/// <param name="Max">Carga máxima permitida.</param>
public readonly record struct GameSpawnQueue(int Pending, int Active, int Load, int Max)
{
    public static GameSpawnQueue Empty => new(0, 0, 0, 0);

    public static bool TryParse(JsonElement root, out GameSpawnQueue queue)
    {
        queue = Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        queue = new GameSpawnQueue(
            ReadInt(root, "pending"),
            ReadInt(root, "active"),
            ReadInt(root, "load"),
            ReadInt(root, "max"));
        return true;
    }

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;
}
