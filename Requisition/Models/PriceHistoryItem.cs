using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    /// <summary>
    /// รายการประวัติราคา
    /// </summary>
    public class PriceHistoryItem
    {
        public decimal Price { get; set; }
        public DateTime Date { get; set; }
        public string Source { get; set; } = string.Empty;
        public int? ImportBatchId { get; set; }
        
        // สำหรับแสดงใน UI
        public string DisplayDate => Date.ToString("dd/MM/yyyy HH:mm");
        public string DisplayPrice => $"{Price:N4} ฿";
        public string DisplaySource => Source == "Import" ? "นำเข้า Excel" : "แก้ไขเอง";
    }
}
