using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models.Reports
{
    public class InactiveProductReportItem
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string DisabledReason { get; set; } = string.Empty;
        public string DisabledBy { get; set; } = string.Empty;
        public DateTime? DisabledAt { get; set; }

        public string DisabledAtDisplay => DisabledAt.HasValue ? DisabledAt.Value.ToString("dd/MM/yyyy HH:mm") : "-";
    }
}
