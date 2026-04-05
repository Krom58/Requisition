using System;

namespace Requisition.Models.Reports;

public class OutletCostComparisonItem
{
    public DateTime UsageDate { get; set; }
    public int? OutletId { get; set; }
    public string OutletName { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    // Estimated (initial) values
    public decimal EstimatedQuantity { get; set; }
    public decimal EstimatedCost { get; set; }

    // Adjustments
    public decimal AddedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }

    // Actual
    public decimal ActualQuantity { get; set; }
    public decimal ActualCost { get; set; }

    // ✅ เพิ่ม: จำนวนคนที่คาดการณ์ (ถ้า 3 ใบเท่ากัน)
    public int? ExpectedPeople { get; set; }

    // Group/context
    public int? TotalPeople { get; set; }

    // Percent of group (0-100)
    public decimal PercentOfGroup { get; set; }

    // Computed diffs
    public decimal CostDifference => ActualCost - EstimatedCost;
    public decimal? PercentDifference => EstimatedCost != 0m
        ? Math.Round((CostDifference / EstimatedCost) * 100m, 4)
        : (decimal?)null;
}
