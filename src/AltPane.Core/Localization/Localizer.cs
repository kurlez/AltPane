using System.ComponentModel;
using AltPane.Core.Models;

namespace AltPane.Core.Localization;

public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Current { get; } = new();

    IReadOnlyDictionary<string, string> _map;

    public Localizer()
    {
        _map = StringTables.English;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string this[string key]
    {
        get
        {
            if (_map.TryGetValue(key, out var value)) return value;
            if (StringTables.English.TryGetValue(key, out var english)) return english;
            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(this[key], args);
        }
        catch (FormatException)
        {
            return this[key];
        }
    }

    public void Apply(AppLanguage language, string? systemCultureName)
    {
        var chinese = language switch
        {
            AppLanguage.ZhCn => true,
            AppLanguage.En => false,
            _ => systemCultureName is not null &&
                 systemCultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        };
        _map = chinese ? StringTables.Chinese : StringTables.English;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public void Raise(string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
