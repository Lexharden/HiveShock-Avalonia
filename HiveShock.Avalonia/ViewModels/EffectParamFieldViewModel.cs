using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class EffectParamFieldViewModel : ObservableObject
{
    private readonly EditableGift _gift;
    private readonly EffectParamDef _def;

    public EffectParamFieldViewModel(EditableGift gift, EffectParamDef def)
    {
        _gift = gift;
        _def = def;
        if (gift.Params.TryGetValue(def.Key, out var wire))
        {
            _valueText = FormatUi(def.ToUi(wire));
        }
    }

    public string Label => _def.Label;

    public string Hint =>
        string.Equals(_def.Unit, "hearts", StringComparison.OrdinalIgnoreCase)
            ? "Vacío = valor por defecto del efecto"
            : "Vacío = valor por defecto del efecto";

    [ObservableProperty] private string _valueText = "";

    partial void OnValueTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _gift.Params.Remove(_def.Key);
            return;
        }

        if (!TryParseUi(value, out var ui))
        {
            return;
        }

        _gift.Params[_def.Key] = _def.ToWire(ui);
    }

    private static string FormatUi(double ui)
    {
        if (Math.Abs(ui - Math.Round(ui)) < 0.0000001)
        {
            return ((long)Math.Round(ui)).ToString(CultureInfo.InvariantCulture);
        }

        return ui.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TryParseUi(string text, out double value)
    {
        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
