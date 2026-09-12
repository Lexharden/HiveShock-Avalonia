using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HiveShock.Logging;

namespace HiveShock.Configuration;

public sealed class CatalogGift
{
    public string Id { get; set; } = "";
    public string? NameEn { get; set; }
    public string? NameEs { get; set; }
    public List<string> Also { get; set; } = [];
    public int? Diamonds { get; set; }
    public int Seen { get; set; }
    public string? LastSeenUtc { get; set; }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NameEs) && !string.IsNullOrWhiteSpace(NameEn) &&
                !string.Equals(NameEs, NameEn, StringComparison.OrdinalIgnoreCase))
            {
                return $"{NameEs} / {NameEn}";
            }

            return NameEs ?? NameEn ?? Also.FirstOrDefault() ?? $"id {Id}";
        }
    }

    public string PrimaryName =>
        NameEs ?? NameEn ?? Also.FirstOrDefault() ?? "";

    public string? ImagePath => GiftImages.ResolvePath(Id, PrimaryName, null);
}

public sealed class GiftCatalogStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, CatalogGift> _byId = new(StringComparer.Ordinal);

    public GiftCatalogStore(string path) => _path = path;

    public string Path => _path;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byId.Count;
            }
        }
    }

    public static GiftCatalogStore LoadOrCreate()
    {
        var path = System.IO.Path.Combine(AppPaths.AppDirectory, "gift-catalog.json");
        var store = new GiftCatalogStore(path);
        store.Load();
        return store;
    }

    public void Load()
    {
        lock (_gate)
        {
            _byId.Clear();
            if (!File.Exists(_path))
            {
                return;
            }

            try
            {
                var text = File.ReadAllText(_path, Encoding.UTF8);
                var doc = JsonSerializer.Deserialize<CatalogFile>(text, JsonOpts);
                if (doc?.Gifts == null)
                {
                    return;
                }

                foreach (var g in doc.Gifts)
                {
                    var key = KeyOf(g);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    _byId[key] = g;
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"gift-catalog.json: {ex.Message}");
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            SaveUnlocked();
        }
    }

    /// <summary>Añade o actualiza una entrada manual. El id es opcional: basta el nombre EN o ES.</summary>
    public bool UpsertManual(string? id, string? nameEn, string? nameEs, int? diamonds, CatalogGift? existing = null)
    {
        if (string.IsNullOrWhiteSpace(nameEn) && string.IsNullOrWhiteSpace(nameEs))
        {
            throw new ArgumentException("Pon al menos un nombre (EN o ES).");
        }

        var incoming = new CatalogGift
        {
            Id = id?.Trim() ?? "",
            NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim(),
            NameEs = string.IsNullOrWhiteSpace(nameEs) ? null : nameEs.Trim(),
            Diamonds = diamonds,
        };
        var key = KeyOf(incoming);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Pon al menos un nombre (EN o ES).");
        }

        lock (_gate)
        {
            var previousKey = existing == null ? null : KeyOf(existing);
            CatalogGift row;
            if (!string.IsNullOrWhiteSpace(previousKey) &&
                previousKey != key &&
                _byId.Remove(previousKey, out var moved))
            {
                row = moved;
                _byId[key] = row;
            }
            else if (!_byId.TryGetValue(key, out row!))
            {
                row = new CatalogGift { Seen = 0 };
                _byId[key] = row;
            }

            row.Id = incoming.Id;
            row.NameEn = incoming.NameEn;
            row.NameEs = incoming.NameEs;
            if (diamonds is >= 0)
            {
                row.Diamonds = diamonds;
            }

            row.LastSeenUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveUnlocked();
            return true;
        }
    }

    /// <summary>Registra/actualiza un regalo visto en el live. Devuelve true si hubo cambio.</summary>
    public bool Observe(string? id, string? name, int? diamondsPerGift)
    {
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = string.IsNullOrWhiteSpace(id) ? $"name:{NormalizeKey(name!)}" : id.Trim();
        var changed = false;

        lock (_gate)
        {
            if (!_byId.TryGetValue(key, out var row))
            {
                row = new CatalogGift { Id = string.IsNullOrWhiteSpace(id) ? "" : id.Trim() };
                _byId[key] = row;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(id) && row.Id != id.Trim())
            {
                row.Id = id.Trim();
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                changed |= MergeName(row, name.Trim());
            }

            if (diamondsPerGift is > 0)
            {
                if (row.Diamonds != diamondsPerGift)
                {
                    row.Diamonds = diamondsPerGift;
                    changed = true;
                }
            }

            row.Seen++;
            row.LastSeenUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            if (changed)
            {
                SaveUnlocked();
            }
        }

        return changed;
    }

    public IReadOnlyList<CatalogGift> ListSorted()
    {
        lock (_gate)
        {
            return _byId.Values
                .OrderBy(g => g.Diamonds ?? int.MaxValue)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public CatalogGift? GetByIndex(int zeroBased)
    {
        var list = ListSorted();
        return zeroBased >= 0 && zeroBased < list.Count ? list[zeroBased] : null;
    }

    private void SaveUnlocked()
    {
        var file = new CatalogFile
        {
            Help = "Catálogo de regalos capturados del live (id + nombres EN/ES). Se actualiza solo.",
            Gifts = _byId.Values
                .OrderBy(g => g.Diamonds ?? int.MaxValue)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        var json = JsonSerializer.Serialize(file, JsonOpts);
        File.WriteAllText(_path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static bool MergeName(CatalogGift row, string name)
    {
        var changed = false;
        var spanish = LooksSpanish(name);

        if (spanish)
        {
            if (string.IsNullOrWhiteSpace(row.NameEs))
            {
                row.NameEs = name;
                changed = true;
            }
            else if (!NamesEqual(row.NameEs, name) && !NamesEqual(row.NameEn, name) &&
                     !row.Also.Any(a => NamesEqual(a, name)))
            {
                if (string.IsNullOrWhiteSpace(row.NameEn) && !LooksSpanish(row.NameEs))
                {
                    // NameEs was actually English; shuffle
                    row.NameEn = row.NameEs;
                    row.NameEs = name;
                    changed = true;
                }
                else if (!row.Also.Any(a => NamesEqual(a, name)))
                {
                    row.Also.Add(name);
                    changed = true;
                }
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(row.NameEn))
            {
                row.NameEn = name;
                changed = true;
            }
            else if (!NamesEqual(row.NameEn, name) && !NamesEqual(row.NameEs, name))
            {
                if (string.IsNullOrWhiteSpace(row.NameEs))
                {
                    // Second distinct name → treat as Spanish alt (TikTok locale)
                    row.NameEs = name;
                    changed = true;
                }
                else if (!row.Also.Any(a => NamesEqual(a, name)))
                {
                    row.Also.Add(name);
                    changed = true;
                }
            }
        }

        return changed;
    }

    public static string KeyOf(CatalogGift gift)
    {
        if (!string.IsNullOrWhiteSpace(gift.Id))
        {
            return gift.Id.Trim();
        }

        var name = gift.NameEs ?? gift.NameEn ?? gift.Also.FirstOrDefault() ?? "";
        return string.IsNullOrWhiteSpace(name) ? "" : $"name:{NormalizeKey(name)}";
    }

    private static bool NamesEqual(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string name) =>
        new string(name.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool LooksSpanish(string name)
    {
        foreach (var c in name)
        {
            if ("áéíóúüñÁÉÍÓÚÜÑ¿¡".Contains(c))
            {
                return true;
            }
        }

        var lower = name.ToLowerInvariant();
        return lower.Contains("rosa", StringComparison.Ordinal) && !lower.Equals("rose", StringComparison.Ordinal)
               || lower.Contains("para ti", StringComparison.Ordinal)
               || lower.Contains("corazón", StringComparison.Ordinal)
               || lower.Contains("corazon", StringComparison.Ordinal);
    }

    private sealed class CatalogFile
    {
        [JsonPropertyName("_help")]
        public string? Help { get; set; }

        [JsonPropertyName("gifts")]
        public List<CatalogGift> Gifts { get; set; } = [];
    }
}
