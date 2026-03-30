using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Requisition.Models
{
    /// <summary>
    /// ประวัติการแก้ไขใบTransfer
    /// </summary>
    public class TransferHistory
    {
        [Key]
        public int Id { get; set; }

        public int TransferId { get; set; }

        public DateTime ModifiedDate { get; set; } = DateTime.Now;

        [MaxLength(100)]
        public string? ModifiedBy { get; set; }

        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>ค่าเก่า (JSON format)</summary>
        public string? OldValues { get; set; }

        /// <summary>ค่าใหม่ (JSON format)</summary>
        public string? NewValues { get; set; }

        // Computed properties
        /// <summary>ข้อความ Action ภาษาไทย</summary>
        public string ActionText => Action switch
        {
            "Created" => "สร้างใบTransfer",
            "AddedItem" => "เพิ่มรายการสินค้า",
            "UpdatedItem" => "แก้ไขรายการสินค้า",
            "RemovedItem" => "ลบรายการสินค้า",
            "AddedMore" => "เบิกเพิ่ม",
            "Completed" => "จบงาน",
            "Reopened" => "เปิดใบTransferอีกครั้ง",
            _ => Action
        };

        /// <summary>
        /// Comparison text prepared by UI layer (not stored in DB).
        /// It can contain human readable diffs derived from OldValues/NewValues JSON.
        /// </summary>
        [NotMapped]
        public string? Comparison { get; set; }
    }
}
