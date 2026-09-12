namespace HiveShock.Avalonia.ViewModels;

public sealed class EffectChoice(string id, string display)
{
    public string Id { get; } = id;
    public string Display { get; } = display;
}
