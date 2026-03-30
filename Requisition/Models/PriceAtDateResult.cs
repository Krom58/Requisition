using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    /// <summary>
    /// ผลลัพธ์จากการดึงราคา ณ วันที่กำหนด
    /// </summary>
    public class PriceAtDateResult
    {
        public string ProductCode { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime PriceDateTime { get; set; }
        public string Source { get; set; } = string.Empty;  // "Import" หรือ "Manual"
        public int? ImportBatchId { get; set; }
    }
}
