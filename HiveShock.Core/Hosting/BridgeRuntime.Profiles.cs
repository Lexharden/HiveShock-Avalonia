using HiveShock.Configuration;
using HiveShock.Input;
using HiveShock.Logging;
using HiveShock.Platform;

namespace HiveShock.Hosting;

/// <summary>Perfiles: cambiar, crear, borrar y recargar efectos y regalos del activo.</summary>
public sealed partial class BridgeRuntime
{
    /// <summary>Cambia de perfil. Debe estar detenido el host.</summary>
    public void SwitchProfile(string profileId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de cambiar de perfil.");
        }

        var profile = ProfileStore.LoadById(profileId);
        ProfileStore.SaveActiveId(profile.Id);
        ApplyProfileToOptions(Options, profile, forcePortsFromProfile: true);

        var effects = LoadEffects(profile, _library);
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(profile.GiftsPath);

        _profile = profile;
        _effects = effects;
        _gifts = gifts;
        Options.ProfileId = profile.Id;
        Goals.ResetAll();
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);

        ApplyHotkeys();
        BridgeLog.Info($"Perfil cambiado: {profile.DisplayName} ({profile.Id}) → {Options.GameHost}:{Options.GamePort}");
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<LoadedGameProfile> ListProfiles() => ProfileStore.ListProfiles();

    /// <summary>Crea un perfil propio (sin cambiar al actual). «Copia» parte del perfil activo.</summary>
    public LoadedGameProfile CreateProfile(string name, ProfileTemplate template)
    {
        var starterMacro = template == ProfileTemplate.Starter ? EnsureStarterMacro() : null;
        return CustomProfileStore.Create(name, template, template == ProfileTemplate.CopyOf ? _profile : null, starterMacro);
    }

    /// <summary>Quita un perfil propio. Si es el activo pasa antes a otro. Con el puente detenido.</summary>
    public void DeleteProfile(string profileId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de borrar un perfil.");
        }

        var target = ProfileStore.LoadById(profileId);
        if (!target.IsCustom)
        {
            throw new InvalidOperationException("Solo se pueden borrar los perfiles propios.");
        }

        if (string.Equals(target.Id, _profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = ProfileStore.ListProfiles().FirstOrDefault(p => !p.IsCustom)
                           ?? throw new InvalidOperationException("No hay otro perfil al que cambiar.");
            SwitchProfile(fallback.Id);
        }

        CustomProfileStore.Delete(target);
        BridgeLog.Info($"Perfil propio quitado: {target.DisplayName} (queda en profiles-deleted por si fue un error)");
    }

    /// <summary>Macro de ejemplo para la plantilla básica: reutiliza la primera que exista o crea una.</summary>
    private string EnsureStarterMacro()
    {
        var existing = _library.Macros.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (existing != null)
        {
            return existing.Id;
        }

        var sample = new KeyMacro
        {
            Id = "teclas_1",
            Label = "Pulsar Espacio",
            Steps = [new KeyStep { Key = "Space" }],
        };
        SaveMacroLibrary([sample], _library.Window);
        return sample.Id;
    }

    private static EffectCatalog LoadEffects(LoadedGameProfile profile, MacroLibrary library)
    {
        var catalog = EffectCatalog.Load(profile.EffectsPath, profile.Info, new KeyMacroCatalog(library.Macros));
        catalog.SetSystemEffects(SystemEffectIds.Supported(PowerControlFactory.Create()));
        return catalog;
    }

    public void ReloadGifts()
    {
        _gifts.Reload(GiftsPath);
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);
    }

    /// <summary>
    /// Crea de nuevo el catálogo de efectos y los regalos. Solo con el puente detenido: el host en marcha guarda
    /// las instancias con las que arrancó y seguiría usándolas, así que habría dos versiones distintas a la vez.
    /// </summary>
    public void ReloadEffectsAndGifts()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de recargar los efectos.");
        }

        _effects = LoadEffects(_profile, _library);
        _gifts = new GiftConfigStore(_effects);
        _gifts.Reload(GiftsPath);
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Relee profile.json del perfil activo tras editarlo (sin cambiar de perfil).</summary>
    public void ReloadProfileMeta()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de editar el perfil.");
        }

        _profile = ProfileStore.LoadFromDirectory(_profile.Directory);
        ReloadEffectsAndGifts();
    }

    public void ReloadCatalog() => Catalog.Load();
}
