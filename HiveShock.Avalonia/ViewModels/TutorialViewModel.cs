using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Un paso del tutorial.</summary>
/// <param name="Title">Título de la tarjeta.</param>
/// <param name="Body">Explicación (2–4 frases, en tono de «tú»).</param>
/// <param name="Target">Clave <c>ctl:Tour.Key</c> a resaltar; null = tarjeta centrada sin resaltar nada.</param>
/// <param name="Enter">Qué hacer antes de mostrar el paso (normalmente navegar a una página).</param>
/// <param name="SelectTab">Clave de un TabItem que se selecciona antes de buscar el objetivo.</param>
public sealed record TutorialStep(
    string Title,
    string Body,
    string? Target = null,
    Action<MainViewModel>? Enter = null,
    string? SelectTab = null);

/// <summary>
/// Tutorial guiado por toda la app. Se abre solo la primera vez (<see cref="Services.UiPreferences.TutorialCompleted"/>)
/// y se puede repetir desde Ayuda o Preferencias. La vista (<c>TourOverlay</c>) resalta el objetivo de cada paso.
/// </summary>
public sealed partial class TutorialViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;

    public TutorialViewModel(MainViewModel shell)
    {
        _shell = shell;
        Steps = BuildSteps();
    }

    public IReadOnlyList<TutorialStep> Steps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(Title), nameof(Body), nameof(StepText), nameof(Progress),
        nameof(CanGoBack), nameof(IsLast), nameof(NextLabel))]
    private int _index;

    [ObservableProperty] private bool _isOpen;

    public TutorialStep Current => Steps[Math.Clamp(Index, 0, Steps.Count - 1)];
    public string Title => Current.Title;
    public string Body => Current.Body;
    public string StepText => $"Paso {Index + 1} de {Steps.Count}";
    public double Progress => (Index + 1) * 100.0 / Steps.Count;
    public bool CanGoBack => Index > 0;
    public bool IsLast => Index == Steps.Count - 1;
    public string NextLabel => Index == 0 ? "Empezar" : IsLast ? "Terminar" : "Siguiente";

    /// <summary>Se dispara al entrar en un paso, después de navegar: la vista busca y resalta el objetivo.</summary>
    public event Action<TutorialStep>? StepEntered;

    /// <summary>Al arrancar la app: solo si nunca se vio.</summary>
    public void StartIfFirstRun()
    {
        if (!_shell.Prefs.TutorialCompleted)
        {
            Start();
        }
    }

    [RelayCommand]
    private void Start()
    {
        IsOpen = true;
        GoTo(0);
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLast)
        {
            Finish();
            return;
        }

        GoTo(Index + 1);
    }

    [RelayCommand]
    private void Back()
    {
        if (CanGoBack)
        {
            GoTo(Index - 1);
        }
    }

    /// <summary>Saltar o terminar cuentan igual: no se vuelve a abrir solo.</summary>
    [RelayCommand]
    private void Finish()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _shell.Prefs.TutorialCompleted = true;
        _shell.Prefs.Save();
        _shell.GoStudio();
    }

    private void GoTo(int index)
    {
        Index = index;
        Current.Enter?.Invoke(_shell);
        StepEntered?.Invoke(Current);
    }

    /// <summary>
    /// Navega solo si no está ya en esa página: GoTikTok/GoTwitch releen regalos, catálogo y perfil de disco,
    /// y repetirlo en cada paso sería lento y movería la selección del editor que se está enseñando.
    /// </summary>
    private static Action<MainViewModel> Page(string key, Action<MainViewModel> go) =>
        shell =>
        {
            if (shell.CurrentPageKey != key)
            {
                go(shell);
            }
        };

    private static IReadOnlyList<TutorialStep> BuildSteps() =>
    [
        new("Te damos la bienvenida a HiveShock",
            "HiveShock convierte lo que pasa en tu directo (regalos, chat, seguidores, bits…) en efectos dentro del juego. " +
            "En un par de minutos te enseñamos cada parte. Puedes saltarlo cuando quieras y repetirlo desde Ayuda.",
            Enter: Page("studio", s => s.GoStudio())),

        new("El menú",
            "Arriba está lo del directo: Inicio, TikTok y Twitch. Abajo, las herramientas: Moderación y Teclas. " +
            "Con Ctrl+1…5 saltas entre ellas, y el botón de la esquina (Ctrl+B) lo pliega para ganar espacio.",
            Target: "nav", Enter: Page("studio", s => s.GoStudio())),

        new("Elige el juego",
            "Cada juego es un perfil con sus propios efectos, regalos y comandos. El marcado en dorado es el activo. " +
            "Con «Nuevo perfil» creas uno propio sin juego, para usar solo comandos y macros de teclas.",
            Target: "studio.games", Enter: Page("studio", s => s.GoStudio()), SelectTab: "studio.tab.live"),

        new("La sesión",
            "«En vivo» manda los efectos al juego. «Anotar» solo apunta los regalos que llegan y «Sin live» sirve para probar. " +
            "«Solo simular» registra todo sin tocar tu partida: ideal para ensayar.",
            Target: "studio.session", Enter: Page("studio", s => s.GoStudio()), SelectTab: "studio.tab.live"),

        new("Overlays para OBS",
            "Alertas, regalos, metas y contador para tu escena. Lo recomendado es copiar la dirección y pegarla en OBS como " +
            "«Fuente de navegador». A la derecha personalizas colores y tamaños con vista previa.",
            Target: "studio.overlays", Enter: Page("studio", s => s.GoStudio()), SelectTab: "studio.tab.overlays"),

        new("Conectar, siempre a mano",
            "Esta tarjeta está en todas las páginas: te dice si estás conectado y tiene el botón para conectar o desconectar. " +
            "Asegúrate de tener el juego abierto y la partida cargada antes de conectar.",
            Target: "sidebar.connect", Enter: Page("studio", s => s.GoStudio()), SelectTab: "studio.tab.live"),

        new("Tu cuenta de TikTok",
            "Escribe tu usuario (sin @) y guarda. El interruptor decide si TikTok se usa en este directo.",
            Target: "tiktok.account", Enter: Page("tiktok", s => s.GoTikTok())),

        new("Regalos",
            "La lista de regalos que hacen algo en el juego. Con + añades uno del catálogo; el número de la cabecera es el total. " +
            "Los que lanzan varios efectos llevan una etiqueta dorada.",
            Target: "tiktok.gifts.list", Enter: Page("tiktok", s => s.GoTikTok()), SelectTab: "tiktok.tab.gifts"),

        new("Qué hace cada regalo",
            "Elige el efecto y sus parámetros, añade más efectos en cadena con su espera, y decide desde cuántos regalos " +
            "seguidos se activa. «Probar en el juego» lo lanza ahora mismo, sin esperar a un regalo.",
            Target: "tiktok.gifts.editor", Enter: Page("tiktok", s => s.GoTikTok()), SelectTab: "tiktok.tab.gifts"),

        new("Catálogo de regalos",
            "Todos los regalos de TikTok (o solo los que han llegado a tus directos), con sus diamantes. " +
            "Selecciona uno y pulsa «Usar en este juego» para añadirlo a tus regalos.",
            Target: "tiktok.catalog", Enter: Page("tiktok", s => s.GoTikTok()), SelectTab: "tiktok.tab.catalog"),

        new("Comandos de chat",
            "Los espectadores escriben, por ejemplo, «!saltar» y pasa algo en el juego. Arriba activas los comandos, " +
            "eliges el prefijo y la espera entre usos para que nadie haga spam.",
            Target: "tiktok.chat", Enter: Page("tiktok", s => s.GoTikTok()), SelectTab: "tiktok.tab.chat"),

        new("Eventos",
            "Efectos para cuando alguien te sigue, cada cierto número de likes o cuando comparten el directo.",
            Target: "tiktok.events", Enter: Page("tiktok", s => s.GoTikTok()), SelectTab: "tiktok.tab.events"),

        new("Metas entre todos",
            "Una meta junta regalos de todos los espectadores y lanza un efecto al llenarse. " +
            "Se puede ver en OBS con su barra de progreso.",
            Target: "tiktok.goals", Enter: Page("tiktok", s => s.GoTikTok()), SelectTab: "tiktok.tab.goals"),

        new("Twitch",
            "Entra con tu cuenta de Twitch (se abre el navegador para autorizar). Aquí configuras comandos de chat, " +
            "bits y nuevos seguidores, por separado de TikTok.",
            Target: "twitch.account", Enter: Page("twitch", s => s.GoTwitch())),

        new("Moderación",
            "Si alguien abusa de los efectos, bloquéalo aquí: se le reconoce aunque cambie de @usuario. " +
            "También tienes la pausa de emergencia y el reinicio del equipo como efecto (desarmado por defecto).",
            Target: "mod.viewers", Enter: Page("moderacion", s => s.GoModeration())),

        new("Teclas",
            "Macros de teclado que pulsan teclas en la ventana del juego. Cada macro aparece como un efecto más, " +
            "así que puedes asignarla a un regalo, a un comando o a un evento.",
            Target: "keys.list", Enter: Page("teclas", s => s.GoKeyMacros())),

        new("Pausa de emergencia",
            "Si algo se descontrola en directo, pulsa aquí (o Ctrl+Mayús+P): los espectadores dejan de activar efectos " +
            "al instante. Se reanuda con el mismo botón.",
            Target: "titlebar.pause", Enter: Page("studio", s => s.GoStudio())),

        new("Qué está pasando",
            "Abre el panel de actividad para ver en tiempo real lo que llega del directo y lo que se manda al juego. " +
            "Abajo siempre ves la última línea.",
            Target: "titlebar.activity", Enter: Page("studio", s => s.GoStudio())),

        new("Preferencias",
            "Tema claro u oscuro, qué hace la X de la ventana (seguir en segundo plano en la bandeja o cerrar) " +
            "y volver a ver este tutorial.",
            Target: "titlebar.prefs", Enter: Page("studio", s => s.GoStudio())),

        new("¡Listo!",
            "Elige el juego, configura tus regalos y conecta. Si te pierdes, en Ayuda (F1) tienes todo resumido " +
            "y puedes repetir este tutorial. ¡Buen directo!",
            Target: "nav.help", Enter: Page("studio", s => s.GoStudio())),
    ];
}
