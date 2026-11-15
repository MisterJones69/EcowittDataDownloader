using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using EcowittGui.Models;

namespace EcowittGui.Views;

/// <summary>Read-only DataGrid whose columns are set at runtime; each row is a string[].</summary>
public sealed class DynamicTable : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<TableColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<DynamicTable, IReadOnlyList<TableColumn>?>(nameof(Columns));

    public static readonly StyledProperty<IEnumerable?> RowsProperty =
        AvaloniaProperty.Register<DynamicTable, IEnumerable?>(nameof(Rows));

    private readonly DataGrid _grid;

    public DynamicTable()
    {
        _grid = new DataGrid
        {
            IsReadOnly = true,
            CanUserSortColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Extended,
        };
        Content = _grid;
    }

    public IReadOnlyList<TableColumn>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public IEnumerable? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty)
        {
            _grid.Columns.Clear();
            foreach (var c in Columns ?? [])
            {
                _grid.Columns.Add(new DataGridTextColumn
                {
                    Header = c.Header,
                    Binding = new Binding($"[{c.Index}]") { Mode = BindingMode.OneTime },
                });
            }
        }
        else if (change.Property == RowsProperty)
        {
            _grid.ItemsSource = Rows;
        }
    }
}
