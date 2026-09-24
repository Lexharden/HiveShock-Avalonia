using System;
using System.Collections.Generic;
using System.Linq;
using TikTokLive.Proto;

namespace TikTokLive.Helpers
{
    public class GiftStreakEvent
    {
        public ulong StreakId { get; set; }
        public bool IsActive { get; set; }
        public bool IsFinal { get; set; }
        public int EventGiftCount { get; set; }
        public int TotalGiftCount { get; set; }
        public long EventDiamondCount { get; set; }
        public long TotalDiamondCount { get; set; }
        /// <summary>TikTok reinició el conteo en el mismo GroupId (nuevo envío); lo previo se acumuló.</summary>
        public bool Restarted { get; set; }
    }

    /// <summary>
    /// Tracks gift streak deltas from TikTok's running totals.
    /// Not thread-safe — use from a single event-handling thread.
    /// </summary>
    public class GiftStreakTracker
    {
        private const long StaleSecs = 60;

<<<<<<< Updated upstream
        private readonly Dictionary<ulong, (int lastRepeatCount, long lastSeenTicks)> _streaks = new();
=======
        /// <summary>
        /// Ventana mucho más corta para regalos caros (por diamante), que casi nunca se
        /// mandan en combos rápidos reales — así el fallback de arriba no los hace esperar.
        /// </summary>
        public static readonly TimeSpan PremiumComboIdleTimeout = TimeSpan.FromMilliseconds(800);

        /// <summary>Diamantes por unidad a partir de los cuales se usa <see cref="PremiumComboIdleTimeout"/>.</summary>
        public const long PremiumDiamondThreshold = 500;

        private sealed class StreakState
        {
            public int GiftId;
            public string GiftName = "";
            public long DiamondsPerGift;
            public string ViewerName = "";
            public int LastRepeatCount;
            /// <summary>Regalos de envíos anteriores del mismo GroupId (conteo reiniciado por TikTok).</summary>
            public int Banked;
            public long LastMsgId;
            public long LastSeenTicks;
        }

        private readonly Dictionary<ulong, StreakState> _streaks = new();
>>>>>>> Stashed changes

        public GiftStreakEvent Process(WebcastGiftMessage msg)
        {
            int diamondPer = msg.GiftDetails?.DiamondCount ?? 0;
            bool isCombo = msg.IsComboGift();
            bool isFinal = msg.IsStreakOver();

            if (!isCombo)
            {
                return new GiftStreakEvent
                {
                    StreakId = msg.GroupId,
                    IsActive = false,
                    IsFinal = true,
                    EventGiftCount = 1,
                    TotalGiftCount = 1,
                    EventDiamondCount = diamondPer,
                    TotalDiamondCount = diamondPer,
                };
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            EvictStale(nowTicks);

<<<<<<< Updated upstream
=======
            int total = msg.ComboTotal();
            long msgId = msg.Common?.MsgId ?? 0;
>>>>>>> Stashed changes
            int prevCount = 0;
            int banked = 0;
            bool restarted = false;
            if (_streaks.TryGetValue(msg.GroupId, out var prev))
<<<<<<< Updated upstream
                prevCount = prev.lastRepeatCount;

            int delta = Math.Max(msg.RepeatCount - prevCount, 0);
=======
            {
                prevCount = prev.LastRepeatCount;
                banked = prev.Banked;

                // TikTok a veces reutiliza el GroupId para un segundo envío y vuelve a
                // contar desde 1 (ej. 2 Mishka Bear seguidos llegan como x1, x1). Un mensaje
                // nuevo (otro MsgId) que reinicia en 1 es otro regalo, no un duplicado.
                if (!isFinal && total == 1 && prevCount >= 1 && msgId != 0 && msgId != prev.LastMsgId)
                {
                    banked += prevCount;
                    prevCount = 0;
                    restarted = true;
                }
            }

            int count = Math.Max(total, prevCount);
            int delta = count - prevCount;
            int grandTotal = banked + count;
>>>>>>> Stashed changes

            if (isFinal)
                _streaks.Remove(msg.GroupId);
            else
<<<<<<< Updated upstream
                _streaks[msg.GroupId] = (msg.RepeatCount, nowTicks);

            long rc = Math.Max(msg.RepeatCount, 1);
=======
            {
                _streaks[msg.GroupId] = new StreakState
                {
                    GiftId = msg.GiftId,
                    GiftName = msg.GiftDetails?.GiftName ?? "",
                    DiamondsPerGift = diamondPer,
                    ViewerName = msg.User?.Nickname is { Length: > 0 } nick ? nick
                        : msg.User?.UniqueId is { Length: > 0 } uid ? uid : "",
                    LastRepeatCount = count,
                    Banked = banked,
                    LastMsgId = msgId != 0 ? msgId : prev?.LastMsgId ?? 0,
                    LastSeenTicks = nowTicks,
                };
            }
>>>>>>> Stashed changes

            return new GiftStreakEvent
            {
                StreakId = msg.GroupId,
                IsActive = !isFinal,
                IsFinal = isFinal,
                EventGiftCount = delta,
<<<<<<< Updated upstream
                TotalGiftCount = msg.RepeatCount,
                EventDiamondCount = (long)diamondPer * delta,
                TotalDiamondCount = (long)diamondPer * rc,
            };
        }

=======
                TotalGiftCount = grandTotal,
                EventDiamondCount = (long)diamondPer * delta,
                TotalDiamondCount = (long)diamondPer * grandTotal,
                Restarted = restarted,
            };
        }

        /// <summary>
        /// Cierra por timeout los combos que quedaron colgados sin mensaje final de TikTok.
        /// Los regalos caros (>= <see cref="PremiumDiamondThreshold"/> diamantes/unidad) usan
        /// <see cref="PremiumComboIdleTimeout"/>; el resto usa <see cref="ComboIdleTimeout"/>.
        /// </summary>
        public List<PendingStreak> FlushStale()
        {
            long nowTicks = DateTime.UtcNow.Ticks;

            List<ulong>? stale = null;
            foreach (var kv in _streaks)
            {
                var timeout = kv.Value.DiamondsPerGift >= PremiumDiamondThreshold
                    ? PremiumComboIdleTimeout
                    : ComboIdleTimeout;
                if (nowTicks - kv.Value.LastSeenTicks >= timeout.Ticks)
                {
                    (stale ??= new List<ulong>()).Add(kv.Key);
                }
            }

            if (stale == null)
            {
                return new List<PendingStreak>();
            }

            var result = new List<PendingStreak>(stale.Count);
            foreach (var groupId in stale)
            {
                var s = _streaks[groupId];
                _streaks.Remove(groupId);
                result.Add(new PendingStreak
                {
                    GroupId = groupId,
                    GiftId = s.GiftId,
                    GiftName = s.GiftName,
                    DiamondsPerGift = s.DiamondsPerGift,
                    ViewerName = s.ViewerName,
                    TotalGiftCount = s.Banked + s.LastRepeatCount,
                    TotalDiamondCount = s.DiamondsPerGift * (s.Banked + s.LastRepeatCount),
                });
            }

            return result;
        }

>>>>>>> Stashed changes
        public int ActiveStreaks() => _streaks.Count;

        public void Reset() => _streaks.Clear();

        private void EvictStale(long nowTicks)
        {
            long cutoff = TimeSpan.FromSeconds(StaleSecs).Ticks;
            var stale = _streaks.Where(kv => (nowTicks - kv.Value.lastSeenTicks) >= cutoff)
                .Select(kv => kv.Key).ToList();
            foreach (ulong id in stale)
                _streaks.Remove(id);
        }
    }
}
