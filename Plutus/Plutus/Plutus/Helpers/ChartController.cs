using Syncfusion.SfChart.XForms;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    public class ChartController
    {
        public SfChart Chart { get; set; }

        
        public ChartController() {
            Chart = new SfChart();
        }

        public void SetPrimaryAxis(ChartAxis axis)
        {
            Chart.PrimaryAxis = axis;
        }

        public void SetSecondaryAxis(RangeAxisBase axis)
        {
            Chart.SecondaryAxis = axis;
        }

        public void AddDataSet(string name, IEnumerable dataSet, string xBindingPath, string yBindingPath)
        {
            ColumnSeries series = new ColumnSeries();
            series.ItemsSource = dataSet;
            series.XBindingPath = xBindingPath;
            series.YBindingPath = yBindingPath;
            series.Label = name;
            series.DataMarker = new ChartDataMarker();
            series.EnableAnimation = true;
            series.EnableTooltip = true;
            Chart.Series.Add(series);
        }


        public void AddDataSets(List<Tuple<string, IEnumerable>> dataSets, string xBindingPath, string yBindingPath)
        {
            foreach (var dataSet in dataSets)
            {
                ColumnSeries series = new ColumnSeries();
                series.ItemsSource = dataSet.Item2;
                series.XBindingPath = xBindingPath;
                series.YBindingPath = yBindingPath;
                series.Label = dataSet.Item1;
                series.DataMarker = new ChartDataMarker();
                series.EnableAnimation = true;
                series.EnableTooltip = true;
                Chart.Series.Add(series);
            }
        }
    }
}
