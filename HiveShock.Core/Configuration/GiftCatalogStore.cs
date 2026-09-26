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

    /// <summary>Imagen indicada por tiktok_gifts.json (se resuelve dentro de gifts-images/). No se guarda.</summary>
    [JsonIgnore]
    public string? ImageFile { get; set; }

    /// <summary>Icono oficial en el CDN de TikTok, si se conoce. No se guarda.</summary>
    [JsonIgnore]
    public string? IconUrl { get; set; }

    /// <summary>True si solo viene de tiktok_gifts.json (aún no se ha visto en un live). No se guarda.</summary>
    [JsonIgnore]
    public bool IsReferenceOnly { get; set; }

    /// <summary>
    /// Otros ids del mismo regalo (variantes de TikTok con el mismo nombre y precio, ej. Mishka Bear
    /// 5566 y 5582) fusionados en esta fila por <see cref="GiftCatalogStore.ListSorted"/>. No se guarda.
    /// </summary>
    [JsonIgnore]
    public List<string> OtherIds { get; set; } = [];

    /// <summary>"id 5566" o "ids 5566, 5582 (variantes del mismo regalo)".</summary>
    [JsonIgnore]
    public string IdsText => OtherIds.Count == 0
        ? string.IsNullOrWhiteSpace(Id) ? "sin id" : $"id {Id}"
        : $"ids {string.Join(", ", new[] { Id }.Concat(OtherIds))} (variantes del mismo regalo)";

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

    public string? ImagePath =>
        GiftImages.ResolvePathForNames(Id, new[] { NameEn, NameEs }.Concat(Also), ImageFile);

    internal CatalogGift Clone() => new()
    {
        Id = Id,
        NameEn = NameEn,
        NameEs = NameEs,
        Also = [.. Also],
        Diamonds = Diamonds,
        Seen = Seen,
        LastSeenUtc = LastSeenUtc,
        ImageFile = ImageFile,
        IconUrl = IconUrl,
        IsReferenceOnly = IsReferenceOnly,
        OtherIds = [.. OtherIds],
    };
}

