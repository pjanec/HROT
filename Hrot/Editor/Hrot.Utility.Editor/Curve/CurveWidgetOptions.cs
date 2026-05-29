using Fdp.Toolkit.Utility;

namespace Hrot.Utility.Editor.Curve
{
    /// <summary>Options passed to CurveWidget.Draw.</summary>
    public readonly struct CurveWidgetOptions
    {
        /// <summary>Width of the plot area in ImGui units. 0 = fill available width.</summary>
        public readonly float PlotWidth;
        /// <summary>Height of the plot area in ImGui units.</summary>
        public readonly float PlotHeight;
        /// <summary>If >= 0, draw a vertical marker at this x position and label the output.</summary>
        public readonly float FixtureInputX;
        /// <summary>If true, draw the comparison curve stored in ComparisonCurve on the same axes.</summary>
        public readonly bool ShowComparisonOverlay;
        /// <summary>The comparison curve to draw when ShowComparisonOverlay is true.</summary>
        public readonly UtilityCurve? ComparisonCurve;

        public static readonly CurveWidgetOptions Default = new CurveWidgetOptions(
            plotWidth: 0f, plotHeight: 80f, fixtureInputX: -1f,
            showComparisonOverlay: false, comparisonCurve: null);

        public CurveWidgetOptions(float plotWidth, float plotHeight, float fixtureInputX,
                                   bool showComparisonOverlay, UtilityCurve? comparisonCurve)
        {
            PlotWidth             = plotWidth;
            PlotHeight            = plotHeight;
            FixtureInputX         = fixtureInputX;
            ShowComparisonOverlay = showComparisonOverlay;
            ComparisonCurve       = comparisonCurve;
        }
    }
}
