using HiveShock.Live;

namespace HiveShock.Tests.Live;

public class ViewerGuardTests
{
    private sealed class Disk
    {
        public ViewerBlocklistFile? Data;
        public int Saves;
    }

    private static ViewerGuard Make(Disk disk, Func<DateTime>? now = null) => new(
        () => disk.Data,
        file =>
        {
            disk.Data = new ViewerBlocklistFile { Viewers = file.Viewers.Select(v => v.Clone()).ToList() };
            disk.Saves++;
        },
        now);

    private static ViewerIdentity Tt(string? id, string? handle, string name = "") =>
        new(LivePortIds.TikTok, id, handle, name);

    private static ViewerIdentity Tw(string? id, string? login, string name = "") =>
        new(LivePortIds.Twitch, id, login, name);

    [Fact]
    public void Nobody_is_blocked_and_effects_run_by_default()
    {
        var guard = Make(new Disk());
        Assert.False(guard.IsPaused);
        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tt("1", "ana")));
    }

    [Fact]
    public void Blocking_by_id_blocks_that_person_even_if_the_handle_changes()
    {
        var guard = Make(new Disk());
        Assert.True(guard.Block(Tt("42", "ana")));

        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", "ana")));
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", "ana_nueva")));
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", null)));
    }

    [Fact]
    public void Another_person_who_takes_the_old_handle_is_not_blocked()
    {
        var guard = Make(new Disk());
        guard.Block(Tt("42", "ana"));

        // Otro id con el mismo @usuario: es otra persona (el @ se puede reutilizar).
        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tt("99", "ana")));
    }

    [Fact]
    public void Blocking_by_handle_only_learns_the_id_when_it_shows_up()
    {
        var disk = new Disk();
        var guard = Make(disk);
        Assert.True(guard.BlockHandle(LivePortIds.TikTok, "@Ana"));

        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt(null, "ana")));
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", "ana"))); // aprende el id 42

        // Ahora la reconoce aunque cambie de @usuario...
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", "otro_nombre")));
        Assert.Equal("42", disk.Data!.Viewers.Single().UserId);
        // ...y otra persona que se quede con "ana" ya no cae.
        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tt("77", "ana")));
    }

    [Fact]
    public void Renaming_keeps_following_the_same_person()
    {
        var disk = new Disk();
        var guard = Make(disk);
        guard.Block(Tt("42", "ana"));

        guard.Evaluate(Tt("42", "ana_nueva"));

        var entry = disk.Data!.Viewers.Single();
        Assert.Equal("ana_nueva", entry.Handle);
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt(null, "ana_nueva")));
    }

    [Fact]
    public void Platforms_do_not_mix()
    {
        var guard = Make(new Disk());
        guard.Block(Tt("42", "ana"));

        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tw("42", "ana")));
    }

    [Fact]
    public void Handle_matching_ignores_case_and_at_sign()
    {
        var guard = Make(new Disk());
        guard.BlockHandle(LivePortIds.Twitch, "@Streamer_Fan");

        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tw(null, "STREAMER_FAN")));
    }

    [Fact]
    public void Someone_who_cannot_be_recognised_cannot_be_blocked()
    {
        var guard = Make(new Disk());

        Assert.False(guard.Block(Tt(null, null, "Solo nombre")));
        Assert.False(guard.Block(Tt("0", "", "Id cero")));
        Assert.Empty(guard.Blocked);
    }

    [Fact]
    public void Blocking_twice_is_a_no_op_and_unblock_lifts_it()
    {
        var disk = new Disk();
        var guard = Make(disk);
        Assert.True(guard.Block(Tt("42", "ana")));
        Assert.False(guard.Block(Tt("42", "ana")));
        Assert.Equal(1, disk.Saves);

        Assert.True(guard.Unblock(guard.Blocked.Single().Id));
        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tt("42", "ana")));
        Assert.False(guard.Unblock("no-existe"));
        Assert.Empty(disk.Data!.Viewers);
    }

    [Fact]
    public void Blocklist_survives_a_restart()
    {
        var disk = new Disk();
        Make(disk).Block(Tt("42", "ana", "Ana"));

        var again = Make(disk);

        Assert.Equal(ViewerVerdict.Blocked, again.Evaluate(Tt("42", null)));
        Assert.Equal("Ana", again.Blocked.Single().Name);
    }

    [Fact]
    public void Unreadable_blocklist_starts_empty_without_throwing()
    {
        var guard = new ViewerGuard(() => throw new InvalidDataException("corrupto"), _ => { });
        Assert.Empty(guard.Blocked);
        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tt("1", "x")));
    }

    [Fact]
    public void Garbage_entries_in_the_file_are_dropped()
    {
        var disk = new Disk
        {
            Data = new ViewerBlocklistFile
            {
                Viewers =
                [
                    new BlockedViewer { PortId = "tiktok", UserId = "5", Handle = "ok" },
                    new BlockedViewer { PortId = "tiktok" },
                    new BlockedViewer { UserId = "9" },
                ],
            },
        };

        var guard = Make(disk);

        Assert.Single(guard.Blocked);
        Assert.False(string.IsNullOrEmpty(guard.Blocked[0].Id));
    }

    [Fact]
    public void A_failing_save_does_not_throw_and_the_block_still_applies()
    {
        var guard = new ViewerGuard(() => null, _ => throw new IOException("disco lleno"));

        Assert.True(guard.Block(Tt("42", "ana")));
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", "ana")));
    }

    [Fact]
    public void Pause_is_reported_and_blocked_wins_over_paused()
    {
        var guard = Make(new Disk());
        var changes = new List<bool>();
        guard.PausedChanged += changes.Add;
        guard.Block(Tt("42", "ana"));

        guard.SetPaused(true);
        guard.SetPaused(true); // sin cambio: no se repite

        Assert.Equal([true], changes);
        Assert.Equal(ViewerVerdict.Paused, guard.Evaluate(Tt("1", "otro")));
        Assert.Equal(ViewerVerdict.Blocked, guard.Evaluate(Tt("42", "ana")));

        guard.SetPaused(false);
        Assert.Equal([true, false], changes);
        Assert.Equal(ViewerVerdict.Allowed, guard.Evaluate(Tt("1", "otro")));
    }

    [Fact]
    public void Pause_is_not_saved_so_a_restart_starts_resumed()
    {
        var disk = new Disk();
        var guard = Make(disk);
        guard.SetPaused(true);

        Assert.False(Make(disk).IsPaused);
    }

    [Fact]
    public void Check_records_activity_newest_first_and_never_records_blocked()
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var guard = Make(new Disk(), () => clock = clock.AddSeconds(1));
        guard.Block(Tt("42", "ana"));

        guard.Check(Tt("1", "uno"), "Follow");
        guard.Check(Tt("42", "ana"), "Follow"); // bloqueada: no se anota
        guard.Check(Tt("2", "dos"), "Regalo");

        Assert.Equal(["dos", "uno"], guard.Recent.Select(a => a.Viewer.Handle));
    }

    [Fact]
    public void Recent_list_is_capped()
    {
        var guard = Make(new Disk());
        for (var i = 0; i < ViewerGuard.MaxRecent + 25; i++)
        {
            guard.Check(Tt(i.ToString(), "u" + i), "x");
        }

        Assert.Equal(ViewerGuard.MaxRecent, guard.Recent.Count);
        Assert.Equal("u" + (ViewerGuard.MaxRecent + 24), guard.Recent[0].Viewer.Handle);
    }

    [Fact]
    public void Concurrent_checks_and_blocks_do_not_break_anything()
    {
        var guard = Make(new Disk());
        Parallel.For(0, 400, i =>
        {
            guard.Check(Tt(i.ToString(), "u" + i), "x");
            if (i % 10 == 0)
            {
                guard.Block(Tt(i.ToString(), "u" + i));
            }

            guard.Evaluate(Tt((i / 2).ToString(), null));
        });

        Assert.Equal(40, guard.Blocked.Count);
    }
}