/// <summary>
/// Catálogo de regalos. Dos fuentes:
/// <list type="bullet">
/// <item><c>gift-catalog.json</c>: lo que se aprende en los lives (visto N veces, nombres EN/ES,
/// diamantes) y las altas manuales. Es lo único que se guarda.</item>
/// <item><c>tiktok_gifts.json</c> (<see cref="GiftReferenceCatalog"/>): la lista oficial de regalos,
/// solo lectura. Completa diamantes, nombres e imagen de lo visto, y añade los regalos que aún
/// no aparecieron en ningún live para poder asignarles efectos igual.</item>
/// </list>
/// <see cref="ListSorted"/> devuelve copias ya combinadas, así editar la lista no toca el archivo.
/// Guardado atómico con .bak; si el archivo está dañado se recupera del .bak o se aparta, nunca
/// se sobrescribe con un catálogo vacío.
/// </summary>
public sealed class GiftCatalogStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<GiftReferenceCatalog> _loadReference;
    private readonly Dictionary<string, CatalogGift> _byId = new(StringComparer.Ordinal);
    private GiftReferenceCatalog _reference = GiftReferenceCatalog.Empty;

    /// <summary>Si el archivo no se pudo leer ni apartar, no se guarda para no destruirlo.</summary>
    private bool _readOnly;

    public GiftCatalogStore(string path, GiftReferenceCatalog? reference = null)
    {
        _path = path;
        _loadReference = reference != null
            ? () => reference
            : () => GiftReferenceCatalog.Load(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
    }

    public string Path => _path;

    /// <summary>Regalos vistos en lives o dados de alta a mano (lo que guarda gift-catalog.json).</summary>
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

    /// <summary>Lista oficial de regalos (tiktok_gifts.json) cargada.</summary>
    public GiftReferenceCatalog Reference
    {
        get
        {
            lock (_gate)
            {
                return _reference;
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

    /// <summary>Relee gift-catalog.json y tiktok_gifts.json (botón Recargar incluido).</summary>
    public void Load()
    {
        var reference = _loadReference();
        lock (_gate)
        {
            _reference = reference;
            _byId.Clear();
            _readOnly = false;
            if (!File.Exists(_path))
            {
                return;
            }

            if (TryRead(_path, out var gifts, out var error))
            {
                AddAll(gifts);
                return;
            }

            BridgeLog.Warn($"gift-catalog.json dañado ({error}).");
            if (TryRead(_path + ".bak", out var backup, out _))
            {
                AddAll(backup);
                BridgeLog.Warn($"gift-catalog.json: se recuperó la copia de respaldo ({backup.Count} regalos).");
                SetAsideCorrupt();
                return;
            }

            // Sin respaldo: se aparta el archivo dañado para que el próximo guardado no lo destruya.
            if (!SetAsideCorrupt())
            {
                _readOnly = true;
                BridgeLog.Error("gift-catalog.json dañado y no se pudo apartar: no se guardarán cambios para no perderlo.");
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

    /// <summary>
    /// Regalos ordenados por diamantes y nombre, como copias ya combinadas con tiktok_gifts.json.
    /// Con <paramref name="includeReference"/> también los que aún no se han visto en un live.
    /// </summary>
    public IReadOnlyList<CatalogGift> ListSorted(bool includeReference = true)
    {
        lock (_gate)
        {
            var result = new List<CatalogGift>(_byId.Count + (includeReference ? _reference.Count : 0));
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in _byId.Values)
            {
                result.Add(Enrich(row));
                if (!string.IsNullOrWhiteSpace(row.Id))
                {
                    known.Add(row.Id.Trim());
                }
            }

            if (includeReference)
            {
                foreach (var entry in _reference.All)
                {
                    if (known.Add(entry.Id))
                    {
                        result.Add(FromReference(entry));
                    }
                }
            }

            return MergeVariants(result)
                .OrderBy(g => g.Diamonds ?? int.MaxValue)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// TikTok publica variantes del mismo regalo con otro id (por región o evento): mismo nombre y
    /// mismo precio. Se muestran como una sola fila (id más visto como principal, los demás en
    /// <see cref="CatalogGift.OtherIds"/>, vistas sumadas). Con distinto precio son regalos distintos
    /// que comparten nombre (Freestyle de 1 y de 1800) y quedan separados. Coherente con el live:
    /// los efectos ya se asignan por nombre, así que una variante dispara el mismo efecto.
    /// </summary>
    private static List<CatalogGift> MergeVariants(List<CatalogGift> gifts)
    {
        var merged = new List<CatalogGift>(gifts.Count);
        foreach (var group in gifts.GroupBy(g => g.Diamonds is > 0 && NormalizeKey(g.NameEn ?? g.PrimaryName) is { Length: > 0 } name
                     ? $"{name}|{g.Diamonds}"
                     : $"solo|{KeyOf(g)}"))
        {
            if (group.Count() == 1)
            {
                merged.Add(group.First());
                continue;
            }

            var ordered = group
                .OrderByDescending(g => g.Seen)
                .ThenBy(g => long.TryParse(g.Id, out var n) ? n : long.MaxValue)
                .ToList();
            var main = ordered[0].Clone();
            foreach (var variant in ordered.Skip(1))
            {
                if (!string.IsNullOrWhiteSpace(variant.Id) && variant.Id != main.Id && !main.OtherIds.Contains(variant.Id))
                {
                    main.OtherIds.Add(variant.Id);
                }

                main.OtherIds.AddRange(variant.OtherIds.Where(id => !main.OtherIds.Contains(id)));
                foreach (var name in new[] { variant.NameEn, variant.NameEs }.Concat(variant.Also))
                {
                    if (!string.IsNullOrWhiteSpace(name) && !NamesEqual(name, main.NameEn) && !NamesEqual(name, main.NameEs) &&
                        !main.Also.Any(a => NamesEqual(a, name)))
                    {
                        main.Also.Add(name);
                    }
                }

                main.Seen += variant.Seen;
                main.IsReferenceOnly &= variant.IsReferenceOnly;
                if (string.Compare(variant.LastSeenUtc, main.LastSeenUtc, StringComparison.Ordinal) > 0)
                {
                    main.LastSeenUtc = variant.LastSeenUtc;
                }

                main.ImageFile ??= variant.ImageFile;
                main.IconUrl ??= variant.IconUrl;
            }

            merged.Add(main);
        }

        return merged;
    }

    /// <summary>Un regalo por id (visto o de la lista oficial), ya combinado; null si no se conoce.</summary>
    public CatalogGift? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            if (_byId.TryGetValue(id.Trim(), out var row))
            {
                return Enrich(row);
            }

            return _reference.FindById(id) is { } entry ? FromReference(entry) : null;
        }
    }

    public CatalogGift? GetByIndex(int zeroBased)
    {
        var list = ListSorted();
        return zeroBased >= 0 && zeroBased < list.Count ? list[zeroBased] : null;
    }

    /// <summary>Copia de lo guardado completada con la lista oficial: diamantes que falten, imagen y nombre oficial como alias.</summary>
    private CatalogGift Enrich(CatalogGift row)
    {
        var view = row.Clone();
        var entry = _reference.FindById(row.Id) ??
                    (string.IsNullOrWhiteSpace(row.Id) ? _reference.FindByName(row.PrimaryName) : null);
        if (entry == null)
        {
            return view;
        }

        if (view.Diamonds is not > 0 && entry.Diamonds is > 0)
        {
            view.Diamonds = entry.Diamonds;
        }

        if (entry.Name.Length > 0 && !NamesEqual(view.NameEn, entry.Name))
        {
            // El nombre oficial manda (TikTok renombra regalos, y el live a veces trae otro nombre para
            // el mismo id: 5827 es "Ice Cream Cone"); el que se vio en el live queda como alias.
            if (!string.IsNullOrWhiteSpace(view.NameEn) && !NamesEqual(view.NameEn, view.NameEs) &&
                !view.Also.Any(a => NamesEqual(a, view.NameEn)))
            {
                view.Also.Add(view.NameEn);
            }

            view.NameEn = entry.Name;
            view.Also.RemoveAll(a => NamesEqual(a, entry.Name));
        }

        view.ImageFile = entry.ImageFile;
        view.IconUrl = entry.IconUrl;
        return view;
    }

    private static CatalogGift FromReference(GiftReferenceEntry entry) => new()
    {
        Id = entry.Id,
        NameEn = entry.Name.Length > 0 ? entry.Name : null,
        Diamonds = entry.Diamonds,
        Seen = 0,
        ImageFile = entry.ImageFile,
        IconUrl = entry.IconUrl,
        IsReferenceOnly = true,
    };

    private void AddAll(IEnumerable<CatalogGift> gifts)
    {
        foreach (var g in gifts)
        {
            var key = KeyOf(g);
            if (!string.IsNullOrWhiteSpace(key))
            {
                g.Also ??= [];
                _byId[key] = g;
            }
        }
    }

    private static bool TryRead(string path, out List<CatalogGift> gifts, out string error)
    {
        gifts = [];
        error = "";
        if (!File.Exists(path))
        {
            error = "no existe";
            return false;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path, Encoding.UTF8), JsonOpts);
            gifts = doc?.Gifts?.Where(g => g != null).ToList() ?? [];
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Renombra el archivo dañado a gift-catalog.corrupt-FECHA.json. True si se pudo.</summary>
    private bool SetAsideCorrupt()
    {
        try
        {
            var aside = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!,
                $"gift-catalog.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(_path, aside);
            BridgeLog.Warn($"gift-catalog.json dañado guardado aparte como {System.IO.Path.GetFileName(aside)}.");
            return true;
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"No se pudo apartar gift-catalog.json dañado: {ex.Message}");
            return false;
        }
    }

    private void SaveUnlocked()
    {
        if (_readOnly)
        {
            return;
        }

        var file = new CatalogFile
        {
            Help = "Catálogo de regalos capturados del live (id + nombres EN/ES). Se actualiza solo. " +
                   "La lista oficial completa está en tiktok_gifts.json.",
            Gifts = _byId.Values
                .OrderBy(g => g.Diamonds ?? int.MaxValue)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOpts) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"gift-catalog.json no se pudo guardar: {ex.Message}");
        }
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
