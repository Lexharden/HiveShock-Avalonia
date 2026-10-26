using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Live;

namespace HiveShock.Overlays;

/// <summary>Aspecto de los overlays tal como lo configura el usuario (colores ya resueltos a #RRGGBB / #AARRGGBB).</summary>
public sealed record OverlayLookData(
    double Scale,
    bool ShowBackground,
    double BackgroundOpacity,
    string CardColor,
    string BorderColor,
    string NumberColor,
    string TitleColor,
    string GiftColor,
    string AccentColor,
    string HighlightColor,
    double NumberSize,
    double TitleSize,
    double GiftNameSize,
    bool AlignCenter,
    string GiftsTitle,
    bool ShowGiftsTitle)
{
    /// <summary>Valores razonables para pruebas y para el primer arranque.</summary>
    public static OverlayLookData Default { get; } = new(
        1.5, true, 0.94, "#0E1520", "#24324A", "#F7F7F7", "#9FB0C8", "#F7F7F7", "#EBA00A", "#3A2F0B",
        72, 16, 14, true, "Regalos", true);
}

/// <summary>Un regalo con su nombre y su imagen del catálogo (para las metas y las alertas).</summary>
public sealed record OverlayGiftRef(string Name, string? ImagePath);

/// <summary>
/// Convierte el estado de la app (contador, regalos, metas, aspecto) en los mensajes que leen las páginas de OBS
/// y los publica en el <see cref="WebOverlayServer"/>. No dibuja nada: solo describe qué hay que enseñar.
/// </summary>
public sealed class OverlayFeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebOverlayServer _server;
    private readonly Func<string, OverlayGiftRef?> _lookup;

    public OverlayFeed(WebOverlayServer server, Func<string, OverlayGiftRef?> lookup)
    {
        _server = server;
        _lookup = lookup;
    }

    public void SetLook(OverlayLookData look) => _server.Publish("look", Serialize(look));

    public void SetCounter(string title, int value) =>
        _server.Publish("counter", Serialize(new { title, value }));

    public void SetGifts(IEnumerable<EditableGift> gifts)
    {
        var items = gifts
            .Where(g => g.Overlay)
            .Select(g => new
            {
                id = g.Id,
                name = g.Gift,
                also = g.Also,
                label = g.OverlayLabel,
                image = _server.RegisterImage(g.ResolvedImagePath),
            })
            .ToList();
        _server.Publish("gifts", Serialize(new { items }));
    }

    public void SetGoals(IEnumerable<GoalSnapshot> goals)
    {
        var items = goals.Select(goal => new
        {
            id = goal.Id,
            label = goal.Label,
            count = goal.Count,
            need = goal.Need,
            closed = goal.Closed,
            repeat = goal.Repeat,
            progress = goal.ProgressText,
            gifts = GiftsOf(goal),
        }).ToList();
        _server.Publish("goals", Serialize(new { items }));
    }

    /// <summary>Resalta un regalo en el overlay de regalos (el mismo guiño que hace la ventana nativa).</summary>
    public void Highlight(string giftName, string? giftId) =>
        _server.Push("hit", Serialize(new { name = giftName, id = giftId ?? "" }));

    /// <summary>Alerta puntual: no se guarda, solo la ven las páginas que estén abiertas en ese momento.</summary>
    public void Alert(string kind, string user, string title, int count, string? imagePath)
    {
        _server.Push("alert", Serialize(new
        {
            kind,
            user,
            title,
            count,
            image = _server.RegisterImage(imagePath),
        }));
    }

    private List<object> GiftsOf(GoalSnapshot goal)
    {
        var list = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in goal.Keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            OverlayGiftRef? info;
            try
            {
                info = _lookup(key);
            }
            catch (Exception)
            {
                info = null;
            }

            // Sin catálogo se enseña al menos el nombre que puso el usuario.
            var shown = info ?? new OverlayGiftRef(key, null);
            if (seen.Add(shown.Name))
            {
                list.Add(new { name = shown.Name, image = _server.RegisterImage(shown.ImagePath) });
            }
        }

        return list;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}
