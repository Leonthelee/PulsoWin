// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Windows.Markup;
using PulsoWin.Core.Localization;

namespace PulsoWin.App.Localization;

/// <summary>
/// Texto traducido en XAML: <c>Text="{l:Tr Main_Tagline}"</c>. Se resuelve al cargar la ventana con el idioma
/// elegido al arrancar; por eso cambiar de idioma reinicia la app (más simple y sin textos a medio cambiar).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
