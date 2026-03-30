using System;
using System.Collections.Generic;

namespace Requisition.Models
{
    public class Template
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Dining room association (nullable for older templates)
        public int? OutletId { get; set; }
        public string? OutletName { get; set; }

        // Ingredients stored as list of items (product code, name and quantity)
        public List<TemplateIngredient> Ingredients { get; set; } = new();

        // Tracking
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public int ModificationCount { get; set; } = 0;
        public bool IsDeleted { get; set; } = false;
    }

    public class TemplateIngredient
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
    }
}
