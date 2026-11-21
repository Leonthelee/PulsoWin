using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Updates;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Fila de la lista de limpieza: una regla o la papelera.</summary>
public sealed partial class RuleRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private string _status = "";

    public RuleRow(CleanRule rule)
    {
        Rule = rule;
        Name = rule.Name;
        Category = rule.Category;
        Note = rule.Note;
        _isSelected = rule.DefaultOn;
    }

    private RuleRow(string name, string category, string note)
    {
        Name = name;
        Category = category;
        Note = note;
        _isSelected = true;
    }

    public static RuleRow CreateRecycleBin() =>
        new("Papelera de reciclaje", "Windows", "Se vacía en todas las unidades.");

    public CleanRule? Rule { get; }
    public bool IsRecycleBin => Rule is null;
    public string Name { get; }
    public string Category { get; }
    public string? Note { get; }

    public ScanItem? LastScan { get; private set; }
    public long RecycleBytes { get; private set; }
    public long RecycleItems { get; private set; }

    public long PendingBytes => IsRecycleBin ? RecycleBytes : LastScan is { BlockedBy: null } s ? s.Bytes : 0;
    public int PendingFiles => LastScan is { BlockedBy: null } s ? s.Files.Count : 0;
    public bool HasWork => IsRecycleBin ? RecycleItems > 0 : PendingFiles > 0;

    public void ApplyScan(ScanItem item)
    {
        LastScan = item;
        SizeText = Bytes.Format(item.Bytes);
        Status = item.BlockedBy is { } process
            ? $"Cierra «{process}» para limpiar"
            : item.Files.Count == 0 ? "Nada que limpiar" : $"{item.Files.Count:N0} archivos";
    }

    public void ApplyRecycleBin(long bytes, long items)
    {
        RecycleBytes = bytes;
        RecycleItems = items;
        SizeText = Bytes.Format(bytes);
        Status = items == 0 ? "Vacía" : $"{items:N0} elementos";
    }

    public void ApplyCleaned(string status)
    {
        Reset();
        Status = status;
    }

    public void Reset()
    {
        LastScan = null;
        RecycleBytes = 0;
        RecycleItems = 0;
        SizeText = "—";
        Status = "";
    }
}

/// <summary>Paquete actualizable detectado por winget. Llega desmarcado: cada uno se elige a propósito.</summary>
public sealed partial class PackageRow(WingetPackage package) : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _status = "Pendiente";

    public WingetPackage Package { get; } = package;
    public string Name => Package.Name;
    public string Id => Package.Id;
    public string InstalledVersion => Package.InstalledVersion;
    public string AvailableVersion => Package.AvailableVersion;
    public string Source => Package.Source;
}
