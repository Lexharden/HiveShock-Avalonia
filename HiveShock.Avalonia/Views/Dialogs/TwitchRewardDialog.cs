using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Live;

namespace HiveShock.Avalonia.Views.Dialogs;

/// <summary>Crear o editar una recompensa de puntos de HiveShock en el canal de Twitch.</summary>
public sealed class TwitchRewardDialog : Window
{
    public TwitchRewardSpec? Result { get; private set; }

    public TwitchRewardDialog(TwitchRewardSpec? existing = null)
    {
        Title = existing == null ? "Nueva recompensa de Twitch" : "Editar recompensa de Twitch";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var title = Field(existing?.Title ?? "", "p. ej. Soltar un enemigo");
        title.MaxLength = TwitchRewardSpec.MaxTitleLength;
        var cost = Field(existing?.Cost.ToString() ?? "500", "puntos");
        var prompt = Field(existing?.Prompt ?? "", "Qué pasa en el juego (lo ven los espectadores)");
        prompt.MaxLength = TwitchRewardSpec.MaxPromptLength;
        var cooldown = Field(Zero(existing?.GlobalCooldownSeconds), "0");
        var perStream = Field(Zero(existing?.MaxPerStream), "0");
        var perUser = Field(Zero(existing?.MaxPerUserPerStream), "0");

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = existing == null ? "Crear en tu canal de Twitch" : "Cambiar en tu canal de Twitch",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        root.Children.Add(Hint(
            "Al crearla desde HiveShock, si el efecto no llega al juego (pausa, espectador bloqueado, juego cerrado) " +
            "se devuelven los puntos, y se pausa sola mientras no estés conectado."));

        root.Children.Add(Label("Nombre"));
        root.Children.Add(title);
        root.Children.Add(Label("Coste"));
        root.Children.Add(cost);
        root.Children.Add(Label("Descripción (opcional)"));
        root.Children.Add(prompt);

        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*") };
        limits.Children.Add(Column(0, "Espera entre canjes (s)", cooldown));
        limits.Children.Add(Column(2, "Máx. por directo", perStream));
        limits.Children.Add(Column(4, "Máx. por espectador", perUser));
        root.Children.Add(limits);
        root.Children.Add(Hint("0 = sin espera ni límite."));

        var cancel = new Button { Content = "Cancelar", Classes = { "secondary" }, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var ok = new Button
        {
            Content = existing == null ? "Crear en Twitch" : "Guardar en Twitch",
            Classes = { "primary" },
            IsDefault = true,
            MinWidth = 120,
        };
        ok.Click += async (_, _) =>
        {
            if (!Number(cost, out var c) || !Number(cooldown, out var cd) || !Number(perStream, out var ps) ||
                !Number(perUser, out var pu))
            {
                await Warn("El coste, la espera y los límites deben ser números enteros.");
                return;
            }

            var spec = new TwitchRewardSpec(title.Text?.Trim() ?? "", c, prompt.Text?.Trim() ?? "", cd, ps, pu);
            if (spec.Validate() is { } problem)
            {
                await Warn(problem);
                return;
            }

            Result = spec;
            Close(true);
        };

        var buttons = new StackPanel
        {
            Classes = { "bar" },
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => title.Focus();
    }

    private async Task Warn(string message) =>
        await new MessageDialog("Recompensa de Twitch", message, confirm: false, kind: "warn").ShowDialog<bool>(this);

    private static string Zero(int? value) => (value ?? 0).ToString();

    private static bool Number(TextBox box, out int value)
    {
        var text = box.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            value = 0;
            return true;
        }

        return int.TryParse(text, out value);
    }

    private static StackPanel Column(int column, string label, TextBox box)
    {
        var panel = new StackPanel();
        Grid.SetColumn(panel, column);
        panel.Children.Add(Label(label));
        panel.Children.Add(box);
        return panel;
    }

    private static TextBlock Hint(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };

    private static TextBlock Label(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

    private static TextBox Field(string text, string placeholder) =>
        new()
        {
            Text = text,
            PlaceholderText = placeholder,
            Margin = new Thickness(0, 0, 0, 12),
        };
}
