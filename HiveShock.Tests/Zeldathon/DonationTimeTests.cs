using System.Text.Json;
using System.Text.Json.Nodes;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class DonationTimeCalculatorTests
{
    [Fact]
    public void Converts_with_the_streamer_rate_and_keeps_fractions_for_later()
    {
        var rule = new DonationTimeRule { Units = 10, Seconds = 1 };
        long carry = 0;

        // Nueve Rosas de 1 diamante no llegan a un segundo; la décima sí.
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(0, DonationTimeCalculator.Seconds(rule, 1, ref carry));
        }

        Assert.Equal(1, DonationTimeCalculator.Seconds(rule, 1, ref carry));
        Assert.Equal(0, carry);
        Assert.Equal(2999, DonationTimeCalculator.Seconds(rule, 29_999, ref carry));
        Assert.Equal(9, carry);
    }

    [Fact]
    public void Honours_the_minimum_the_own_cap_and_disabled_rules()
    {
        long carry = 0;
        var rule = new DonationTimeRule { Units = 100, Seconds = 60, MinUnits = 100, MaxSecondsPerDonation = 600 };
        Assert.Equal(0, DonationTimeCalculator.Seconds(rule, 99, ref carry));
        Assert.Equal(0, carry); // por debajo del mínimo ni siquiera acumula
        Assert.Equal(60, DonationTimeCalculator.Seconds(rule, 100, ref carry));
        Assert.Equal(600, DonationTimeCalculator.Seconds(rule, 5000, ref carry));
        Assert.Equal(0, DonationTimeCalculator.Preview(new DonationTimeRule { Enabled = false }, 1000));
        Assert.Equal(0, DonationTimeCalculator.Preview(new DonationTimeRule { Units = 0 }, 1000));
    }

    [Fact]
    public void Formats_durations_for_people()
    {
        Assert.Equal("45 s", DonationTimeCalculator.Format(45));
        Assert.Equal("2 min 30 s", DonationTimeCalculator.Format(-150));
        Assert.Equal("1 h 5 min", DonationTimeCalculator.Format(3900));
        Assert.Equal("+1 min", DonationTimeReporter.Signed(60));
        Assert.Equal("−30 s", DonationTimeReporter.Signed(-30));
    }

    [Fact]
    public void Reads_the_organizer_policy_from_the_event()
    {
        using var doc = JsonDocument.Parse("""{"enabled":true,"allowAdd":false,"allowRemove":true,"maxSecondsPerDonation":600}""");
        var policy = DonationTimePolicy.FromJson(doc.RootElement)!;
        Assert.False(policy.Allows(DonationTimeDirection.Add));
        Assert.True(policy.Allows(DonationTimeDirection.Remove));
        Assert.Equal(600, policy.MaxSecondsPerDonation);
    }
}

public class DonationTimeReporterTests
{
    private readonly List<ZeldathonOutbound> _sent = [];
    private readonly DonationTimeSettings _settings = new() { Enabled = true };
    private DonationJournal? _disk;
    private bool _active = true;
    private DateTime _now = new(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc);

    private DonationTimeReporter Reporter() => new(
        () => _settings,
        () => _active,
        _sent.Add,
        load: () => _disk == null ? null : JsonSerializer.Deserialize<DonationJournal>(JsonSerializer.Serialize(_disk)),
        save: j => _disk = JsonSerializer.Deserialize<DonationJournal>(JsonSerializer.Serialize(j)),
        utcNow: () => _now);

    private static JsonNode Body(ZeldathonOutbound m) => JsonNode.Parse(m.Json)!;

    private static ZeldathonInbound Applied(string id, long requested, long applied, string limitedBy = "") =>
        new(ZeldathonInboundKind.TimeApplied, id, RequestedSeconds: requested, AppliedSeconds: applied, LimitedBy: limitedBy);

    [Fact]
    public void A_tiktok_combo_becomes_one_time_donation_with_the_streamer_direction()
    {
        _settings.TikTok = new DonationTimeRule { Direction = DonationTimeDirection.Remove, Units = 1, Seconds = 2 };
        var reporter = Reporter();

        Assert.True(reporter.OnTikTokGift("fan", "Rose", 5, 5));

        var msg = Assert.Single(_sent);
        var body = Body(msg);
        Assert.Equal("TIME_DONATION", msg.Type);
        Assert.StartsWith("don-", msg.Id);
        Assert.Equal(-10, body["deltaSeconds"]!.GetValue<long>());
        Assert.Equal("tiktok", body["source"]!["platform"]!.GetValue<string>());
        Assert.Equal("diamonds", body["source"]!["currency"]!.GetValue<string>());
        Assert.Equal(5, body["source"]!["amount"]!.GetValue<long>());
        Assert.Equal(5, body["source"]!["giftCount"]!.GetValue<int>());
        Assert.Equal(1, reporter.PendingCount);
    }

