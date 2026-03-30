using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Requisition.Models
{
    /// <summary>
    /// รายการสินค้าในใบTransfer (เป็น observable เพื่อรองรับ TwoWay binding)
    /// </summary>
    public class TransferItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _id;
        public int Id { get => _id; set { if (_id == value) return; _id = value; OnPropertyChanged(nameof(Id)); } }

        private int _transferId;
        public int TransferId { get => _transferId; set { if (_transferId == value) return; _transferId = value; OnPropertyChanged(nameof(TransferId)); } }

        private string _productCode = string.Empty;
        [Required]
        [MaxLength(50)]
        public string ProductCode { get => _productCode; set { if (_productCode == value) return; _productCode = value; OnPropertyChanged(nameof(ProductCode)); } }

        private string _productName = string.Empty;
        [Required]
        [MaxLength(200)]
        public string ProductName { get => _productName; set { if (_productName == value) return; _productName = value; OnPropertyChanged(nameof(ProductName)); } }

        private decimal _initialQuantity;
        public decimal InitialQuantity
        {
            get => _initialQuantity;
            set
            {
                if (_initialQuantity == value) return;
                _initialQuantity = value;
                OnPropertyChanged(nameof(InitialQuantity));
                NotifyQuantityDependentProperties();
            }
        }

        private decimal _additionalQuantity;
        public decimal AdditionalQuantity
        {
            get => _additionalQuantity;
            set
            {
                if (_additionalQuantity == value) return;
                _additionalQuantity = value;
                OnPropertyChanged(nameof(AdditionalQuantity));
                NotifyQuantityDependentProperties();
            }
        }

        private decimal? _returnedQuantity;
        public decimal? ReturnedQuantity
        {
            get => _returnedQuantity;
            set
            {
                if (_returnedQuantity == value) return;
                _returnedQuantity = value;
                OnPropertyChanged(nameof(ReturnedQuantity));
                NotifyQuantityDependentProperties();
            }
        }

        [MaxLength(50)]
        private string? _unit;
        public string? Unit { get => _unit; set { if (_unit == value) return; _unit = value; OnPropertyChanged(nameof(Unit)); } }

        [MaxLength(200)]
        private string? _notes;
        public string? Notes { get => _notes; set { if (_notes == value) return; _notes = value; OnPropertyChanged(nameof(Notes)); } }

        // ราคาและวันที่
        private decimal? _unitPrice;
        public decimal? UnitPrice
        {
            get => _unitPrice;
            set
            {
                if (_unitPrice == value) return;
                _unitPrice = value;
                OnPropertyChanged(nameof(UnitPrice));
                NotifyPriceDependentProperties();
            }
        }

        private DateTime? _priceDate;
        public DateTime? PriceDate { get => _priceDate; set { if (_priceDate == value) return; _priceDate = value; OnPropertyChanged(nameof(PriceDate)); } }

        // UI-only (ไม่บันทึกใน DB)
        [JsonIgnore]
        private decimal? _currentPrice;
        [JsonIgnore]
        public decimal? CurrentPrice { get => _currentPrice; set { if (_currentPrice == value) return; _currentPrice = value; OnPropertyChanged(nameof(CurrentPrice)); NotifyPriceDependentProperties(); } }

        public decimal TotalReturnedQuantity => ReturnedQuantity ?? 0;

        // Computed properties
        public decimal TotalIssuedQuantity => InitialQuantity + AdditionalQuantity;
        public decimal TotalQuantity => TotalIssuedQuantity;

        public decimal RemainingQuantity
        {
            get
            {
                decimal returned = ReturnedQuantity ?? 0;
                return TotalIssuedQuantity - returned;
            }
        }

        public decimal AvailableToReturn => RemainingQuantity;
        public bool CanReturn => RemainingQuantity > 0;
        public bool IsFullyReturned => ReturnedQuantity.HasValue && RemainingQuantity <= 0;

        public string QuantitySummary
        {
            get
            {
                var summary = $"เบิก: {TotalIssuedQuantity:N4}";
                if (ReturnedQuantity.HasValue)
                {
                    summary += $" | คืน: {ReturnedQuantity.Value:N4} | คงเหลือ: {RemainingQuantity:N4}";
                }
                summary += $" {Unit ?? ""}";
                return summary;
            }
        }

        // ใช้ UnitPrice ที่บันทึกไว้แทน CurrentPrice
        [JsonIgnore]
        public decimal TotalCost => Math.Round(RemainingQuantity * (UnitPrice ?? 0), 4, MidpointRounding.AwayFromZero);

        [JsonIgnore]
        public string UnitCostDisplay => UnitPrice.HasValue ? $"{UnitPrice.Value:N4} ฿" : "ไม่ระบุ";

        [JsonIgnore]
        public string TotalCostDisplay => TotalCost > 0 ? $"{TotalCost:N4} ฿" : "-";

        // แสดงวันที่ของราคา
        [JsonIgnore]
        public string PriceDateDisplay => PriceDate.HasValue 
            ? ThaiDateHelper.ToThaiDateShort(PriceDate.Value) 
            : "-";

        // ✅ เพิ่ม: จำนวนสุทธิ (หลังหักคืน) สำหรับการแสดงผล
        [JsonIgnore]
        public double RemainingQuantityDouble => (double)RemainingQuantity;

        // ✅ เพิ่ม: จำนวนเบิกทั้งหมด (ไม่หักคืน) สำหรับการแสดงผล
        [JsonIgnore]
        public double InitialQuantityDouble => (double)InitialQuantity;

        private decimal _returnQuantity;
        [JsonIgnore]
        public decimal ReturnQuantity
        {
            get => _returnQuantity;
            set
            {
                if (_returnQuantity == value) return;
                _returnQuantity = Math.Round(value, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                OnPropertyChanged(nameof(ReturnQuantity));
                OnPropertyChanged(nameof(ReturnQuantityDouble));
            }
        }

        [JsonIgnore]
        public double ReturnQuantityDouble
        {
            get => (double)_returnQuantity;
            set
            {
                var dec = Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                if (_returnQuantity == dec) return;
                _returnQuantity = dec;
                OnPropertyChanged(nameof(ReturnQuantity));
                OnPropertyChanged(nameof(ReturnQuantityDouble));
            }
        }

        [JsonIgnore]
        public decimal RemainingAfterPendingReturn
        {
            get
            {
                // RemainingQuantity คำนวณจาก ReturnedQuantity (persisted)
                // เอา pending return (จาก UI) มาหักอีกครั้ง
                decimal rem = RemainingQuantity - ReturnQuantity;
                if (rem < 0) rem = 0;
                return Math.Round(rem, 4, MidpointRounding.AwayFromZero); // ✅ เพิ่ม
            }
        }

        [JsonIgnore]
        public double RemainingAfterPendingReturnDouble => (double)RemainingAfterPendingReturn;

        public bool CanReturnQuantity(decimal quantity, out string? errorMessage)
        {
            if (quantity <= 0)
            {
                errorMessage = "จำนวนที่คืนต้องมากกว่า 0";
                return false;
            }

            if (quantity > AvailableToReturn)
            {
                errorMessage = $"ไม่สามารถคืนได้ {quantity:N4} {Unit} มีเพียง {AvailableToReturn:N4} {Unit} ที่สามารถคืนได้";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public bool CanAddMoreQuantity(decimal quantity, out string? errorMessage)
        {
            if (quantity <= 0)
            {
                errorMessage = "จำนวนที่เบิกเพิ่มต้องมากกว่า 0";
                return false;
            }

            errorMessage = null;
            return true;
        }

        // Double helpers (InitialQuantityDouble ให้เป็น read/write เพื่อใช้ TwoWay binding กับ NumberBox)
        public double TotalIssuedQuantityDouble => (double)TotalIssuedQuantity;
        public double AvailableToReturnDouble => (double)AvailableToReturn;
        public double AdditionalQuantityDouble => (double)AdditionalQuantity;
        public double ReturnedQuantityDouble => (double)(ReturnedQuantity ?? 0m);

        public Visibility CanShowActionButtons => Visibility.Visible;

        protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        private void NotifyQuantityDependentProperties()
        {
            OnPropertyChanged(nameof(TotalIssuedQuantity));
            OnPropertyChanged(nameof(TotalQuantity));
            OnPropertyChanged(nameof(RemainingQuantity));
            OnPropertyChanged(nameof(AvailableToReturn));
            OnPropertyChanged(nameof(CanReturn));
            OnPropertyChanged(nameof(IsFullyReturned));
            OnPropertyChanged(nameof(QuantitySummary));
            OnPropertyChanged(nameof(TotalCost));
            OnPropertyChanged(nameof(TotalCostDisplay));
            OnPropertyChanged(nameof(TotalIssuedQuantityDouble));
            OnPropertyChanged(nameof(RemainingQuantityDouble));
            OnPropertyChanged(nameof(AvailableToReturnDouble));
            OnPropertyChanged(nameof(InitialQuantityDouble));
            OnPropertyChanged(nameof(AdditionalQuantityDouble));
            OnPropertyChanged(nameof(ReturnedQuantityDouble));
            OnPropertyChanged(nameof(RemainingAfterPendingReturn));
            OnPropertyChanged(nameof(RemainingAfterPendingReturnDouble));
        }

        private void NotifyPriceDependentProperties()
        {
            OnPropertyChanged(nameof(TotalCost));
            OnPropertyChanged(nameof(TotalCostDisplay));
            OnPropertyChanged(nameof(UnitCostDisplay));
        }
    }
}
