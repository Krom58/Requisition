using System.Text.Json.Serialization;

namespace Requisition.Models.Reports
{
    public class MaterialUsageReportItem
    {
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string PeriodKey { get; set; } = string.Empty;
        public string PeriodDisplay { get; set; } = string.Empty;
        public decimal TotalQuantity { get; set; }

        // Category / Type
        public string Category { get; set; } = string.Empty;

        // Kitchen
        public string KitchenDisplay { get; set; } = string.Empty;

        // ✅ ต้นทุนต่อหน่วย
        public decimal UnitCost { get; set; }

        // ✅ ต้นทุนรวม (คำนวณจาก TotalQuantity * UnitCost)
        [JsonIgnore]
        public decimal TotalCost => TotalQuantity * UnitCost;

        // ✅ แก้ไข: เปลี่ยนจาก N4 เป็น F4 (ไม่มี comma คั่น)
        [JsonIgnore]
        public string TotalQuantityDisplay => $"{TotalQuantity:F4}";

        // ✅ แก้ไข: เปลี่ยนจาก N2 เป็น F2
        [JsonIgnore]
        public string UnitCostDisplay => $"{UnitCost:F4}";

        // ✅ แก้ไข: เปลี่ยนจาก N2 เป็น F2
        [JsonIgnore]
        public string TotalCostDisplay => $"{TotalCost:F4}";
    }
}