    [Fact]
    public void Twitch_bits_use_their_own_rate()
    {
        _settings.Twitch = new DonationTimeRule { Units = 100, Seconds = 60 };
        var reporter = Reporter();

        reporter.OnTwitchBits("fan", 250);

        var body = Body(Assert.Single(_sent));
        Assert.Equal(150, body["deltaSeconds"]!.GetValue<long>());
        Assert.Equal("bits", body["source"]!["currency"]!.GetValue<string>());
    }

    [Fact]
    public void Nothing_is_sent_when_off_not_connected_to_zeldathon_or_below_a_second()
    {
        _settings.Enabled = false;
        Assert.False(Reporter().OnTwitchBits("fan", 100));
        _settings.Enabled = true;
        _active = false;
        Assert.False(Reporter().OnTwitchBits("fan", 100));
        _active = true;
        _settings.Twitch = new DonationTimeRule { Units = 100, Seconds = 1 };
        var reporter = Reporter();
        Assert.False(reporter.OnTwitchBits("fan", 60));
        Assert.True(reporter.OnTwitchBits("fan", 40)); // 60 + 40 = 100 bits = 1 s
        Assert.Single(_sent);
    }

    [Fact]
    public void Pending_donations_survive_a_restart_and_are_resent_with_the_same_id()
    {
        var first = Reporter();
        first.OnTwitchBits("fan", 30);
        var id = _sent[0].Id;

        // HiveShock se cierra sin confirmación y se vuelve a abrir.
        _sent.Clear();
        var again = Reporter();
        Assert.Equal(1, again.PendingCount);
        again.OnConnected();

        Assert.Equal(id, Assert.Single(_sent).Id);
        again.OnReply(Applied(id, 30, 30));
        Assert.Equal(0, again.PendingCount);
        Assert.Empty(_disk!.Pending);
        Assert.Equal(30, again.AddedSeconds);
    }

    [Fact]
    public void The_applied_event_carries_what_the_server_really_applied_not_what_was_asked()
    {
        var reporter = Reporter();
        var events = new List<DonationTimeApplied>();
        reporter.Applied += events.Add;
        reporter.OnTwitchBits("fan", 100);
        var id = _sent[0].Id;

        reporter.OnReply(Applied(id, requested: 100, applied: 60, limitedBy: "daily_limit"));

        var e = Assert.Single(events);
        Assert.Equal(60, e.Seconds);
        Assert.Equal(100, e.RequestedSeconds);
        Assert.Equal("daily_limit", e.LimitedBy);
        Assert.Equal(_now, e.CreatedUtc);
    }

    [Fact]
    public void The_applied_event_keeps_the_sign_when_time_is_removed()
    {
        _settings.Twitch = new DonationTimeRule { Direction = DonationTimeDirection.Remove, Units = 1, Seconds = 1 };
        var reporter = Reporter();
        var events = new List<DonationTimeApplied>();
        reporter.Applied += events.Add;
        reporter.OnTwitchBits("fan", 45);

        reporter.OnReply(Applied(_sent[0].Id, requested: -45, applied: -45));

        Assert.Equal(-45, Assert.Single(events).Seconds);
    }

    [Fact]
    public void The_applied_event_is_not_raised_for_rejections_repeats_zero_or_other_messages()
    {
        var reporter = Reporter();
        var events = new List<DonationTimeApplied>();
        reporter.Applied += events.Add;
        reporter.OnTwitchBits("a", 100);
        reporter.OnTwitchBits("b", 100);
        reporter.OnTwitchBits("c", 100);
        var (a, b, c) = (_sent[0].Id, _sent[1].Id, _sent[2].Id);

        reporter.OnReply(new ZeldathonInbound(ZeldathonInboundKind.Error, a, Code: "event_not_live"));
        reporter.OnReply(new ZeldathonInbound(ZeldathonInboundKind.Ack, b)); // ya aplicado antes
        reporter.OnReply(Applied(c, requested: 100, applied: 0, limitedBy: "daily_limit")); // tope diario: sin efecto
        reporter.OnReply(Applied("hb-1", 0, 5)); // no es una donación

        Assert.Empty(events);
    }

    [Fact]
    public void The_applied_event_is_raised_once_even_if_the_same_confirmation_arrives_twice()
    {
        var reporter = Reporter();
        var events = new List<DonationTimeApplied>();
        reporter.Applied += events.Add;
        reporter.OnTwitchBits("fan", 100);
        var id = _sent[0].Id;

        reporter.OnReply(Applied(id, 100, 100));
        reporter.OnReply(Applied(id, 100, 100));

        Assert.Single(events);
    }

