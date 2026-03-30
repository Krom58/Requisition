using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    public class ImportError
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int? ExcelRow { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty; // "Database", "Validation", "Format", etc.
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int NewProducts { get; set; }
        public int UpdatedPrices { get; set; }
        public List<ImportError> Errors { get; set; } = new();
    }
}
