using System.ComponentModel;
using System.Runtime.CompilerServices;
using PulsoWin.Core.Updates;

namespace PulsoWin.App;

/// <summary>Fila de paquete actualizable detectado por <c>winget</c>.</summary>
public sealed class PackageRow : INotifyPropertyChanged
{
    // Desmarcado por defecto: la actualización es silenciosa y acepta licencias,
    // así que cada paquete se elige a conciencia.
    private bool _isSelected;
    private string _status = "Pendiente";

    public PackageRow(WingetPackage package)
    {
        Package = package;
    }

    public WingetPackage Package { get; }
    public string Name => Package.Name;
    public string Id => Package.Id;
    public string InstalledVersion => Package.InstalledVersion;
    public string AvailableVersion => Package.AvailableVersion;
    public string Source => Package.Source;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
