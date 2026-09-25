using System;
using System.Collections.Generic;
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

    /// <summary>Combo que quedó sin el mensaje de cierre (RepeatEnd=1) de TikTok.</summary>
    public sealed class PendingStreak
    {
        public ulong GroupId { get; set; }
        public int GiftId { get; set; }
        public string GiftName { get; set; } = "";
        public long DiamondsPerGift { get; set; }
        public string ViewerName { get; set; } = "";
        public int TotalGiftCount { get; set; }
        public long TotalDiamondCount { get; set; }
    }

    /// <summary>
    /// Tracks gift streak deltas from TikTok's running totals.
    /// Not thread-safe — el llamador debe serializar el acceso (Process/FlushStale).
    /// </summary>
    public class GiftStreakTracker
    {
        /// <summary>
        /// TikTok no siempre manda el mensaje final (RepeatEnd=1) de un combo —
        /// pasa sobre todo con regalos tipo "battle"/gallery (ej. ballenas, galaxias). Si no
        /// llega nada nuevo para ese GroupId durante esta ventana, se da el combo por cerrado
        /// y se procesa con el total visto hasta el momento.
        /// </summary>
        public static readonly TimeSpan ComboIdleTimeout = TimeSpan.FromSeconds(6);

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

            int total = msg.ComboTotal();
            long msgId = msg.Common?.MsgId ?? 0;
            int prevCount = 0;
            int banked = 0;
            bool restarted = false;
            if (_streaks.TryGetValue(msg.GroupId, out var prev))
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

            if (isFinal)
            {
                _streaks.Remove(msg.GroupId);
            }
            else
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

            return new GiftStreakEvent
            {
                StreakId = msg.GroupId,
                IsActive = !isFinal,
                IsFinal = isFinal,
                EventGiftCount = delta,
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

        public int ActiveStreaks() => _streaks.Count;

        public void Reset() => _streaks.Clear();
    }
}
