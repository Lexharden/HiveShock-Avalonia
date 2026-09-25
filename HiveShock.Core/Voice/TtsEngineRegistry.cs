namespace HiveShock.Voice;

/// <summary>
/// Motores de voz disponibles en este equipo, en orden de preferencia (el primero es el
/// predeterminado). Lo arma quien conoce las implementaciones concretas (MainViewModel en
/// escritorio); Core solo trabaja con <see cref="ITtsEngine"/>.
/// </summary>
public sealed class TtsEngineRegistry
{
    private readonly List<ITtsEngine> _engines;

    public TtsEngineRegistry(IEnumerable<ITtsEngine> engines)
    {
        _engines = [];
        foreach (var engine in engines)
        {
            if (_engines.Any(e => string.Equals(e.Id, engine.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Motor de voz duplicado: {engine.Id}", nameof(engines));
            }

            _engines.Add(engine);
        }

        if (_engines.Count == 0)
        {
            throw new ArgumentException("Hace falta al menos un motor de voz.", nameof(engines));
        }
    }

    public IReadOnlyList<ITtsEngine> All => _engines;

    /// <summary>Solo los que se pueden usar ahora (los no instalados quedan fuera).</summary>
    public IReadOnlyList<ITtsEngine> Available => _engines.Where(e => e.IsAvailable).ToList();

    public ITtsEngine Default => Available.FirstOrDefault() ?? _engines[0];

    public ITtsEngine? Find(string? id) =>
        _engines.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>El motor con ese id si existe y está disponible; si no, el predeterminado.</summary>
    public ITtsEngine Resolve(string? id) => Find(id) is { IsAvailable: true } engine ? engine : Default;

    /// <summary>Primer motor disponible que funciona sin internet (para el aviso de bloqueo), o null.</summary>
    public ITtsEngine? OfflineFallback => Available.FirstOrDefault(e => !e.RequiresInternet);
}
