using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Requisition.Models
{
    // ========== ViewModels สำหรับหน้า List ==========
    
    /// <summary>
    /// ViewModel สำหรับแสดงรายการ CombinedTransfer ในหน้า List
    /// </summary>
    public class CombinedTransferListViewModel
    {
        public int Id { get; set; }
        public string CombinedNo { get; set; } = string.Empty;
        public string CreatedDate { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = "ไม่ระบุ";
        public int CombinedCount { get; set; }
        public string CombinedCountDisplay => $"{CombinedCount} ใบ";
        
        public string TransferNosDisplay { get; set; } = string.Empty;
        
        // 🔥 เพิ่มฟิลด์ใหม่
        public string OutletsDisplay { get; set; } = "-";
        public string KitchensDisplay { get; set; } = "-";
        public List<int> OutletIds { get; set; } = new();
        public List<int> KitchenIds { get; set; } = new();
        
        public string? Reason { get; set; }
        public string ReasonDisplay => string.IsNullOrWhiteSpace(Reason) ? "-" : Reason;
        
        public decimal TotalCost { get; set; }
        public string TotalCostDisplay => $"{TotalCost:N4} ฿";
        
        // 🔥 Properties ใหม่สำหรับจำนวนคน (เช็คความสอดคล้อง)
        public int? PeopleCount { get; set; }
        public bool HasInconsistentPeopleCount { get; set; }
        public List<int> AllPeopleCounts { get; set; } = new();
        
        public string PeopleCountDisplay
        {
            get
            {
                if (HasInconsistentPeopleCount)
                {
                    // แสดงช่วงจำนวนคน
                    if (AllPeopleCounts.Count > 0)
                    {
                        var min = AllPeopleCounts.Min();
                        var max = AllPeopleCounts.Max();
                        return $"{min}-{max} คน";
                    }
                    return "ไม่สอดคล้อง";
                }
                return PeopleCount.HasValue ? $"{PeopleCount} คน" : "-";
            }
        }
        
        public Visibility PeopleCountWarningVisibility => 
            HasInconsistentPeopleCount ? Visibility.Visible : Visibility.Collapsed;
        
        // 🔥 ราคา/หัว (ใช้จำนวนคนที่สอดคล้อง หรือค่าเฉลี่ย)
        public decimal? CombinedCostPerHead { get; set; }
        public string CostPerHeadDisplay => CombinedCostPerHead.HasValue 
            ? $"{CombinedCostPerHead:N4} ฿/คน" 
            : "-";
        
        // 🔥 รวมแต่ละใบ - แสดงยอดเงินของแต่ละใบ
        public string IndividualTransferCostsDisplay { get; set; } = "-";
        
        // 🔥 เปลี่ยนจาก งบตาม outlet เป็น ราคาต่อหัวของ Outlet
        public string OutletPricePerHeadDisplay { get; set; } = "-";
        
        public bool IsDeleted { get; set; }
        
        public Visibility ActionButtonsVisibility => IsDeleted ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DeletedBadgeVisibility => IsDeleted ? Visibility.Visible : Visibility.Collapsed;
    }
    
    // ========== ViewModels สำหรับหน้าเลือก Transfer ==========

    /// <summary>
    /// ViewModel สำหรับเลือก Transfer ในหน้าสร้างใบรวม
    /// </summary>
    public class SelectableTransferViewModel : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string TransferNo { get; set; } = string.Empty;
        public int? OutletId { get; set; } // NEW
        public string OutletName { get; set; } = "-";
        public string KitchenName { get; set; } = "-";
        public DateTime CreatedDate { get; set; }
        public string CreatedDateDisplay => ThaiDateHelper.ToThaiDateTimeShort(CreatedDate);  // ✅ แก้ไขตรงนี้
        
        public DateTime? UsageDate { get; set; }
        public string UsageDateDisplay => ThaiDateHelper.ToThaiDateShortOrDefault(UsageDate);  // ✅ แก้ไขตรงนี้
        
        public int ExpectedPeople { get; set; }
        public string ExpectedPeopleDisplay => $"{ExpectedPeople} คน";
        
        public int? ActualPeople { get; set; } // NEW: ผู้มาใช้จริง
        public string ActualPeopleDisplay => ActualPeople.HasValue ? $"{ActualPeople} คน" : "ยังไม่ระบุ";
        
        public decimal TotalQuantity { get; set; }
        public string TotalQuantityDisplay => $"{TotalQuantity:N4}";
        
        public decimal TotalCost { get; set; }
        public string TotalCostDisplay => $"{TotalCost:N4} ฿";
        
        public decimal? CostPerPerson { get; set; }
        public string CostPerPersonDisplay => CostPerPerson.HasValue 
            ? $"{CostPerPerson:N4} ฿/คน" 
            : "-";

        public decimal? OutletPricePerHead { get; set; }
        public string OutletPricePerHeadDisplay => OutletPricePerHead.HasValue
            ? $"{OutletPricePerHead:N4} ฿"
            : "-";
        
        public SolidColorBrush OutletPriceBrush
        {
            get
            {
                if (!CostPerPerson.HasValue || !OutletPricePerHead.HasValue)
                    return new SolidColorBrush(Microsoft.UI.Colors.Gray);

                return CostPerPerson > OutletPricePerHead
                    ? new SolidColorBrush(Microsoft.UI.Colors.Red)
                    : new SolidColorBrush(Microsoft.UI.Colors.Green);
            }
        }
        
        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        
        private bool _isAlreadyCombined;
        public bool IsAlreadyCombined 
        { 
            get => _isAlreadyCombined;
            set
            {
                if (_isAlreadyCombined != value)
                {
                    _isAlreadyCombined = value;
                    OnPropertyChanged(nameof(IsAlreadyCombined));
                    OnPropertyChanged(nameof(IsSelectable));
                }
            }
        }
        
        public bool IsSelectable => !IsAlreadyCombined;

        // NEW: bound combined info (if this Transfer already belongs to a combined)
        public int? BoundCombinedId { get; set; }
        public string? BoundCombinedNo { get; set; }
        public string BoundCombinedDisplay => BoundCombinedId.HasValue ? (BoundCombinedNo ?? "-") : "-";
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    // ========== ViewModels สำหรับหน้ารายละเอียด ==========

    /// <summary>
    /// ViewModel สำหรับแสดงรายละเอียดใบรวม
    /// </summary>
    public class CombinedTransferDetailViewModel
    {
        public int Id { get; set; }
        public string CombinedNo { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? Reason { get; set; }
        
        // 🔥 ข้อมูลสรุป
        public int TransferCount { get; set; }
        public int? ConsistentPeopleCount { get; set; } // จำนวนคนถ้าเท่ากันทุกใบ
        public bool HasInconsistentPeople { get; set; } // flag ว่าจำนวนคนไม่เท่ากัน
        public List<int> AllPeopleCounts { get; set; } = new(); // จำนวนคนของทุกใบ
        public decimal TotalCost { get; set; }
        public decimal? CostPerHead { get; set; } // ราคาต่อหัวจากจำนวนคนที่กำหนด
        
        // 🔥 ข้อมูลผู้มาใช้จริง
        public int? TotalActualPeople { get; set; } // รวมผู้มาใช้จริงทั้งหมด
        public bool HasInconsistentActualPeople { get; set; }
        public List<int> AllActualPeopleCounts { get; set; } = new();
        public decimal? ActualCostPerHead { get; set; } // ราคาต่อหัวจากผู้มาใช้จริง
        
        // 🔥 รวมแต่ละใบ
        public string IndividualCostsDisplay { get; set; } = "-";
        
        // 🔥 งบตาม Outlet
        public string OutletBudgetsDisplay { get; set; } = "-";
        public decimal? MaxOutletBudget { get; set; } // งบสูงสุดสำหรับเปรียบเทียบ
        
        // รายการ Transfer และ Items
        public List<TransferSummaryViewModel> Transfers { get; set; } = new();
        public List<CombinedItemViewModel> CombinedItems { get; set; } = new();
    }
    
    /// <summary>
    /// ViewModel สำหรับสรุปแต่ละ Transfer ในใบรวม
    /// </summary>
    public class TransferSummaryViewModel
    {
        public int Id { get; set; }
        public string TransferNo { get; set; } = string.Empty;
        public string OutletName { get; set; } = "-";
        public string KitchenName { get; set; } = "-"; // 🔥 เพิ่มชื่อครัว
        public DateTime? UsageDate { get; set; }
        public string UsageDateDisplay => UsageDate?.ToString("dd/MM/yyyy") ?? "-";
        
        public int ExpectedPeople { get; set; }
        public int? ActualPeople { get; set; } // 🔥 ผู้มาใช้จริง
        public string ActualPeopleDisplay => ActualPeople.HasValue ? $"{ActualPeople} คน" : "ยังไม่ระบุ";
        
        public decimal TotalQuantity { get; set; }
        public decimal TotalCost { get; set; }
        public decimal? CostPerPerson { get; set; }
        public string CostPerPersonDisplay => CostPerPerson.HasValue ? $"{CostPerPerson:N4} ฿" : "-";
        
        public decimal? OutletPricePerHead { get; set; }
        public string OutletPricePerHeadDisplay => OutletPricePerHead.HasValue ? $"{OutletPricePerHead:N4} ฿/คน" : "-";
        
        public decimal? HiddenCostPercentage { get; set; }  // ✅ เพิ่มบรรทัดนี้
    }
    
    /// <summary>
    /// ViewModel สำหรับแสดงสินค้าที่รวมจากหลายใบ
    /// </summary>
    public class CombinedItemViewModel
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        
        public decimal TotalQuantity { get; set; }
        
        public decimal AverageUnitCost { get; set; }
        public string AverageUnitCostDisplay => $"{AverageUnitCost:N4} ฿";
        
        public decimal TotalCost { get; set; }
        public string TotalCostDisplay => $"{TotalCost:N4} ฿";
        
        public List<ItemSourceInfo> Sources { get; set; } = new();
        
        public string SourcesTooltip
        {
            get
            {
                if (Sources.Count == 0) return "ไม่มีข้อมูล";
                if (Sources.Count == 1) 
                    return $"จาก: {Sources[0].TransferNo}\nจำนวน: {Sources[0].Quantity:N4} {Unit}";
                
                return $"มาจาก {Sources.Count} ใบ:\n" + 
                       string.Join("\n", Sources.Select(s => $"• {s.TransferNo}: {s.Quantity:N4} {Unit}"));
            }
        }
    }
    
    /// <summary>
    /// ข้อมูลที่มาของสินค้าแต่ละรายการ
    /// </summary>
    public class ItemSourceInfo
    {
        public string TransferNo { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
    }
    
    // ========== ViewModels สำหรับหน้าเก่า (CombineTransfersPage) ==========
    
    /// <summary>
    /// ViewModel สำหรับ Transfer ในหน้าเก่า (backward compatibility)
    /// </summary>
    public class TransferViewModel : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string TransferNo { get; set; } = string.Empty;
        public string OutletDisplay { get; set; } = string.Empty;
        public string CreatedDateDisplay { get; set; } = string.Empty;
        public string UsageDateDisplay { get; set; } = string.Empty;
        public string CostPerPersonDisplay { get; set; } = string.Empty;
        public string OutletPricePerHeadDisplay { get; set; } = string.Empty;
        public string TotalQuantityDisplay { get; set; } = string.Empty;
        public string TotalCostDisplay { get; set; } = string.Empty;
        
        public decimal? CostPerPerson { get; set; }
        public decimal? OutletPricePerHead { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalCost { get; set; }
        
        public SolidColorBrush OutletPriceBrush
        {
            get
            {
                if (!CostPerPerson.HasValue || !OutletPricePerHead.HasValue)
                    return new SolidColorBrush(Microsoft.UI.Colors.Gray);

                return CostPerPerson > OutletPricePerHead
                    ? new SolidColorBrush(Microsoft.UI.Colors.Red)
                    : new SolidColorBrush(Microsoft.UI.Colors.Green);
            }
        }
        
        private bool _isSelectable = true;
        public bool IsSelectable
        {
            get => _isSelectable;
            set
            {
                if (_isSelectable != value)
                {
                    _isSelectable = value;
                    OnPropertyChanged(nameof(IsSelectable));
                }
            }
        }
        
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        
        public string DisplayTitle => TransferNo;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// ViewModel สำหรับประวัติการรวม (backward compatibility)
    /// </summary>
    public class CombinedHistoryViewModel
    {
        public int Id { get; set; }
        public string CombinedNo { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public string CreatedDateDisplay => CreatedDate == DateTime.MinValue 
            ? "-" 
            : ThaiDateHelper.ToThaiDateShort(CreatedDate) + " " + CreatedDate.ToString("HH:mm");
        public int CombinedCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public string TotalQuantityDisplay => $"{TotalQuantity:N4}";
        public decimal TotalCost { get; set; }
        public string TotalCostDisplay => $"{TotalCost:N4} ฿";
    }
    
    public class CombinedTransferHistoryViewModel
    {
        public int Id { get; set; }
        public int CombinedTransferId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        public string ModifiedDateDisplay => ModifiedDate == DateTime.MinValue 
            ? "-" 
            : ThaiDateHelper.ToThaiDateShort(ModifiedDate) + " " + ModifiedDate.ToString("HH:mm");

        // Aliases expected by UI
        public string ActionType => Action;
        public string? PerformedBy => ModifiedBy;
        public string PerformedAtDisplay => ModifiedDate == DateTime.MinValue 
            ? "-" 
            : ThaiDateHelper.ToThaiDateShort(ModifiedDate) + " " + ModifiedDate.ToString("HH:mm");
        public string? Details => Description;
    }
}
