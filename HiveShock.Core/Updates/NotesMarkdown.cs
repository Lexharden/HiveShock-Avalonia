namespace HiveShock.Updates;

public enum NotesBlockKind
{
    Heading,
    Bullet,
    Quote,
    Paragraph,
}

/// <summary>Un trozo de texto; <see cref="Bold"/> si iba entre <c>**…**</c>.</summary>
public sealed record NotesSpan(string Text, bool Bold);

/// <summary>Un bloque de las notas: título, viñeta, cita o párrafo, con sus trozos en negrita o no.</summary>
public sealed record NotesBlock(NotesBlockKind Kind, IReadOnlyList<NotesSpan> Spans)
{
    public string PlainText => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>
/// Markdown mínimo de las notas de versión (CHANGELOG y releases de GitHub): títulos (#…), viñetas (* o -),
/// citas (&gt;), párrafos y negrita. Lo demás se deja como texto: nunca se pierde contenido.
/// </summary>
public static class NotesMarkdown
{
    public static IReadOnlyList<NotesBlock> Parse(string? markdown)
    {
        var blocks = new List<NotesBlock>();
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new NotesBlock(NotesBlockKind.Paragraph, Spans(string.Join(' ', paragraph))));
                paragraph.Clear();
            }
        }

        foreach (var raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (line.StartsWith('#'))
            {
                FlushParagraph();
                blocks.Add(new NotesBlock(NotesBlockKind.Heading, Spans(line.TrimStart('#').Trim())));
            }
            else if (line.Length > 1 && line[0] is '*' or '-' && line[1] == ' ')
            {
                FlushParagraph();
                blocks.Add(new NotesBlock(NotesBlockKind.Bullet, Spans(line[2..].Trim())));
            }
            else if (line.StartsWith('>'))
            {
                FlushParagraph();
                blocks.Add(new NotesBlock(NotesBlockKind.Quote, Spans(line.TrimStart('>').Trim())));
            }
            else if (blocks.Count > 0 && blocks[^1].Kind == NotesBlockKind.Bullet && raw.StartsWith("  ", StringComparison.Ordinal))
            {
                // Continuación con sangría de la viñeta anterior.
                var last = blocks[^1];
                blocks[^1] = last with { Spans = [.. last.Spans, new NotesSpan(" ", false), .. Spans(line)] };
            }
            else
            {
                paragraph.Add(line);
            }
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>Parte el texto en trozos normales y en negrita (<c>**así**</c>); quita el `código` dejando su texto.</summary>
    public static IReadOnlyList<NotesSpan> Spans(string text)
    {
        var spans = new List<NotesSpan>();
        var bold = false;
        var start = 0;
        while (true)
        {
            var at = text.IndexOf("**", start, StringComparison.Ordinal);
            var piece = at < 0 ? text[start..] : text[start..at];
            if (piece.Length > 0)
            {
                spans.Add(new NotesSpan(piece.Replace("`", ""), bold));
            }

            if (at < 0)
            {
                break;
            }

            bold = !bold;
            start = at + 2;
        }

        return spans;
    }
}
