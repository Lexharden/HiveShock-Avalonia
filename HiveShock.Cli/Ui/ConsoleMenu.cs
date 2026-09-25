using HiveShock;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;

namespace HiveShock.Ui;

public sealed class ConsoleMenu
{
    private readonly BridgeRuntime _runtime;
    private readonly BridgeOptions _options;
    private readonly EffectCatalog _effects;
    private readonly GiftConfigStore _gifts;
    private readonly GiftCatalogStore _catalog;
    private readonly string _giftsPath;

    public ConsoleMenu(
        BridgeRuntime runtime,
        EffectCatalog effects,
        GiftConfigStore gifts,
        GiftCatalogStore catalog,
        string giftsPath,
        string effectsPath)
    {
        _runtime = runtime;
        _options = runtime.Options;
        _effects = effects;
        _gifts = gifts;
        _catalog = catalog;
        _giftsPath = giftsPath;
        _ = effectsPath;
    }

    /// <returns>true = start bridge, false = exit</returns>
    public bool Run()
    {
        while (true)
        {
            Console.Clear();
            PrintHeader();
            PrintStatus();
            Console.WriteLine();
            Console.WriteLine("  [1] Iniciar");
            Console.WriteLine("  [2] Capturar catálogo");
            Console.WriteLine("  [3] Gestionar regalos");
            Console.WriteLine("  [4] Catálogo");
            Console.WriteLine("  [5] Canales");
            Console.WriteLine("  [6] Efectos");
            Console.WriteLine($"  [7] Dry-run  ({OnOff(_options.DryRun)})");
            Console.WriteLine("  [8] Modo pruebas");
            Console.WriteLine("  [0] Salir");
            Console.WriteLine();
            Console.Write("> ");

            switch ((Console.ReadLine() ?? "").Trim())
            {
                case "1":
                case "":
                    if (!RequireLivePorts())
                    {
                        continue;
                    }

                    _options.DevMode = false;
                    _options.CaptureOnly = false;
                    Console.Clear();
                    return true;

                case "2":
                    if (!RequireTikTok())
                    {
                        continue;
                    }

                    _options.DevMode = false;
                    _options.CaptureOnly = true;
                    Console.Clear();
                    return true;

                case "3":
                    ManageGiftsMenu();
                    break;

                case "4":
                    CatalogMenu();
                    break;

                case "5":
                    ChangePorts();
                    break;

                case "6":
                    ListEffects();
                    Pause();
                    break;

                case "7":
                    _options.DryRun = !_options.DryRun;
                    break;

                case "8":
                    _options.DevMode = true;
                    _options.CaptureOnly = false;
                    _options.DisableCrowdControl = false;
                    Console.Clear();
                    return true;

                case "0":
                case "q":
                case "Q":
                    return false;
            }
        }
    }

    private bool RequireLivePorts()
    {
        if (_options.HasAnyLivePort)
        {
            return true;
        }

        Console.WriteLine();
        Console.WriteLine("Activa TikTok (usuario) o Twitch (cuenta). Usa [5].");
        Pause();
        return false;
    }

    private bool RequireTikTok()
    {
        if (_options.TikTokReady)
        {
            return true;
        }

        Console.WriteLine();
        Console.WriteLine("Anotar regalos necesita TikTok. Usa [5].");
        Pause();
        return false;
    }

