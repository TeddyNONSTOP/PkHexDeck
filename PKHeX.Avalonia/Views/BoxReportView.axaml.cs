using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using PKHeX.Application.Services;
using PKHeX.Presentation.ViewModels;
using PKHeX.Presentation.Localization;

namespace PKHeX.Avalonia.Views;

/// <summary>Report column presentation and geometry; report data and persistence stay in the VM.</summary>
public partial class BoxReportView : UserControl
{
    private BoxReportViewModel? _model;
    private readonly Dictionary<BoxReportColumn, DataGridColumn> _columns = [];
    private bool _updating;

    public BoxReportView()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ReportGrid")!.ColumnReordered += (_, _) => PersistLayout();
        DataContextChanged += (_, _) => AttachModel();
        AttachedToVisualTree += (_, _) =>
        {
            AttachModel();
            LocalizedStrings.Instance.PropertyChanged += OnLanguageChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            LocalizedStrings.Instance.PropertyChanged -= OnLanguageChanged;
            DetachModel();
        };
    }

    private void AttachModel()
    {
        if (ReferenceEquals(_model, DataContext)) return;
        DetachModel();
        _model = DataContext as BoxReportViewModel;
        var grid = this.FindControl<DataGrid>("ReportGrid")!;
        grid.Columns.Clear();
        _columns.Clear();
        if (_model is null) return;
        _updating = true;
        foreach (var descriptor in _model.Columns)
        {
            var header = new TextBlock { Text = descriptor.Header, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(header, descriptor.Header);
            AutomationProperties.SetName(header, descriptor.Header);
            AutomationProperties.SetHelpText(header, descriptor.Header);
            var column = new DataGridTemplateColumn
            {
                Header = header,
                SortMemberPath = descriptor.SortMember,
                CanUserSort = true,
                CanUserReorder = !descriptor.IsIdentity,
                MinWidth = descriptor.IsIdentity ? 100 : 80,
                MaxWidth = 800,
                Width = new DataGridLength(GetWidth(descriptor)),
                IsVisible = descriptor.IsVisible,
                CellTemplate = new FuncDataTemplate<BoxReportRow>((row, _) =>
                {
                    var value = row is null ? string.Empty : descriptor.GetValue(row)?.ToString() ?? string.Empty;
                    var text = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
                    ToolTip.SetTip(text, value);
                    AutomationProperties.SetName(text, $"{descriptor.Header}: {value}");
                    AutomationProperties.SetHelpText(text, value);
                    return text;
                }),
            };
            grid.Columns.Add(column);
            _columns.Add(descriptor, column);
            column.PropertyChanged += OnColumnPropertyChanged;
        }
        ApplyOrder();
        _updating = false;
        _model.ColumnLayoutChanged += UpdateLayout;
    }

    private double GetWidth(BoxReportColumn descriptor)
    {
        var width = _model?.GetColumnLayout(descriptor.Id)?.Width ?? descriptor.DefaultWidth;
        return double.IsFinite(width) && width >= 80 && width <= 800 ? width : descriptor.DefaultWidth;
    }

    private void ApplyOrder()
    {
        if (_model is null) return;
        // Identifying columns always remain first and frozen, including after malformed saved layouts.
        var ordered = _model.Columns.Where(c => c.IsIdentity).Concat(_model.Columns.Where(c => !c.IsIdentity)
            .OrderBy(c => _model.GetColumnLayout(c.Id)?.Order ?? Array.IndexOf(_model.Columns, c))).ToArray();
        for (var index = 0; index < ordered.Length; index++)
            _columns[ordered[index]].DisplayIndex = index;
    }

    private void UpdateLayout(bool reset)
    {
        _updating = true;
        foreach (var (descriptor, column) in _columns)
        {
            column.IsVisible = descriptor.IsVisible;
            if (reset) column.Width = new DataGridLength(descriptor.DefaultWidth);
        }
        if (reset) ApplyOrder();
        _updating = false;
        PersistLayout();
    }

    private void OnColumnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_updating && e.Property == DataGridColumn.WidthProperty)
            PersistLayout();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var (descriptor, column) in _columns)
        {
            descriptor.RefreshHeader();
            if (column.Header is not TextBlock header) continue;
            header.Text = descriptor.Header;
            ToolTip.SetTip(header, descriptor.Header);
            AutomationProperties.SetName(header, descriptor.Header);
            AutomationProperties.SetHelpText(header, descriptor.Header);
        }
        _model?.Refresh();
    }

    private void PersistLayout() => _model?.SaveColumnLayout(_columns.Select(pair => new AppSettings.ReportColumnLayout
    {
        Id = pair.Key.Id, Width = pair.Value.Width.Value, Order = pair.Value.DisplayIndex, Visible = pair.Key.IsVisible,
    }));

    private void DetachModel()
    {
        if (_model is null) return;
        PersistLayout();
        _model.ColumnLayoutChanged -= UpdateLayout;
        foreach (var column in _columns.Values) column.PropertyChanged -= OnColumnPropertyChanged;
        _model = null;
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is BoxReportViewModel vm)
            vm.ActivateSelectedRowCommand.Execute(null);
    }
}
