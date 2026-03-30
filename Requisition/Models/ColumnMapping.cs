using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    /// <summary>
    /// Column Mapping สำหรับการ Import Excel
    /// 
    /// หลักการทำงาน:
    /// 1. ถ้า Property เป็น NULL = ไม่ได้ระบุคอลัมน์ใน Excel → ไม่ Import ข้อมูลคอลัมน์นั้น (เก็บค่าเดิมไว้)
    /// 2. ถ้า Property มีค่า (ระบุคอลัมน์แล้ว):
    ///    - ถ้าเซลล์ว่าง → อัปเดตเป็น NULL
    ///    - ถ้าเซลล์มีข้อมูล → อัปเดตข้อมูลใหม่
    /// 
    /// ตัวอย่าง:
    /// - CategoryColumn = null → ไม่ต้อง Import Category (เก็บค่าเดิม)
    /// - CategoryColumn = 3 และเซลล์ว่าง → อัปเดต Category = NULL
    /// - CategoryColumn = 3 และเซลล์มีข้อมูล → อัปเดต Category = ข้อมูลใหม่
    /// </summary>
    public class ColumnMapping
    {
        /// <summary>รหัสสินค้า (Required - ต้องระบุเสมอ)</summary>
        public int? CodeColumn { get; set; }
        
        /// <summary>ชื่อสินค้า (Optional - ถ้าไม่ระบุจะเก็บค่าเดิม)</summary>
        public int? NameColumn { get; set; }
        
        /// <summary>หมวดหมู่ (Optional - ถ้าไม่ระบุจะเก็บค่าเดิม)</summary>
        public int? CategoryColumn { get; set; }
        
        /// <summary>หน่วย (Optional - ถ้าไม่ระบุจะเก็บค่าเดิม)</summary>
        public int? UnitColumn { get; set; }
        
        /// <summary>ราคา (Optional - ถ้าไม่ระบุจะเก็บค่าเดิม)</summary>
        public int? PriceColumn { get; set; }
        
        /// <summary>วันที่ราคา (Optional - ถ้าไม่ระบุจะใช้วันที่ปัจจุบัน)</summary>
        public int? PriceDateColumn { get; set; }
        
        /// <summary>หมายเหตุ (Optional - ถ้าไม่ระบุจะเก็บค่าเดิม)</summary>
        public int? RemarksColumn { get; set; }
    }
}
