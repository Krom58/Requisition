using System.Text.Json.Serialization;

namespace Requisition.Models.Reports
{
    public class CostUsageReportItem
    {
        public string PeriodKey { get; set; } = string.Empty;
        public string PeriodDisplay { get; set; } = string.Empty;
        
        // ✅ เพิ่ม: ห้องครัว
        public string KitchenDisplay { get; set; } = string.Empty;
        
        // ✅ เปลี่ยน: จำนวนคนต่อครั้ง (ไม่ใช่รวม)
        public int PeoplePerMeal { get; set; }
        
        // ✅ เพิ่ม: จำนวนครั้งที่ทำอาหาร
        public int MealCount { get; set; }
        
        // ✅ 1. ยอดต้นทุน (ไม่รวมต้นทุนแฝง)
        public decimal TotalCost { get; set; }
        
        // ✅ 2. ต้นทุน/หัว (ไม่รวมต้นทุนแฝง)
        public decimal CostPerHead { get; set; }
        
        // ✅ 3. ต้นทุนแฝง % (เฉลี่ยถ่วงน้ำหนัก)
        public decimal HiddenCostPercentage { get; set; }
        
        // ✅ 4. ยอดต้นทุนแฝง (บาท)
        public decimal HiddenCostAmount { get; set; }
        
        // ✅ 5. ต้นทุนแฝง/หัว
        public decimal HiddenCostPerHead { get; set; }

        [JsonIgnore]
        public string PeoplePerMealDisplay => $"{PeoplePerMeal} คน/ครั้ง";
        
        [JsonIgnore]
        public string MealCountDisplay => $"{MealCount} ครั้ง";

        [JsonIgnore]
        public string TotalCostDisplay => $"{TotalCost:N4} ฿";

        [JsonIgnore]
        public string CostPerHeadDisplay => $"{CostPerHead:N4} ฿";
        
        [JsonIgnore]
        public string HiddenCostPercentageDisplay => $"{HiddenCostPercentage:N4}%";
        
        [JsonIgnore]
        public string HiddenCostAmountDisplay => $"{HiddenCostAmount:N4} ฿";
        
        [JsonIgnore]
        public string HiddenCostPerHeadDisplay => $"{HiddenCostPerHead:N4} ฿";
    }
}
