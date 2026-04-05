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

        // Percent value (Difference / Expected) * 100.0.
        // Page code will set this using ceiling to 2 decimal places.
        [JsonIgnore]
        public double PercentActualOfExpected { get; set; }

        // Display helpers for binding
        [JsonIgnore]
        public string TotalExpectedPeopleDisplay => TotalExpectedPeople.ToString();

        [JsonIgnore]
        public string TotalActualPeopleDisplay => TotalActualPeople.ToString();

        [JsonIgnore]
        public string DifferenceDisplay => Difference >= 0 ? $"+{Difference}" : Difference.ToString();

        // Display percent with 2 decimal places. If no expected value, show '-'
        [JsonIgnore]
        public string PercentDisplay
        {
            get
            {
                if (TotalExpectedPeople <= 0)
                    return "-";
                return $"{PercentActualOfExpected:F2} %";
            }
        }

        [JsonIgnore]
        public string TransfersCountDisplay => $"{TransfersCount} ใบ";
    }
}
