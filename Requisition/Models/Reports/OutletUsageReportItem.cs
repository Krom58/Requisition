using System;

namespace Requisition.Models.Reports
{
    // Aggregated row for a single (Date, Outlet, Category)
    public class OutletUsageReportItem
    {
        public DateTime UsageDate { get; set; }              // date (grouping level 1)
        public int? OutletId { get; set; }                   // outlet id (may be null)
        public string OutletName { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty; // product category
        public decimal TotalQuantity { get; set; }           // summed quantity for category
        public decimal TotalCost { get; set; }               // summed cost for category

        // kept for compatibility (may be null)
        public int? TotalPeople { get; set; }                // TotalPeople from CombinedTransfer (legacy)

        // new: Consistent actual-people info computed server-side
        // - If transfers in the combined group all have the same ActualPeople, ConsistentActualPeople contains that value.
        // - If ActualPeople differ between transfers in the combined group, ActualPeopleInconsistent == true.
        public int? ConsistentActualPeople { get; set; }
        public bool ActualPeopleInconsistent { get; set; }

        public decimal? CombinedCostPerHead { get; set; }    // CombinedCostPerHead from CombinedTransfer (use if present)

        // Computed client-side:
        public decimal PercentOfGroup { get; set; }          // percent of group cost (0-100)
    }
}
