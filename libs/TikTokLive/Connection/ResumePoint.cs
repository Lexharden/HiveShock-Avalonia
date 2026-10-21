using System;

namespace TikTokLive.Connection
{
    /// <summary>
    /// Último punto de lectura que TikTok indicó (<c>cursor</c> + <c>internal_ext</c>). Si se pasa al reconectar, TikTok
    /// podría reenviar lo ocurrido desde ese punto. Es experimental y no está documentado: no se sabe si lo acepta.
    /// Se comparte entre conexiones para que sobreviva a un cliente nuevo.
    /// </summary>
    public sealed class ResumePoint
    {
        private readonly object _gate = new object();
        private string _cursor = "";
        private string _internalExt = "";
        private string _roomId = "";
        private DateTime _updatedUtc = DateTime.MinValue;

        public string Cursor { get { lock (_gate) return _cursor; } }

        public string InternalExt { get { lock (_gate) return _internalExt; } }

        public DateTime UpdatedUtc { get { lock (_gate) return _updatedUtc; } }

        public void Update(string roomId, string? cursor, string? internalExt)
        {
            if (string.IsNullOrEmpty(cursor))
            {
                return;
            }

            lock (_gate)
            {
                _cursor = cursor!;
                _internalExt = internalExt ?? "";
                _roomId = roomId;
                _updatedUtc = DateTime.UtcNow;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _cursor = "";
                _internalExt = "";
                _roomId = "";
                _updatedUtc = DateTime.MinValue;
            }
        }

        /// <summary>Hay un punto reciente y de la misma sala (no más viejo que <paramref name="maxAge"/>) que mandar al reconectar.</summary>
        public bool TryGet(string roomId, TimeSpan maxAge, out string cursor, out string internalExt)
        {
            lock (_gate)
            {
                if (_cursor.Length > 0 && _roomId == roomId && DateTime.UtcNow - _updatedUtc <= maxAge)
                {
                    cursor = _cursor;
                    internalExt = _internalExt;
                    return true;
                }
            }

            cursor = "";
            internalExt = "";
            return false;
        }
    }
}