    [Fact]
    public void Replies_settle_each_donation_and_explain_what_happened()
    {
        var reporter = Reporter();
        reporter.OnTwitchBits("a", 100);
        reporter.OnTwitchBits("b", 200);
        reporter.OnTwitchBits("c", 300);
        var (a, b, c) = (_sent[0].Id, _sent[1].Id, _sent[2].Id);

        reporter.OnReply(Applied(a, 100, 60, "daily_limit"));
        reporter.OnReply(new ZeldathonInbound(ZeldathonInboundKind.Error, b, Code: "event_not_live"));
        reporter.OnReply(new ZeldathonInbound(ZeldathonInboundKind.Ack, c)); // ya aplicado antes
        reporter.OnReply(Applied("hb-1", 0, 0)); // no es una donación: se ignora

        Assert.Equal(0, reporter.PendingCount);
        Assert.Equal(60, reporter.AddedSeconds);
        var recent = reporter.Recent;
        Assert.Equal("Ya aplicado", recent.Single(e => e.Id == c).Status);
        Assert.StartsWith("Rechazado", recent.Single(e => e.Id == b).Status);
        var limited = recent.Single(e => e.Id == a);
        Assert.Equal(60, limited.AppliedSeconds);
        Assert.Contains("tope diario", limited.Status);
    }

    [Fact]
    public void The_organizer_policy_stops_directions_it_does_not_allow()
    {
        _settings.TikTok.Direction = DonationTimeDirection.Remove;
        var reporter = Reporter();
        reporter.Policy = new DonationTimePolicy(true, true, false, 3600, 14400, 14400);

        Assert.False(reporter.OnTikTokGift("fan", "Rose", 1, 1));
        Assert.Empty(_sent);
        Assert.StartsWith("No enviado", reporter.Recent[0].Status);

        reporter.Policy = null; // sin datos del servidor: se envía y el servidor decide
        Assert.True(reporter.OnTikTokGift("fan", "Rose", 1, 1));
    }

    [Fact]
    public void Very_old_pending_donations_are_dropped()
    {
        Reporter().OnTwitchBits("fan", 10);
        _now += DonationTimeReporter.MaxPendingAge + TimeSpan.FromMinutes(1);
        _sent.Clear();

        var later = Reporter();
        later.OnConnected();

        Assert.Equal(0, later.PendingCount);
        Assert.Empty(_sent);
    }
}

public class DonationTimeProtocolTests
{
    [Fact]
    public void Reads_time_applied_with_its_clock()
    {
        var msg = ZeldathonProtocol.Parse("""
            {"type":"TIME_APPLIED","id":"don-1","requestedSeconds":-90,"appliedSeconds":-60,"limitedBy":"daily_limit",
             "clock":{"racerId":"cuaco","remainingMs":1000,"status":"live","resetAtUtc":"2026-10-08T12:00:00.000Z",
             "serverTimeUtc":"2026-10-07T13:00:00.000Z"}}
            """);

        Assert.Equal(ZeldathonInboundKind.TimeApplied, msg.Kind);
        Assert.Equal(("don-1", -90L, -60L, "daily_limit"), (msg.Id, msg.RequestedSeconds, msg.AppliedSeconds, msg.LimitedBy));
        Assert.Equal(1000, msg.Clock!.RemainingMs);
    }

    [Fact]
    public void Builds_the_donation_message_and_trims_long_texts()
    {
        var m = ZeldathonProtocol.TimeDonation("don-x", 30, DonationPlatform.Twitch, 50, viewer: new string('v', 100));
        var body = JsonNode.Parse(m.Json)!;
        Assert.Equal("don-x", m.Id);
        Assert.Equal(64, body["source"]!["viewer"]!.GetValue<string>().Length);
        Assert.Null(body["source"]!["gift"]);
    }
}

public class DonationTimeClientTests
{
    [Fact]
    public async Task The_client_takes_the_new_clock_from_time_applied_and_reports_the_reply()
    {
        var factory = new FakeTransportFactory
        {
            Configure = (_, t) => t.OnSent = (self, text) =>
            {
                var body = JsonNode.Parse(text)!;
                if (body["type"]!.GetValue<string>() == "TIME_DONATION")
                {
                    var reply = JsonNode.Parse("""
                        {"type":"TIME_APPLIED","requestedSeconds":60,"appliedSeconds":60,
                         "clock":{"racerId":"cuaco","remainingMs":14460000,"status":"live",
                         "resetAtUtc":"2026-10-08T12:00:00.000Z","serverTimeUtc":"2026-10-07T13:00:00.000Z"}}
                        """)!;
                    reply["id"] = body["id"]!.GetValue<string>();
                    self.Push(reply.ToJsonString());
                }
            },
        };
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, new ZeldathonClientOptions
        {
            HeartbeatInterval = TimeSpan.FromHours(1),
            MinBackoff = TimeSpan.FromMilliseconds(1),
            MaxBackoff = TimeSpan.FromMilliseconds(5),
        });
        var replies = new List<ZeldathonInbound>();
        client.Replied += r =>
        {
            lock (replies)
            {
                replies.Add(r);
            }
        };
        client.Start(new Uri("wss://zeldathon.example.com/ingest"), "tok");

        client.Send(ZeldathonProtocol.TimeDonation("don-1", 60, DonationPlatform.Twitch, 100));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (replies.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        var reply = Assert.Single(replies);
        Assert.Equal((ZeldathonInboundKind.TimeApplied, "don-1", 60L), (reply.Kind, reply.Id, reply.AppliedSeconds));
        Assert.Equal(14_460_000, clock.Snapshot!.RemainingMs);
        Assert.Equal(1, client.Acked);
    }
}
