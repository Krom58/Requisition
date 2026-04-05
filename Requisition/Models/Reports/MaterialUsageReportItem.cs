using System.Text.Json.Serialization;

namespace Requisition.Models.Reports
{
    public class MaterialUsageReportItem
    {
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string PeriodKey { get; set; } = string.Empty;
        public string PeriodDisplay { get; set; } = string.Empty;

        // Now store issued/additional/returned separately
        public decimal TotalIssuedQuantity { get; set; }    // Initial + Additional summed
        public decimal TotalAdditionalQuantity { get; set; }
        public decimal TotalReturnedQuantity { get; set; }

        // Net used quantity = issued - returned
        [JsonIgnore]
        public decimal TotalQuantity => TotalIssuedQuantity - TotalReturnedQuantity;

        // Category / Type
        public string Category { get; set; } = string.Empty;

        // Kitchen
        public string KitchenDisplay { get; set; } = string.Empty;

        // ✅ ต้นทุนต่อหน่วย (weighted by issued quantity)
        public decimal UnitCost { get; set; }

        // ✅ ต้นทุนรวม (คำนวณจาก Net Quantity * UnitCost)
        [JsonIgnore]
        public decimal TotalCost => TotalQuantity * UnitCost;

        // Display formats
        // show net used quantity with 4 decimals (existing UI binds to this)
        [JsonIgnore]
        public string TotalQuantityDisplay => $"{TotalQuantity:F4}";

        [JsonIgnore]
        public string UnitCostDisplay => $"{UnitCost:F4}";

        [JsonIgnore]
        public string TotalCostDisplay => $"{TotalCost:F4}";
    }
}
