using HiveShock.Live;

namespace HiveShock.Tests.Live;

public class FollowGateTests
{
    private sealed class Disk
    {
        public Dictionary<string, long>? Data;
        public int Saves;
    }

    private static FollowGate Make(Disk disk, Func<DateTime>? now = null) => new(
        () => disk.Data,
        d =>
        {
            disk.Data = new Dictionary<string, long>(d);
            disk.Saves++;
        },
        now ?? (() => DateTime.UtcNow));

    private static string K(string id, string channel = "streamer", string port = "tiktok") =>
        FollowGate.Key(port, channel, id);

    [Fact]
    public void First_follow_passes_and_repeats_are_blocked()
    {
        using var gate = Make(new Disk());
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("42")));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.TryAdmit(K("42")));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.TryAdmit(K("42")));
    }

    [Fact]
    public void Different_users_channels_and_ports_do_not_mix()
    {
        using var gate = Make(new Disk());
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("42")));
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("43")));
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("42", channel: "otro")));
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("42", port: "twitch")));
    }

    [Fact]
    public void Key_is_case_and_at_insensitive_for_channel_and_falls_back_to_anon()
    {
        Assert.Equal(K("42", "Streamer"), K("42", "@streamer"));
        using var gate = Make(new Disk());
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(FollowGate.Key("tiktok", "s", "")));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.TryAdmit(FollowGate.Key("tiktok", "s", null)));
    }

    [Fact]
    public void Concurrent_events_of_the_same_user_admit_exactly_one()
    {
        using var gate = Make(new Disk());
        var allowed = 0;
        Parallel.For(0, 200, _ =>
        {
            if (gate.TryAdmit(K("99")) == FollowAdmitKind.Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });
        Assert.Equal(1, allowed);
    }

    [Fact]
    public void History_survives_a_restart()
    {
        var disk = new Disk();
        using (var gate = Make(disk))
        {
            gate.TryAdmit(K("42"));
        } // Dispose vuelca a disco

        Assert.Equal(1, disk.Saves);
        using var again = Make(disk);
        Assert.Equal(FollowAdmitKind.Duplicate, again.TryAdmit(K("42")));
        Assert.Equal(FollowAdmitKind.Allowed, again.TryAdmit(K("7")));
    }

    [Fact]
    public void Unreadable_history_starts_empty_without_throwing()
    {
        using var gate = new FollowGate(
            () => throw new InvalidDataException("corrupto"),
            _ => { },
            () => DateTime.UtcNow);
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("42")));
    }

    [Fact]
    public void Flush_only_writes_when_something_changed_and_failures_are_retried()
    {
        var disk = new Disk();
        var fail = true;
        using var gate = new FollowGate(
            () => null,
            d =>
            {
                if (fail)
                {
                    throw new IOException("disco lleno");
                }

                disk.Saves++;
            },
            () => DateTime.UtcNow);
        gate.Flush();
        Assert.Equal(0, disk.Saves);

        gate.TryAdmit(K("1"));
        gate.Flush(); // falla, no lanza
        Assert.Equal(0, disk.Saves);

        fail = false;
        gate.Flush(); // se reintenta
        Assert.Equal(1, disk.Saves);
        gate.Flush();
        Assert.Equal(1, disk.Saves);
    }

    [Fact]
    public void Clear_forgets_everyone_and_persists_it()
    {
        var disk = new Disk();
        using var gate = Make(disk);
        gate.TryAdmit(K("42"));
        gate.Clear();
        Assert.Empty(disk.Data!);
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("42")));
    }

    [Fact]
    public void Memory_is_capped_dropping_the_oldest()
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var gate = Make(new Disk(), () => clock = clock.AddSeconds(1));
        for (var i = 0; i <= FollowGate.MaxTracked; i++)
        {
            gate.TryAdmit(K(i.ToString()));
        }

        Assert.True(gate.Count <= FollowGate.MaxTracked);
        // El más antiguo se descartó; el más reciente sigue bloqueado.
        Assert.Equal(FollowAdmitKind.Allowed, gate.TryAdmit(K("0")));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.TryAdmit(K(FollowGate.MaxTracked.ToString())));
    }

    private static ViewerIdentity Viewer(string? id, string? handle, string name = "", string port = "tiktok") =>
        new(port, id, handle, name);

    [Fact]
    public void Admit_recognises_the_same_person_by_id_even_after_a_rename()
    {
        using var gate = Make(new Disk());
        Assert.Equal(FollowAdmitKind.Allowed, gate.Admit(Viewer("42", "ana"), "streamer"));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer("42", "ana"), "streamer"));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer("42", "ana_nueva"), "streamer"));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer("42", null), "streamer"));
    }

    [Fact]
    public void Admit_recognises_an_event_without_id_through_the_remembered_handle()
    {
        using var gate = Make(new Disk());
        gate.Admit(Viewer("42", "ana"), "streamer");

        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer(null, "ana"), "streamer"));
    }

    [Fact]
    public void Admit_a_different_person_who_took_the_old_handle_is_a_new_follower()
    {
        using var gate = Make(new Disk());
        gate.Admit(Viewer("42", "ana"), "streamer");

        // El id manda: otro id con el mismo @usuario no es un duplicado (sería un falso positivo).
        Assert.Equal(FollowAdmitKind.Allowed, gate.Admit(Viewer("99", "ana"), "streamer"));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer("99", "ana"), "streamer"));
    }

    [Fact]
    public void Admit_with_only_a_handle_still_blocks_repeats()
    {
        using var gate = Make(new Disk());
        Assert.Equal(FollowAdmitKind.Allowed, gate.Admit(Viewer(null, "Ana"), "streamer"));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer(null, "@ana"), "streamer"));
    }

    [Fact]
    public void Admit_with_only_a_nickname_lets_it_through_without_remembering_anyone()
    {
        using var gate = Make(new Disk());

        Assert.Equal(FollowAdmitKind.Unidentified, gate.Admit(Viewer(null, null, "Maria"), "streamer"));
        Assert.Equal(FollowAdmitKind.Unidentified, gate.Admit(Viewer(null, null, "Maria"), "streamer"));
        Assert.Equal(0, gate.Count);
    }

    [Fact]
    public void Admit_still_honours_history_saved_with_the_old_id_only_keys()
    {
        // Entradas guardadas antes: "puerto:canal:id" (y "puerto:canal:usuario" cuando no había id).
        var disk = new Disk { Data = new Dictionary<string, long> { [K("42")] = 1, [K("vieja")] = 1 } };
        using var gate = Make(disk);

        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer("42", "otro"), "streamer"));
        Assert.Equal(FollowAdmitKind.Duplicate, gate.Admit(Viewer(null, "vieja"), "streamer"));
    }

    [Fact]
    public void Admit_keeps_channels_and_ports_apart()
    {
        using var gate = Make(new Disk());
        gate.Admit(Viewer("42", "ana"), "streamer");

        Assert.Equal(FollowAdmitKind.Allowed, gate.Admit(Viewer("42", "ana"), "otro"));
        Assert.Equal(FollowAdmitKind.Allowed, gate.Admit(Viewer("42", "ana", port: "twitch"), "streamer"));
    }

    [Fact]
    public void Admit_remembered_alias_is_persisted()
    {
        var disk = new Disk();
        using (var gate = Make(disk))
        {
            gate.Admit(Viewer("42", "ana"), "streamer");
        }

        using var again = Make(disk);
        Assert.Equal(FollowAdmitKind.Duplicate, again.Admit(Viewer(null, "ana"), "streamer"));
    }
}
