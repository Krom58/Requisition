using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Requisition.Models
{
    public class ProductModificationHistory
    {
        [Key]
        public int Id { get; set; }

        public string ProductCode { get; set; } = string.Empty;

        public DateTime ModifiedDate { get; set; } = DateTime.Now;

        [MaxLength(50)]
        public string? ModifiedBy { get; set; }

        [MaxLength(100)]
        public string? ModificationSource { get; set; }

        [MaxLength(500)]
        public string? ChangesDescription { get; set; }

        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        // Not mapped runtime fields used by UI
        [NotMapped]
        public string? SourceTable { get; set; }

        [NotMapped]
        public int? SourceId { get; set; } // PriceHistory.Id or ProductModificationHistory.Id

        // ⬇️ เพิ่มบรรทัดนี้
        [NotMapped]
        public DateTime? PriceDate { get; set; }  // วันที่ของราคาจาก PriceHistory.PriceDate

        // ⚠️ ใหม่: ฟิลด์สำหรับติดตามการแก้ไขในวันเดียวกัน
        public string? OriginalNewValues { get; set; }
        public int EditCount { get; set; }
        public bool IsModifiedToday { get; set; }
        public DateTime? LastEditDate { get; set; }
        public string? EditHistory { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
