using System;
using System.ComponentModel.DataAnnotations;

namespace Requisition.Models
{
    public class CombinedTransfer
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string CombinedNo { get; set; } = string.Empty;
        
        public DateTime CreatedDate { get; set; }
        
        [MaxLength(100)]
        public string? CreatedBy { get; set; }
        
        public int CombinedCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalCost { get; set; }
        public int? TotalPeople { get; set; }
        public decimal? CombinedCostPerHead { get; set; }
        
        [MaxLength(500)]
        public string? Reason { get; set; }
        
        [MaxLength(500)]
        public string? Notes { get; set; }  // เพิ่มสำหรับ backward compatibility
        
        // Soft Delete
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedDate { get; set; }
        
        [MaxLength(100)]
        public string? DeletedBy { get; set; }
        
        [MaxLength(500)]
        public string? DeletedReason { get; set; }
    }
}
