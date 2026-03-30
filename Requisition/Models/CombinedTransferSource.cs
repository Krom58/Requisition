using System;
using System.ComponentModel.DataAnnotations;

namespace Requisition.Models
{
    /// <summary>
    /// แสดงว่า Transfer ใดบ้างที่อยู่ใน CombinedTransfer นี้
    /// </summary>
    public class CombinedTransferSource
    {
        public int Id { get; set; }
        
        public int CombinedTransferId { get; set; }
        public CombinedTransfer CombinedTransfer { get; set; } = null!;
        
        public int TransferId { get; set; }
        public Transfer Transfer { get; set; } = null!;
        
        public DateTime AddedDate { get; set; }
    }
}
