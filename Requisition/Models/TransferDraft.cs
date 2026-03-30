using System;
using System.Collections.Generic;

namespace Requisition.Models
{
    public class TransferDraft
    {
        public int TransferId { get; set; }
        public DateTime? UsageDate { get; set; }
        public int? OutletId { get; set; }
        public int? KitchenId { get; set; } // ✅ เพิ่มบรรทัดนี้
        public string? Notes { get; set; }
        public DateTime SavedAt { get; set; }
        public List<TransferItemDraft> Items { get; set; } = new();
    }

    public class TransferItemDraft
    {
        public int Id { get; set; } // temp negative id preserved
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal InitialQuantity { get; set; }
        public string? Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Notes { get; set; }
    }
}
