using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Windows.UI;

namespace Requisition.Models
{
    /// <summary>
    /// ข้อมูลหลักของใบTransfer
    /// </summary>
    public class Transfer
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string TransferNo { get; set; } = string.Empty;
        [Required]
        public DateTime CreatedDate { get; set; }
        public int ExpectedPeople { get; set; }
        public decimal Budget { get; set; }
        public DateTime? UsageDate { get; set; }
        
        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public TransferStatus Status { get; set; } = TransferStatus.Draft;

        // Outlet fields
        public int? OutletId { get; set; }

        [MaxLength(200)]
        public string? OutletName { get; set; }

        // ✅ NEW: Kitchen fields (similar to Outlet)
        public int? KitchenId { get; set; }

        [MaxLength(200)]
        public string? KitchenName { get; set; }

        // ✅ NEW: Hidden Cost Percentage (เก็บค่า % ณ เวลาที่สร้างใบ)
        public decimal? HiddenCostPercentage { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime? CompletedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }

        // Soft Delete fields
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedDate { get; set; }
        
        [MaxLength(100)]
        public string? DeletedBy { get; set; }
        
        [MaxLength(500)]
        public string? DeletedReason { get; set; }

        [JsonIgnore]
        public List<TransferItem> Items { get; set; } = new();

        [JsonIgnore]
        public int ItemCount => Items?.Count ?? 0;

        [JsonIgnore]
        public decimal TotalQuantity => Items?.Sum(i => i.TotalIssuedQuantity) ?? 0;

        [JsonIgnore]
        public decimal? TotalReturnedQuantity => Items?.Sum(i => i.ReturnedQuantity ?? 0);

        [JsonIgnore]
        public bool CanEdit => Status == TransferStatus.Draft && !IsDeleted;

        [JsonIgnore]
        public bool CanReturn => Status == TransferStatus.InProgress && !IsDeleted;

        [JsonIgnore]
        public string StatusText => Status switch
        {
            TransferStatus.Draft => "แบบร่าง",
            TransferStatus.InProgress => "กำลังดำเนินการ",
            TransferStatus.Completed => "จบงานแล้ว",
            _ => "ไม่ทราบสถานะ"
        };

        [JsonIgnore]
        public string DisplayStatus => IsDeleted ? "🗑️ ถูกลบ" : StatusText;
        
        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility EditVisibility => 
            Status == TransferStatus.InProgress 
                ? Microsoft.UI.Xaml.Visibility.Visible 
                : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility DeleteVisibility => 
            Status == TransferStatus.Draft && ItemCount == 0 
                ? Microsoft.UI.Xaml.Visibility.Visible 
                : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public Color StatusColor => Status switch
        {
            TransferStatus.Draft => Color.FromArgb(255, 156, 163, 175),
            TransferStatus.InProgress => Color.FromArgb(255, 59, 130, 246),
            TransferStatus.Completed => Color.FromArgb(255, 16, 185, 129),
            _ => Color.FromArgb(255, 128, 128, 128)
        };

        [JsonIgnore]
        public Brush StatusBrush => new SolidColorBrush(StatusColor);

        [JsonIgnore]
        public string CreatedDateDisplay => ThaiDateHelper.ToThaiDateTimeShort(CreatedDate);  // ✅ แก้ไขตรงนี้

        [JsonIgnore]
        public string TotalQuantityDisplay => $"{TotalQuantity:N4} หน่วย";

        [JsonIgnore]
        public string TotalReturnedDisplay => TotalReturnedQuantity.HasValue 
            ? $"{TotalReturnedQuantity.Value:N4} หน่วย" 
            : "0.0000 หน่วย";

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility CompleteButtonVisibility => 
            Status == TransferStatus.InProgress 
                ? Microsoft.UI.Xaml.Visibility.Visible 
                : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public string UsageDateDisplay => ThaiDateHelper.ToThaiDateShortOrDefault(UsageDate, "ไม่ระบุ");  // ✅ แก้ไขตรงนี้

        [JsonIgnore]
        public string BudgetDisplay => $"{Budget:N4} ฿";

        [JsonIgnore]
        public decimal TotalCost
        {
            get
            {
                if (Items == null || Items.Count == 0)
                    return 0;

                return Items.Sum(item => item.TotalCost);
            }
        }

        [JsonIgnore]
        public string BudgetUsageDisplay => $"{TotalCost:N4} / {Budget:N4} ฿";

        [JsonIgnore]
        public double BudgetUsagePercent => Budget > 0 ? (double)(TotalCost / Budget * 100) : 0;

        [JsonIgnore]
        public bool IsBudgetExceeded => TotalCost > Budget;

        [JsonIgnore]
        public Color BudgetStatusColor
        {
            get
            {
                if (IsBudgetExceeded)
                    return Color.FromArgb(255, 220, 38, 38);
                else if (BudgetUsagePercent >= 80)
                    return Color.FromArgb(255, 234, 179, 8);
                else
                    return Color.FromArgb(255, 34, 197, 94);
            }
        }

        [JsonIgnore]
        public Brush BudgetStatusBrush => new SolidColorBrush(BudgetStatusColor);

        [JsonIgnore]
        public decimal CostPerPerson
        {
            get
            {
                if (ExpectedPeople <= 0) return 0;
                decimal totalCost = TotalCost;
                return totalCost / ExpectedPeople;
            }
        }

        [JsonIgnore]
        public string CostPerPersonDisplay => $"{CostPerPerson:N4} ฿/คน";

        public int? ActualPeople { get; set; }

        [JsonIgnore]
        public decimal CostPerActualPerson
        {
            get
            {
                if (ActualPeople == null || ActualPeople.GetValueOrDefault() <= 0) return 0;
                decimal totalCost = TotalCost;
                return Math.Round(totalCost / ActualPeople.Value, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
            }
        }

        [JsonIgnore]
        public string CostPerActualPersonDisplay => $"{CostPerActualPerson:N4} ฿/คน";

        [JsonIgnore]
        public string OutletDisplay => OutletName ?? (OutletId.HasValue ? $"Outlet #{OutletId}" : "ไม่ระบุOutlet");

        // ✅ NEW: Kitchen display helper
        [JsonIgnore]
        public string KitchenDisplay => KitchenName ?? (KitchenId.HasValue ? $"Kitchen #{KitchenId}" : "ไม่ระบุห้องครัว");

        public decimal? OutletPricePerHeadAtSave { get; set; }
        public DateTime? OutletPricePerHeadSavedAt { get; set; }
    }
}
