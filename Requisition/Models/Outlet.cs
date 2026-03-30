using System;

namespace Requisition.Models
{
    public class Outlet
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal? PricePerHead { get; set; }
        public bool IsActive { get; set; } = true;
        public string? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
