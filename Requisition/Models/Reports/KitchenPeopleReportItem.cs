using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Requisition.Models.Reports
{
    public class KitchenPeopleReportItem
    {
        public string KitchenDisplay { get; set; } = string.Empty;
        public int TotalExpectedPeople { get; set; }
        public int TotalActualPeople { get; set; }
        public int TransfersCount { get; set; }

        // 🆕 เพิ่มฟิลด์นี้เพื่อใช้สำหรับการพิมพ์
        public string DateDisplay { get; set; } = string.Empty;

        [JsonIgnore]
        public int Difference => TotalActualPeople - TotalExpectedPeople;

        [JsonIgnore]
        public double PercentActualOfExpected { get; set; }

        // Display helpers for binding
        [JsonIgnore]
        public string TotalExpectedPeopleDisplay => TotalExpectedPeople.ToString();

        [JsonIgnore]
        public string TotalActualPeopleDisplay => TotalActualPeople.ToString();

        [JsonIgnore]
        public string DifferenceDisplay => Difference >= 0 ? $"+{Difference}" : Difference.ToString();

        [JsonIgnore]
        public string PercentDisplay => TotalExpectedPeople > 0 ? $"{PercentActualOfExpected:N2} %" : "-";

        [JsonIgnore]
        public string TransfersCountDisplay => $"{TransfersCount} ใบ";
    }
}
