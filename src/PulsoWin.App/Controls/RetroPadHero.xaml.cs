using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace PulsoWin.App.Controls;

/// <summary>Cabecera animada. Solo presentación: no conoce la lógica de la app, solo IsActive y Caption.</summary>
public partial class RetroPadHero : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(RetroPadHero), new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(RetroPadHero), new PropertyMetadata("PULSA START"));

    public RetroPadHero()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Respeta la opción de Windows "Mostrar animaciones": sin ella, la cabecera queda quieta.
            if (SystemParameters.ClientAreaAnimation)
                ((Storyboard)Resources["IdleStory"]).Begin(this, isControllable: true);
            UpdateActive();
        };
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RetroPadHero)d).UpdateActive();

    private void UpdateActive()
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        var active = (Storyboard)Resources["ActiveStory"];
        if (IsActive) active.Begin(this, isControllable: true);
        else active.Stop(this);
    }
}
