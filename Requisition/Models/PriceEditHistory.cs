using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    public class PriceEditHistory
    {
        public DateTime EditDate { get; set; }
        public string EditBy { get; set; } = string.Empty;
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
        public string? Remarks { get; set; }
    }
}
