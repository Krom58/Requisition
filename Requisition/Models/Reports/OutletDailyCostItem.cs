using System;

namespace Requisition.Models.Reports
{
    // Aggregated daily ABF totals for a single outlet
    public class OutletDailyCostItem
    {
        public DateTime UsageDate { get; set; }
        public int? OutletId { get; set; }
        public string OutletName { get; set; } = string.Empty;

        // total ABF cost for that date + outlet
        public decimal TotalCost { get; set; }

        // จำนวนผู้มีสิทธิ์ใช้ (คาดการณ์)
        public int ExpectedPeople { get; set; }

        // จำนวนผู้มาใช้จริง
        public int ActualPeople { get; set; }

        // ปริมาณสำหรับประเภทที่ผู้ใช้เลือก (ถ้าเลือก) — aggregated quantity
        public decimal CategoryQuantity { get; set; }

        // unit สำหรับ Category (ถ้ามี)
        public string? Unit { get; set; }

        // Hidden cost percentage (nullable). Stored as percent value e.g. 15 => 15%
        public decimal? HiddenCostPercentage { get; set; }

        // New: aggregated meat and egg quantities for that date+outlet
        // MeatQuantity expected in kilograms (kg)
        public decimal MeatQuantity { get; set; }

        // EggQuantity expected in pieces (ฟอง)
        public decimal EggQuantity { get; set; }
    }
}
