// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PulsoWin.Presentation.ViewModels;
using PulsoWin.Core.Localization;

namespace PulsoWin.App.Controls;

/// <summary>
/// Selección múltiple con casillas en una tabla, al estilo del Explorador de Windows:
/// - Ctrl/Shift + clic (o arrastrar) elige varias filas.
/// - Barra espaciadora: marca/desmarca todas las filas elegidas.
/// - Clic en la casilla de una fila elegida: el mismo valor se aplica a todas las elegidas.
/// - Clic derecho: menú para marcar/desmarcar las elegidas o todas.
/// Uso en XAML: controls:GridMultiCheck.IsEnabled="True". La lógica de marcado vive en Presentation (MultiCheck).
/// </summary>
public static class GridMultiCheck
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridMultiCheck), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;
        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.PreviewKeyDown += OnPreviewKeyDown;
        grid.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnCheckBoxClick));
        grid.ContextMenu = BuildMenu(grid);
    }

    private static IReadOnlyCollection<ISelectableRow> Highlighted(DataGrid grid) =>
        grid.SelectedItems.OfType<ISelectableRow>().ToList();

    private static IEnumerable<ISelectableRow> All(DataGrid grid) =>
        grid.Items.OfType<ISelectableRow>();

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not DataGrid grid) return;
        // Si el foco está en la propia casilla, la tecla ya la cambia; con varias elegidas manda la selección.
        var rows = Highlighted(grid);
        if (rows.Count == 0) return;
        MultiCheck.Toggle(rows);
        e.Handled = true;
    }

    private static void OnCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not CheckBox { DataContext: ISelectableRow row }) return;
        MultiCheck.ApplyFrom(row, Highlighted(grid));
    }

    private static ContextMenu BuildMenu(DataGrid grid)
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var menu = new ContextMenu();
        menu.Items.Add(Item(L.T("Grid_CheckChosen"), () => MultiCheck.Set(Highlighted(grid), true)));
        menu.Items.Add(Item(L.T("Grid_UncheckChosen"), () => MultiCheck.Set(Highlighted(grid), false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L.T("Grid_CheckAll"), () => MultiCheck.Set(All(grid), true)));
        menu.Items.Add(Item(L.T("Grid_UncheckAll"), () => MultiCheck.Set(All(grid), false)));
        menu.Opened += (_, _) =>
        {
            var any = Highlighted(grid).Count > 0;
            ((MenuItem)menu.Items[0]).IsEnabled = any;
            ((MenuItem)menu.Items[1]).IsEnabled = any;
        };
        return menu;
    }
}
