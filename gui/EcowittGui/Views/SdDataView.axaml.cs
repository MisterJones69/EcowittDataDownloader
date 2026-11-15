using Avalonia.Controls;
using EcowittGui.ViewModels;
using ScottPlot;

namespace EcowittGui.Views;

public partial class SdDataView : UserControl
{
    private SdDataViewModel? _vm;

    public SdDataView()
    {
        InitializeComponent();
        Plot.Plot.Axes.DateTimeTicksBottom();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.ChartChanged -= DrawChart;
        }
        _vm = DataContext as SdDataViewModel;
        if (_vm != null)
        {
            _vm.ChartChanged += DrawChart;
            DrawChart();
        }
    }

    private void DrawChart()
    {
        var plt = Plot.Plot;
        plt.Clear();
        plt.Axes.Left.Label.Text = "";
        plt.Axes.Right.Label.Text = "";
        var series = _vm?.GetChartSeries() ?? [];

        // first unit on the left axis, a second one on the right; further units share the left
        var units = series.Select(s => s.Unit).Distinct().ToList();
        string? leftUnit = units.ElementAtOrDefault(0);
        string? rightUnit = units.ElementAtOrDefault(1);
        plt.Axes.Left.Label.Text = units.Count > 2 ? string.Join(", ", units.Where(u => u != rightUnit)) : leftUnit ?? "";
        plt.Axes.Right.Label.Text = rightUnit ?? "";

        var palette = new ScottPlot.Palettes.Category10();
        for (int i = 0; i < series.Count; i++)
        {
            var s = series[i];
            var color = palette.GetColor(i);
            bool right = rightUnit != null && s.Unit == rightUnit;
            bool first = true;
            // split at missing values so gaps are not bridged by a line; a sparse series with
            // many gaps is drawn as one line instead (thousands of plottables would be slow)
            var segments = Segments(s.Xs, s.Ys).ToList();
            if (segments.Count > 50)
            {
                segments = [(segments.SelectMany(p => p.xs).ToArray(), segments.SelectMany(p => p.ys).ToArray())];
            }
            foreach (var (xs, ys) in segments)
            {
                var line = plt.Add.ScatterLine(xs, ys, color);
                line.LineWidth = 1.5f;
                line.MarkerSize = xs.Length == 1 ? 4 : 0;
                if (first)
                {
                    line.LegendText = s.Name;
                    first = false;
                }
                if (right)
                {
                    line.Axes.YAxis = plt.Axes.Right;
                }
            }
        }

        plt.Axes.DateTimeTicksBottom();
        plt.Legend.IsVisible = series.Count > 0;
        plt.Legend.Alignment = Alignment.UpperLeft;
        plt.Axes.AutoScale();
        Plot.Refresh();
    }

    private static IEnumerable<(double[] xs, double[] ys)> Segments(double[] xs, double[] ys)
    {
        var sx = new List<double>();
        var sy = new List<double>();
        for (int i = 0; i < xs.Length; i++)
        {
            if (double.IsNaN(xs[i]) || double.IsNaN(ys[i]))
            {
                if (sx.Count > 0)
                {
                    yield return (sx.ToArray(), sy.ToArray());
                    sx.Clear();
                    sy.Clear();
                }
                continue;
            }
            sx.Add(xs[i]);
            sy.Add(ys[i]);
        }
        if (sx.Count > 0)
        {
            yield return (sx.ToArray(), sy.ToArray());
        }
    }
}
