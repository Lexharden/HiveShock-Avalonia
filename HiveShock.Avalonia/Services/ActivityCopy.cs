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

        if (m.StartsWith("Chat saturado", StringComparison.Ordinal))
        {
            return "El chat mandó muchos comandos a la vez. Se omiten unos.";
        }

        if (m.StartsWith("Chat ", StringComparison.Ordinal) &&
            m.Contains(" — espera ", StringComparison.Ordinal))
        {
            var rest = m["Chat ".Length..];
            var userEnd = rest.IndexOf(' ');
            var user = userEnd > 0 ? rest[..userEnd] : rest;
            return $"{user} ya usó un comando. Espera un poco.";
        }

        if (m.StartsWith("Chat ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Chat ".Length..], "escribió en el chat");
        }

        if (m.StartsWith("Bits ", StringComparison.Ordinal))
        {
            return FormatArrowLine(m["Bits ".Length..], "mandó bits");
        }

        if (m.StartsWith("Meta ", StringComparison.Ordinal))
        {
            if (m.Contains("->", StringComparison.Ordinal))
            {
                return FormatArrowLine(m["Meta ".Length..], "completó la meta");
            }

            return m["Meta ".Length..].Trim();
        }

        if (m.StartsWith("Capturado ", StringComparison.Ordinal))
        {
            return "Anotamos un regalo nuevo del live.";
        }

        if (m.StartsWith("TikTok conectando", StringComparison.Ordinal))
        {
            return "TikTok: conectando al live…";
        }

        if (m.StartsWith("TikTok conectado", StringComparison.Ordinal))
        {
            return "TikTok: en vivo.";
        }

        if (m.StartsWith("TikTok desconectado", StringComparison.Ordinal) ||
            m.StartsWith("TikTok: ", StringComparison.Ordinal))
        {
            return "TikTok: se cortó. Reintentando…";
        }

        if (m.StartsWith("TikTok reconectando", StringComparison.Ordinal))
        {
            return "TikTok: reconectando…";
        }

        if (m.StartsWith("TikTok live terminado", StringComparison.Ordinal))
        {
            return "TikTok: el live se cerró.";
        }

        if (m.StartsWith("Twitch conectando", StringComparison.Ordinal))
        {
            return "Twitch: conectando…";
        }

        if (m.StartsWith("Twitch conectado", StringComparison.Ordinal) ||
            m.StartsWith("Twitch cuenta", StringComparison.Ordinal))
        {
            return m.StartsWith("Twitch cuenta", StringComparison.Ordinal)
                ? "Twitch: cuenta guardada."
                : "Twitch: en vivo.";
        }

        if (m.StartsWith("Twitch bits:", StringComparison.Ordinal))
        {
            return "Twitch: bits no autorizado. Sal y entra otra vez.";
        }

        if (m.StartsWith("Twitch desconectado", StringComparison.Ordinal) ||
            m.StartsWith("Twitch: ", StringComparison.Ordinal) ||
            m.StartsWith("Twitch token", StringComparison.Ordinal) ||
            m.StartsWith("Twitch follow", StringComparison.Ordinal) ||
            m.StartsWith("Twitch socket", StringComparison.Ordinal))
        {
            return "Twitch: se cortó o falló. Reintentando…";
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
                ? "El personaje cayó. Se restó una vida."
                : "El personaje cayó. El contador subió.";
        }

        if (m.StartsWith("Partida borrada", StringComparison.Ordinal))
        {
            return "La partida se borró en el juego.";
        }

        if (m.StartsWith("Canal guardado", StringComparison.Ordinal))
        {
            return "Usuario de TikTok guardado.";
        }

        if (m.StartsWith("Twitch Client ID", StringComparison.Ordinal))
        {
            return "Twitch: Client ID guardado.";
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