    private void PrintHeader()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  HiveShock");
        Console.WriteLine("  Lo que pasa en tu live llega al juego");
        Console.WriteLine("=================================================");
    }

    private void PrintStatus()
    {
        var tiktok = string.IsNullOrWhiteSpace(_options.TikTokUniqueId)
            ? "(sin usuario)"
            : "@" + _options.TikTokUniqueId;
        var twitch = string.IsNullOrWhiteSpace(_options.TwitchUserLogin)
            ? "(sin cuenta)"
            : "@" + _options.TwitchUserLogin;

        Console.WriteLine();
        Console.WriteLine($"  TikTok    {( _options.TikTokEnabled ? "sí" : "no" )}  {tiktok}");
        Console.WriteLine($"  Twitch    {( _options.TwitchEnabled ? "sí" : "no" )}  {twitch}");
        Console.WriteLine($"  Perfil    {_options.ProfileId ?? "(activo)"}");
        Console.WriteLine($"  Juego     {_options.GameHost}:{_options.GamePort}");
        Console.WriteLine($"  Mapeos    {_gifts.Snapshot.Groups.Count}");
        Console.WriteLine($"  Catálogo  {_catalog.Count}");
    }

    private static string OnOff(bool value) => value ? "sí" : "no";

    private void ChangePorts()
    {
        Console.WriteLine();
        Console.WriteLine("  [1] Usuario TikTok");
        Console.WriteLine($"  [2] Usar TikTok ({OnOff(_options.TikTokEnabled)})");
        Console.WriteLine("  [3] Entrar en Twitch");
        Console.WriteLine("  [4] Salir de Twitch");
        Console.WriteLine($"  [5] Usar Twitch ({OnOff(_options.TwitchEnabled)})");
        Console.WriteLine("  [Enter] Volver");
        Console.Write("> ");
        switch ((Console.ReadLine() ?? "").Trim())
        {
            case "1":
                ChangeTikTokUser();
                break;
            case "2":
                ToggleFlag("TIKTOK_ENABLED", v => _options.TikTokEnabled = v, _options.TikTokEnabled);
                break;
            case "3":
                LoginTwitch();
                break;
            case "4":
                _runtime.LogoutTwitch();
                Console.WriteLine("Twitch: sesión cerrada.");
                Pause();
                break;
            case "5":
                ToggleFlag("TWITCH_ENABLED", v => _options.TwitchEnabled = v, _options.TwitchEnabled);
                break;
        }
    }

    private void ToggleFlag(string key, Action<bool> apply, bool current)
    {
        var next = !current;
        apply(next);
        try
        {
            EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), key, next ? "1" : "0");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Pause();
        }
    }

    private void LoginTwitch()
    {
        Console.WriteLine();
        Console.WriteLine("Se abre el navegador. Si no, ve a twitch.tv/activate y escribe el código.");
        var progress = new Progress<TwitchDeviceStart>(start =>
        {
            Console.WriteLine();
            Console.WriteLine($"  Código  {start.UserCode}");
            Console.WriteLine($"  {start.VerificationUri}");
        });
        try
        {
            _runtime.LoginTwitchAsync(progress, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"Twitch @{_options.TwitchUserLogin}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        Pause();
    }

    private void ChangeTikTokUser()
    {
        Console.WriteLine();
        Console.Write($"Usuario TikTok [{_options.TikTokUniqueId}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var normalized = BridgeOptions.NormalizeUniqueId(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            Console.WriteLine("Usuario inválido.");
            Pause();
            return;
        }

        _options.TikTokUniqueId = normalized;
        try
        {
            EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TIKTOK_UNIQUE_ID", normalized);
            Console.WriteLine($"Guardado: @{normalized}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        Pause();
    }

    private void ListEffects()
    {
        Console.WriteLine();
        Console.WriteLine($"  Efectos ({_effects.Count})");
        Console.WriteLine();
        foreach (var (id, label) in _effects.ListEntries())
        {
            Console.WriteLine($"  {id,-16}  {label}");
        }
    }

    private void ManageGiftsMenu()
    {
        var editor = new GiftFileEditor(_giftsPath);
        try
        {
            editor.Load();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo leer gifts.json: {ex.Message}");
            Pause();
            return;
        }

        var dirty = false;
        while (true)
        {
            Console.Clear();
            PrintHeader();
            Console.WriteLine();
            Console.WriteLine("  Gestionar regalos");
            if (dirty)
            {
                Console.WriteLine("  * Sin guardar");
            }

            Console.WriteLine();
            Console.WriteLine("  #   Nombre                 Id         Min  Cada  Efecto");
            for (var i = 0; i < editor.Gifts.Count; i++)
            {
                var g = editor.Gifts[i];
                Console.WriteLine(
                    $"  {i + 1,2}  {Truncate(g.Gift, 20),-22} {Truncate(string.IsNullOrWhiteSpace(g.Id) ? "-" : g.Id, 9),-10} {g.MinCount,3}  {(g.Each ? "sí" : "no"),4}  {g.Effect}");
            }

            if (editor.Gifts.Count == 0)
            {
                Console.WriteLine("  (vacío)");
            }

            Console.WriteLine();
            Console.WriteLine("  [N] Nuevo  [E] Editar  [B] Borrar  [A] Abrir JSON");
            Console.WriteLine("  [S] Guardar  [0] Volver");
            Console.Write("> ");

            var raw = (Console.ReadLine() ?? "").Trim();
            if (raw is "0" or "q" or "Q")
            {
                if (dirty && !AskYes("Hay cambios sin guardar. ¿Salir?", defaultYes: false))
                {
                    continue;
                }

                return;
            }

            if (raw is "s" or "S")
            {
                try
                {
                    editor.Save();
                    ReloadGifts();
                    dirty = false;
                    Console.WriteLine("Guardado.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                Pause();
                continue;
            }

            if (raw is "a" or "A")
            {
                OpenInNotepad(_giftsPath);
                Pause();
                try
                {
                    editor.Load();
                    ReloadGifts();
                    dirty = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Pause();
                }

                continue;
            }

            if (raw is "n" or "N")
            {
                EditableGift? created;
                if (_catalog.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  [1] Desde catálogo");
                    Console.WriteLine("  [2] Manual");
                    Console.Write("> ");
                    var how = (Console.ReadLine() ?? "").Trim();
                    created = how switch
                    {
                        "2" => PromptGift(null),
                        "1" or "" => PromptGiftFromCatalog(),
                        _ => null,
                    };
                }
                else
                {
                    created = PromptGift(null);
                }

                if (created != null)
                {
                    editor.Add(created);
                    dirty = true;
                }

                continue;
            }

            if (int.TryParse(raw, out var bare) && TryResolveIndex(bare, editor.Gifts.Count, out var bareIdx))
            {
                if (ApplyEdit(editor, bareIdx))
                {
                    dirty = true;
                }

                continue;
            }

            if (raw.StartsWith("e", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("b", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadIndex(raw, editor.Gifts.Count, out var index))
                {
                    continue;
                }

                if (raw.StartsWith("b", StringComparison.OrdinalIgnoreCase))
                {
                    if (AskYes($"¿Borrar {editor.Gifts[index].Gift}?", defaultYes: false))
                    {
                        editor.RemoveAt(index);
                        dirty = true;
                    }

                    continue;
                }

                if (ApplyEdit(editor, index))
                {
                    dirty = true;
                }
            }
        }
    }

    private bool ApplyEdit(GiftFileEditor editor, int index)
    {
        var edited = PromptGift(editor.Gifts[index]);
        if (edited == null)
        {
            return false;
        }

        editor.Gifts[index].Gift = edited.Gift;
        editor.Gifts[index].Also = edited.Also;
        editor.Gifts[index].Id = edited.Id;
        editor.Gifts[index].Effect = edited.Effect;
        editor.Gifts[index].What = edited.What;
        editor.Gifts[index].Diamonds = edited.Diamonds;
        return true;
    }

    private EditableGift? PromptGiftFromCatalog()
    {
        var picked = PickCatalogGift();
        if (picked == null)
        {
            return null;
        }

        var primary = picked.PrimaryName;
        var also = new List<string>();
        void AddAlias(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, primary, StringComparison.OrdinalIgnoreCase) ||
                also.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            also.Add(name);
        }

        AddAlias(picked.NameEn);
        AddAlias(picked.NameEs);
        foreach (var extra in picked.Also)
        {
            AddAlias(extra);
        }

        return PromptGift(new EditableGift
        {
            Gift = primary,
            Id = picked.Id,
            Also = also,
            Diamonds = picked.Diamonds,
            Effect = "impulse",
            What = "",
        });
    }

    private CatalogGift? PickCatalogGift()
    {
        _catalog.Load();
        var list = _catalog.ListSorted();
        if (list.Count == 0)
        {
            Console.WriteLine("Catálogo vacío. Usa [2] Capturar catálogo.");
            Pause();
            return null;
        }

        const int pageSize = 20;
        var page = 0;
        while (true)
        {
            Console.Clear();
            PrintHeader();
            Console.WriteLine();
            Console.WriteLine($"  Elegir regalo ({list.Count})");
            Console.WriteLine("  #    Id         Diam  Nombre");
            var start = page * pageSize;
            var end = Math.Min(start + pageSize, list.Count);
            for (var i = start; i < end; i++)
            {
                var g = list[i];
                Console.WriteLine(
                    $"  {i + 1,3}  {Truncate(string.IsNullOrWhiteSpace(g.Id) ? "-" : g.Id, 10),-10} {(g.Diamonds?.ToString() ?? "-"),4}  {Truncate(g.DisplayName, 40)}");
            }

            WritePagerFooter(list.Count > pageSize);
            var input = ReadPrompt();
            if (input is "0")
            {
                return null;
            }

            if (HandlePager(input, ref page, list.Count, pageSize))
            {
                continue;
            }

            if (int.TryParse(input, out var num) && num >= 1 && num <= list.Count)
            {
                return list[num - 1];
            }
        }
    }

    private void CatalogMenu()
    {
        while (true)
        {
            _catalog.Load();
            var list = _catalog.ListSorted();
            Console.Clear();
            PrintHeader();
            Console.WriteLine();
            Console.WriteLine($"  Catálogo ({list.Count})");
            Console.WriteLine("  #    Id         Diam  Veces  Nombre");
            var show = Math.Min(list.Count, 40);
            for (var i = 0; i < show; i++)
            {
                var g = list[i];
                Console.WriteLine(
                    $"  {i + 1,3}  {Truncate(string.IsNullOrWhiteSpace(g.Id) ? "-" : g.Id, 10),-10} {(g.Diamonds?.ToString() ?? "-"),4}  {g.Seen,5}  {Truncate(g.DisplayName, 32)}");
            }

            if (list.Count > show)
            {
                Console.WriteLine($"  ... +{list.Count - show}");
            }

            if (list.Count == 0)
            {
                Console.WriteLine("  (vacío — [2] Capturar catálogo)");
            }

            Console.WriteLine();
            Console.WriteLine("  [A] Abrir JSON  [R] Recargar  [0] Volver");
            Console.Write("> ");
            var raw = (Console.ReadLine() ?? "").Trim();
            if (raw is "0" or "q" or "Q")
            {
                return;
            }

            if (raw is "a" or "A")
            {
                OpenInNotepad(_catalog.Path);
                Pause();
            }
            // R u otra tecla: recarga al redibujar
        }
    }

    private EditableGift? PromptGift(EditableGift? existing)
    {
        Console.WriteLine();
        Console.WriteLine(existing == null ? "  Nuevo regalo" : $"  {existing.Gift}");
        Console.WriteLine();

        var gift = ReadOrDefault("Nombre", existing?.Gift ?? "");
        if (existing == null && string.IsNullOrWhiteSpace(gift))
        {
            return null;
        }

        var id = ReadOrDefault("Id", existing?.Id ?? "");
        var alsoRaw = ReadOrDefault("Alias", existing == null ? "" : string.Join(", ", existing.Also));
        var diamondsRaw = ReadOrDefault("Diamantes (referencia)", existing?.Diamonds?.ToString() ?? "");
        var minRaw = ReadOrDefault("Mín x del combo (1 = siempre)", existing?.MinCount.ToString() ?? "1");
        var each = AskYes("¿Cada uno? (x10 → 10 efectos)", defaultYes: existing?.Each == true);

        var effect = PickEffect(existing?.Effect ?? "impulse");
        if (effect == null)
        {
            return null;
        }

        var whatDefault = !string.IsNullOrWhiteSpace(existing?.What)
            ? existing.What
            : _effects.Describe(effect);
        var what = ReadOrDefault("Nota", whatDefault);

        int? diamonds = null;
        if (int.TryParse(diamondsRaw, out var d) && d >= 0)
        {
            diamonds = d;
        }

        var minCount = 1;
        if (int.TryParse(minRaw, out var mc) && mc > 0)
        {
            minCount = mc;
        }

        var also = alsoRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToList();

        Console.WriteLine();
        var comboHint = each ? $"mín x{minCount}, cada uno" : $"mín x{minCount}, 1 vez";
        Console.WriteLine($"  {gift.Trim()} → {effect} ({_effects.Describe(effect)}) [{comboHint}]");
        if (!AskYes("¿Guardar?", defaultYes: true))
        {
            return null;
        }

        return new EditableGift
        {
            Gift = gift.Trim(),
            Id = id.Trim(),
            Also = also,
            Effect = effect,
            What = what.Trim(),
            Diamonds = diamonds,
            MinCount = minCount,
            Each = each,
        };
    }

    private string? PickEffect(string current)
    {
        const int pageSize = 14;
        var filter = "";
        var page = 0;

        while (true)
        {
            var entries = _effects.ListEntries(string.IsNullOrWhiteSpace(filter) ? null : filter);
            if (page * pageSize >= entries.Count && page > 0)
            {
                page = 0;
            }

            Console.Clear();
            PrintHeader();
            Console.WriteLine();
            Console.WriteLine("  Efecto");
            if (!string.IsNullOrWhiteSpace(current))
            {
                Console.WriteLine($"  Actual  {current} — {_effects.Describe(current)}");
            }

            if (!string.IsNullOrWhiteSpace(filter))
            {
                Console.WriteLine($"  Buscar  \"{filter}\" ({entries.Count})");
            }

            Console.WriteLine();
            if (entries.Count == 0)
            {
                Console.WriteLine("  (sin resultados)");
            }
            else
            {
                var start = page * pageSize;
                var end = Math.Min(start + pageSize, entries.Count);
                var pages = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
                Console.WriteLine($"  #    Id                Descripción              {page + 1}/{pages}");
                for (var i = start; i < end; i++)
                {
                    var (id, label) = entries[i];
                    var mark = string.Equals(id, current, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                    Console.WriteLine($"  {i + 1,3}{mark} {id,-16}  {Truncate(label, 34)}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("  Número  Enter=actual  N/P  /texto  L  0=cancelar");
            Console.Write("> ");
            var input = (Console.ReadLine() ?? "").Trim();

            if (input.Length == 0)
            {
                return string.IsNullOrWhiteSpace(current) ? null : current;
            }

            if (input is "0")
            {
                return null;
            }

            if (HandlePager(input, ref page, entries.Count, pageSize))
            {
                continue;
            }

            if (input is "l" or "L")
            {
                filter = "";
                page = 0;
                continue;
            }

            if (input.StartsWith('/'))
            {
                filter = input[1..].Trim();
                page = 0;
                continue;
            }

            if (int.TryParse(input, out var num) && num >= 1 && num <= entries.Count)
            {
                return entries[num - 1].Id;
            }

            var exact = entries.FirstOrDefault(e =>
                string.Equals(e.Id, input, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exact.Id))
            {
                return exact.Id;
            }

            var partial = entries
                .Where(e => e.Id.Contains(input, StringComparison.OrdinalIgnoreCase) ||
                            e.Label.Contains(input, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (partial.Count == 1)
            {
                return partial[0].Id;
            }

            if (partial.Count > 1)
            {
                filter = input;
                page = 0;
                continue;
            }

            Console.WriteLine("No encontrado.");
            Pause();
        }
    }

    private static bool HandlePager(string input, ref int page, int count, int pageSize)
    {
        if (input is "n" or "N")
        {
            if ((page + 1) * pageSize < count)
            {
                page++;
            }

            return true;
        }

        if (input is "p" or "P")
        {
            if (page > 0)
            {
                page--;
            }

            return true;
        }

        return false;
    }

    private static void WritePagerFooter(bool hasPages)
    {
        Console.WriteLine();
        if (hasPages)
        {
            Console.WriteLine("  [N] Siguiente  [P] Anterior  [0] Cancelar");
        }
        else
        {
            Console.WriteLine("  [0] Cancelar");
        }

        Console.Write("> ");
    }

    private static string ReadPrompt() => (Console.ReadLine() ?? "").Trim();

    private static bool TryResolveIndex(int oneBased, int count, out int zeroBased)
    {
        zeroBased = oneBased - 1;
        return zeroBased >= 0 && zeroBased < count;
    }

    private bool TryReadIndex(string raw, int count, out int zeroBased)
    {
        zeroBased = -1;
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int oneBased;
        if (parts.Length >= 2 && int.TryParse(parts[1], out oneBased))
        {
            // ok
        }
        else
        {
            Console.Write("Número: ");
            if (!int.TryParse(Console.ReadLine(), out oneBased))
            {
                return false;
            }
        }

        if (!TryResolveIndex(oneBased, count, out zeroBased))
        {
            Console.WriteLine("Fuera de rango.");
            Pause();
            return false;
        }

        return true;
    }

    private static bool AskYes(string question, bool defaultYes)
    {
        var hint = defaultYes ? "S/n" : "s/N";
        Console.Write($"{question} ({hint}): ");
        var raw = (Console.ReadLine() ?? "").Trim();
        if (raw.Length == 0)
        {
            return defaultYes;
        }

        return raw.Equals("s", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("si", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("sí", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadOrDefault(string label, string current)
    {
        Console.Write(string.IsNullOrEmpty(current) ? $"  {label}: " : $"  {label} [{current}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? current : input.Trim();
    }

    private void ReloadGifts()
    {
        try
        {
            _gifts.Reload(_giftsPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo recargar: {ex.Message}");
        }
    }

    private static void OpenInNotepad(string path)
    {
        try
        {
            PlatformShell.OpenFile(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.Message}");
            Console.WriteLine(path);
        }
    }

    private static void Pause(string? message = null)
    {
        Console.WriteLine();
        Console.Write(message ?? "Enter...");
        Console.ReadLine();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..Math.Max(0, max - 3)] + "...";
}
