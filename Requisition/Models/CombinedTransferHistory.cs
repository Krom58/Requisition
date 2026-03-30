using System;

namespace Requisition.Models
{
    public class CombinedTransferHistory
    {
        public int Id { get; set; }
        public int CombinedTransferId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
    }
}
