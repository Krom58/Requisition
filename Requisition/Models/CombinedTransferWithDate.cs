using System;

namespace Requisition.Models
{
    /// <summary>
    /// CombinedTransfer พร้อม DateTime สำหรับการกรองตามวันที่
    /// </summary>
    public class CombinedTransferWithDate
    {
        public int Id { get; set; }
        public string CombinedNo { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public bool IsDeleted { get; set; }
    }
}
