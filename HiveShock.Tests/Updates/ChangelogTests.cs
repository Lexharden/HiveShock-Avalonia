using HiveShock.Updates;

namespace HiveShock.Tests.Updates;

public sealed class ChangelogTests
{
    private const string Sample = """
        # Changelog

        Cambios relevantes.

        ## [Unreleased]

        ### Pendiente
        * Algo que aún no salió.

        ## [3.0.0-beta.2] - 2026-11-12

        ### Listas
        * **Supr** elimina.

        ## [3.0.0-beta.1] - 2026-11-11

        > **Beta de la nueva interfaz.**

        ### Interfaz nueva
        * Rediseño completo.

        ## [2.12.5] - 2026-11-05

        ### Perfiles
        * Majora's Mask: enemigos nuevos.

        ## [2.12.4] - 2026-11-04

        * Arreglos.
        """;

    private static IReadOnlyList<ChangelogEntry> Entries => Changelog.Parse(Sample);

    [Fact]
    public void Parse_skips_unreleased_and_keeps_order_date_and_body()
    {
        var entries = Entries;

        Assert.Equal(["3.0.0-beta.2", "3.0.0-beta.1", "2.12.5", "2.12.4"], entries.Select(e => e.Version));
        Assert.Equal("2026-11-12", entries[0].Date);
        Assert.Equal("### Listas\n* **Supr** elimina.", entries[0].Body);
        Assert.DoesNotContain(entries, e => e.Body.Contains("aún no salió", StringComparison.Ordinal));
    }

    [Fact]
    public void DateText_is_spanish_short_date() =>
        Assert.Equal("11 nov 2026", Entries.Single(e => e.Version == "3.0.0-beta.1").DateText);

    [Fact]
    public void Between_shows_versions_after_last_seen_up_to_current()
    {
        var shown = Changelog.Between(Entries, "3.0.0-beta.1", "3.0.0-beta.2");

        Assert.Equal(["3.0.0-beta.2"], shown.Select(e => e.Version));
    }

    [Fact]
    public void Between_without_last_seen_shows_everything_since_previous_stable()
    {
        // Quien viene de 2.x (sin versión vista guardada) ve las dos betas, no la 2.12.5.
        var shown = Changelog.Between(Entries, null, "3.0.0-beta.2");

        Assert.Equal(["3.0.0-beta.2", "3.0.0-beta.1"], shown.Select(e => e.Version));
    }

    [Fact]
    public void Between_from_older_stable_includes_skipped_versions_newest_first()
    {
        var shown = Changelog.Between(Entries, "2.12.4", "3.0.0-beta.2");

        Assert.Equal(["3.0.0-beta.2", "3.0.0-beta.1", "2.12.5"], shown.Select(e => e.Version));
    }

    [Theory]
    [InlineData("3.0.0-beta.2")] // ya vista
    [InlineData("3.1.0")] // venía de una más nueva (volvió a una anterior)
    public void Between_is_empty_when_nothing_new(string lastSeen) =>
        Assert.Empty(Changelog.Between(Entries, lastSeen, "3.0.0-beta.2"));

    [Fact]
    public void Between_never_shows_versions_newer_than_installed() =>
        Assert.DoesNotContain(Changelog.Between(Entries, "2.12.4", "2.12.5"), e => e.Version.StartsWith("3.", StringComparison.Ordinal));
}

public sealed class NotesMarkdownTests
{
    [Fact]
    public void Parses_headings_bullets_quotes_and_paragraphs()
    {
        var blocks = NotesMarkdown.Parse("""
            ### Interfaz nueva
            * **Supr** elimina.
            - Arrastra para ordenar.

            > Beta de la nueva interfaz.

            Un párrafo
            en dos líneas.
            """);

        Assert.Equal(
            [NotesBlockKind.Heading, NotesBlockKind.Bullet, NotesBlockKind.Bullet, NotesBlockKind.Quote, NotesBlockKind.Paragraph],
            blocks.Select(b => b.Kind));
        Assert.Equal("Interfaz nueva", blocks[0].PlainText);
        Assert.Equal("Un párrafo en dos líneas.", blocks[4].PlainText);
    }

    [Fact]
    public void Spans_mark_bold_parts_and_drop_code_ticks()
    {
        var spans = NotesMarkdown.Spans("**Supr** elimina en `Regalos` y **más**");

        Assert.Equal(
            [("Supr", true), (" elimina en Regalos y ", false), ("más", true)],
            spans.Select(s => (s.Text, s.Bold)));
    }

    [Fact]
    public void Indented_line_continues_previous_bullet()
    {
        var blocks = NotesMarkdown.Parse("* Primera parte\n  y su continuación.");

        Assert.Single(blocks);
        Assert.Equal("Primera parte y su continuación.", blocks[0].PlainText);
    }
}
