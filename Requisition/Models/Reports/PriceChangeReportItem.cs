using System;
using System.Text.Json.Serialization;
using Requisition.Helpers;

namespace Requisition.Models.Reports
{
    public class PriceChangeReportItem
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Unit { get; set; }
        
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
        public DateTime OldPriceDate { get; set; }
        public DateTime NewPriceDate { get; set; }
        
        public decimal PriceChange => NewPrice - OldPrice;
        public decimal PercentChange => OldPrice > 0 ? Math.Round((NewPrice - OldPrice) / OldPrice * 100, 2) : 0;
        
        public bool IsActive { get; set; }
        
        [JsonIgnore]
        public string OldPriceDisplay => $"{OldPrice:N4} ฿";
        
        [JsonIgnore]
        public string NewPriceDisplay => $"{NewPrice:N4} ฿";
        
        [JsonIgnore]
        public string PriceChangeDisplay => PriceChange >= 0 ? $"+{PriceChange:N4} ฿" : $"{PriceChange:N4} ฿";
        
        [JsonIgnore]
        public string PercentChangeDisplay => PercentChange >= 0 ? $"+{PercentChange:N2}%" : $"{PercentChange:N2}%";
        
        [JsonIgnore]
        public string OldPriceDateDisplay => ThaiDateHelper.ToThaiDateShort(OldPriceDate);
        
        [JsonIgnore]
        public string NewPriceDateDisplay => ThaiDateHelper.ToThaiDateShort(NewPriceDate);
        
        [JsonIgnore]
        public string StatusDisplay => IsActive ? "ใช้งาน" : "ไม่ใช้งาน";
    }
}
