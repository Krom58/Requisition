using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    /// <summary>
    /// สถานะของใบTransfer
    /// </summary>
    public enum TransferStatus
    {
        /// <summary>แบบร่าง - ยังไม่เริ่มใช้งาน</summary>
        Draft = 0,

        /// <summary>กำลังดำเนินการ - สามารถเบิกเพิ่มได้</summary>
        InProgress = 1,

        /// <summary>จบงานแล้ว - ไม่สามารถแก้ไขได้</summary>
        Completed = 2
    }
}
