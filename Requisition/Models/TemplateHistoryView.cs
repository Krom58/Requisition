using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    public class TemplateHistoryView
    {
        public string ModifiedDate { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public string ModifiedBy { get; set; } = string.Empty;
    }
}
