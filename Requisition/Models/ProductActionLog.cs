using System;

namespace Requisition.Models
{
    public class ProductActionLog
    {
        public int Id { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string? ActionType { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Reason { get; set; }
        public string? PerformedBy { get; set; }
        public DateTime? PerformedAt { get; set; }
        public string? Source { get; set; }
        public int? RelatedId { get; set; } // ✅ เพิ่มถ้ายังไม่มี

        // Property สำหรับแสดงวันที่ที่เหมาะสม
        public DateTime? DisplayDate
        {
            get
            {
                // แสดงวันที่ที่ทำรายการ (วันที่เกิดเหตุการณ์)
                return PerformedAt;
            }
        }

        // Property สำหรับแสดงรายละเอียดตาม ActionType
        public string DisplayDetail
        {
            get
            {
                if (ActionType == "Disable")
                {
                    // ใช้ ThaiDateHelper แทนการ format เอง
                    var eff = EffectiveDate.HasValue 
                        ? ThaiDateHelper.ToThaiDate(EffectiveDate.Value) 
                        : "-";
                    return $"เหตุผล: {Reason}";
                }
                // ✅ เพิ่มการจัดการ Price Edit (History)
                else if (ActionType == "Price Edit (History)")
                {
                    return $"จาก: {OldValue ?? "-"} → {NewValue ?? "-"} (แก้ไขในประวัติ)";
                }
                else if (ActionType == "RemarksChange" || ActionType == "PriceChange")
                {
                    return $"จาก: {OldValue ?? "-"} → {NewValue ?? "-"}";
                }
                return Reason ?? string.Empty;
            }
        }
    }
}
