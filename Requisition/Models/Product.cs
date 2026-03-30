using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Requisition.Models
{
    public class Product
    {
        // Properties ที่ใช้กับ SQL Server Schema
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Unit { get; set; }
        public decimal? Price { get; set; }
        
        // ✅ เพิ่ม: ต้นทุนต่อหน่วย (สำหรับคำนวณในรายงาน)
        public decimal UnitCost { get; set; } = 0;
        
        /// <summary>
        /// วันที่ราคา - เก็บเป็น DateTime (ค.ศ.) สำหรับบันทึกลงฐานข้อมูล
        /// </summary>
        public DateTime? PriceDate { get; set; }
        
        /// <summary>
        /// Raw value read from the Excel cell (e.g. "25/01/2568") — used for preview display.
        /// </summary>
        public string? PriceDateRaw { get; set; }

        public string? Remarks { get; set; }
        public bool IsActive { get; set; } = true;

        // สำหรับ tracking
        public DateTime? CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public int ModificationCount { get; set; } = 0;

        // UI helper: prefer raw text for preview; otherwise show BE formatted date for consistency
        public string DisplayPriceDate
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(PriceDateRaw))
                    return PriceDateRaw!;
                if (PriceDate.HasValue)
                {
                    // ใช้ ThaiDateHelper แทนการคำนวณเอง
                    return ThaiDateHelper.ToThaiDate(PriceDate.Value);
                }
                return string.Empty;
            }
        }

        public int? ExcelRow { get; set; }

        /// <summary>
        /// If set, the product is considered disabled until this date (exclusive).
        /// When DisabledUntil &gt; Today the product should be hidden / inactive for requisitions.
        /// </summary>
        public DateTime? DisabledUntil { get; set; }

        [NotMapped]
        public bool IsCurrentlyDisabled => DisabledUntil.HasValue && DisabledUntil.Value.Date > DateTime.Today;

        [NotMapped]
        public bool CanDisable => !IsCurrentlyDisabled;
    }
}
