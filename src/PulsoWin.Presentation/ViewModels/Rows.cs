// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Updates;
using PulsoWin.Core.Localization;

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

/// <summary>
/// Marcado en bloque de varias filas elegidas a la vez (Ctrl/Shift + clic), como en el Explorador de Windows.
/// </summary>
public static class MultiCheck
{
    /// <summary>Barra espaciadora: si alguna elegida está sin marcar, las marca todas; si no, las desmarca todas.</summary>
    public static void Toggle(IEnumerable<ISelectableRow> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;
        Set(list, list.Any(r => !r.IsSelected));
    }

    public static void Set(IEnumerable<ISelectableRow> rows, bool mark)
    {
        foreach (var row in rows) row.IsSelected = mark;
    }

    /// <summary>
    /// Clic en la casilla de una fila que forma parte de una selección múltiple: el mismo valor
    /// se aplica a todas las filas elegidas. Si la fila no está entre las elegidas, solo cambia ella.
    /// </summary>
    public static void ApplyFrom(ISelectableRow clicked, IReadOnlyCollection<ISelectableRow> highlighted)
    {
        if (highlighted.Count < 2 || !highlighted.Contains(clicked)) return;
        Set(highlighted, clicked.IsSelected);
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
        // Las reglas vienen del catálogo en español; si hay traducción por id, se usa (si no, el texto original).
        Name = L.TOr($"Rule_{rule.Id}_Name", rule.Name);
        Category = L.TOr($"Category_{rule.Category}", rule.Category);
        Note = rule.Note is null ? null : L.TOr($"Rule_{rule.Id}_Note", rule.Note);
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
        new(L.T("Recycle_Name"), "Windows", L.T("Recycle_Note"));

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
            ? L.F("Row_CloseToClean", process)
            : item.Files.Count == 0 ? L.T("Row_NothingToClean") : L.F("Row_Files", item.Files.Count);
    }

    public void ApplyRecycleBin(long bytes, long items)
    {
        RecycleBytes = bytes;
        RecycleItems = items;
        SizeText = Bytes.Format(bytes);
        Status = items == 0 ? L.T("Row_Empty") : L.F("Row_Items", items);
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
    [ObservableProperty] private string _status = L.T("Upd_Pending");

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
            Status = u.IsDownloaded ? L.T("Upd_Downloaded") : L.T("Upd_Pending"),
        };

    public UpdateKind Kind { get; }
    public string KindText => Kind == UpdateKind.Program ? L.T("Upd_KindProgram") : "Windows";
    public string Name { get; }
    /// <summary>ID de winget o UpdateID de Windows Update (lo que se instala de verdad).</summary>
    public string Id { get; }
    public string CurrentVersion { get; }
    /// <summary>Versión nueva del programa, o el KB de la actualización de Windows.</summary>
    public string NewVersion { get; }
    public string SizeText { get; }
}
