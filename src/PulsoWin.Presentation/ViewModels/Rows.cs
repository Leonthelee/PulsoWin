using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Updates;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Fila que se puede marcar.</summary>
public interface ISelectableRow : System.ComponentModel.INotifyPropertyChanged
{
    bool IsSelected { get; set; }
}

/// <summary>
/// Estado de la casilla «seleccionar todo» de una tabla: true (todas), false (ninguna) o null (algunas).
/// Se mantiene al día cuando se marcan filas una a una o cambia la lista.
/// </summary>
public sealed class SelectAllState<T> where T : ISelectableRow
{
    private readonly System.Collections.ObjectModel.ObservableCollection<T> _rows;
    private readonly Action _changed;

    public SelectAllState(System.Collections.ObjectModel.ObservableCollection<T> rows, Action changed)
    {
        _rows = rows;
        _changed = changed;
        foreach (var row in rows) row.PropertyChanged += OnRowChanged;
        rows.CollectionChanged += (_, e) =>
        {
            foreach (var row in e.NewItems?.OfType<T>() ?? []) row.PropertyChanged += OnRowChanged;
            foreach (var row in e.OldItems?.OfType<T>() ?? []) row.PropertyChanged -= OnRowChanged;
            _changed();
        };
    }

    public bool? Value
    {
        get
        {
            if (_rows.Count == 0) return false;
            var selected = _rows.Count(r => r.IsSelected);
            return selected == 0 ? false : selected == _rows.Count ? true : null;
        }
        set
        {
            if (value is not { } mark) return;
            foreach (var row in _rows) row.IsSelected = mark;
        }
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISelectableRow.IsSelected)) _changed();
    }
}

/// <summary>Fila de la lista de limpieza: una regla o la papelera.</summary>
public sealed partial class RuleRow : ObservableObject, ISelectableRow
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

public enum UpdateKind { Program, Windows }

/// <summary>
/// Una actualización pendiente en la lista única: un programa (winget) o una actualización de Windows.
/// Llega desmarcada: cada una se elige a propósito.
/// </summary>
public sealed partial class UpdateRow : ObservableObject, ISelectableRow
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _status = "Pendiente";

    private UpdateRow(UpdateKind kind, string name, string id, string current, string next, string size)
    {
        Kind = kind;
        Name = name;
        Id = id;
        CurrentVersion = current;
        NewVersion = next;
        SizeText = size;
    }

    public static UpdateRow FromWinget(WingetPackage p) =>
        new(UpdateKind.Program, p.Name, p.Id, p.InstalledVersion, p.AvailableVersion, "");

    public static UpdateRow FromWindows(WindowsUpdateItem u) =>
        new(UpdateKind.Windows, u.Title, u.UpdateId, "", u.KbId.Length > 0 ? u.KbId : "—", u.SizeText)
        {
            Status = u.IsDownloaded ? "Descargada, lista para instalar" : "Pendiente",
        };

    public UpdateKind Kind { get; }
    public string KindText => Kind == UpdateKind.Program ? "Programa" : "Windows";
    public string Name { get; }
    /// <summary>ID de winget o UpdateID de Windows Update (lo que se instala de verdad).</summary>
    public string Id { get; }
    public string CurrentVersion { get; }
    /// <summary>Versión nueva del programa, o el KB de la actualización de Windows.</summary>
    public string NewVersion { get; }
    public string SizeText { get; }
}
