using HiveShock.Logging;

namespace HiveShock.Avalonia.Services;

public static class ActivityCopy
{
    public static string? ToViewerLine(LogEntry entry)
    {
        var m = entry.Message;

        if (m.StartsWith("Regalo ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Regalo ".Length..], "envió");
        }

        if (m.StartsWith("Likes ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Likes ".Length..], "mandó likes");
        }

        if (m.StartsWith("Follow ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Follow ".Length..], "empezó a seguirte");
        }

        if (m.StartsWith("Share ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Share ".Length..], "compartió el live");
        }

        if (m.StartsWith("Chat ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Chat ".Length..], "escribió en el chat");
        }

        if (m.StartsWith("Capturado ", StringComparison.Ordinal))
        {
            return "Anotamos un regalo nuevo del live.";
        }

        if (m.StartsWith("TikTok conectando", StringComparison.Ordinal))
        {
            return "Conectando al live…";
        }

        if (m.StartsWith("TikTok conectado", StringComparison.Ordinal))
        {
            return "¡Conectado! Ya estás en vivo.";
        }

        if (m.StartsWith("TikTok desconectado", StringComparison.Ordinal) ||
            m.StartsWith("TikTok: ", StringComparison.Ordinal))
        {
            return "Se cortó el live. Reintentando…";
        }

        if (m.StartsWith("TikTok reconectando", StringComparison.Ordinal))
        {
            return "Reconectando al live…";
        }

        if (m.StartsWith("TikTok live terminado", StringComparison.Ordinal))
        {
            return "El live se cerró.";
        }

        if (m.StartsWith("Pruebas escuchando", StringComparison.Ordinal) ||
            m.StartsWith("Modo pruebas:", StringComparison.Ordinal))
        {
            return "Modo pruebas listo.";
        }

        if (m.StartsWith("Captura de regalos", StringComparison.Ordinal))
        {
            return "Anotando regalos del live…";
        }

        if (m.StartsWith("Inicio mode=", StringComparison.Ordinal))
        {
            return null;
        }

        if (m.Equals("Detenido.", StringComparison.Ordinal))
        {
            return "Desconectado.";
        }

        if (m.StartsWith("Prueba ", StringComparison.Ordinal))
        {
            return "Mandamos una prueba al juego.";
        }

        if (m.StartsWith("OK prueba", StringComparison.Ordinal))
        {
            return "El juego recibió la prueba.";
        }

        if (m.StartsWith("Muerte", StringComparison.Ordinal))
        {
            return m.Contains("vidas", StringComparison.OrdinalIgnoreCase)
                ? "Link cayó. Se restó una vida."
                : "Link cayó. El contador subió.";
        }

        if (m.StartsWith("Partida borrada", StringComparison.Ordinal))
        {
            return "La partida se borró en el juego.";
        }

        if (m.StartsWith("Canal guardado", StringComparison.Ordinal))
        {
            return "Usuario de TikTok guardado.";
        }

        if (m.StartsWith("gifts.json guardado", StringComparison.Ordinal) ||
            m.StartsWith("Regalos guardados", StringComparison.Ordinal))
        {
            return "Cambios de regalos guardados.";
        }

        if (m.StartsWith("Perfil cambiado", StringComparison.Ordinal) ||
            m.StartsWith("Perfil activo", StringComparison.Ordinal) ||
            m.StartsWith("Perfil UI", StringComparison.Ordinal))
        {
            return "Juego activo actualizado.";
        }

        if (m.StartsWith("Sin mapeo:", StringComparison.Ordinal))
        {
            return "Llegó un regalo que aún no tiene efecto. Revisa Regalos o anótalo en Vistos.";
        }

        return null;
    }

    private static string FormatArrowLine(string rest, string verb)
    {
        var arrow = rest.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0)
        {
            return rest.Trim();
        }

        var left = rest[..arrow].Trim();
        var right = rest[(arrow + 2)..].Trim();
        var effect = right;
        var paren = right.IndexOf('(');
        if (paren > 0)
        {
            effect = right[..paren].Trim();
        }

        var userEnd = left.IndexOf(' ');
        var user = userEnd > 0 ? left[..userEnd] : left;
        return $"{user} {verb} → {effect}";
    }
}
