using System.ComponentModel;
using System.Runtime.CompilerServices;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;

namespace PulsoWin.App;

/// <summary>Fila de la lista de limpieza: una regla o la papelera.</summary>
public sealed class RuleRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _sizeText = "—";
    private string _status = "";

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

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    public string SizeText
    {
        get => _sizeText;
        private set { _sizeText = value; OnChanged(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnChanged(); }
    }

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
        LastScan = null;
        RecycleBytes = 0;
        RecycleItems = 0;
        SizeText = "—";
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
