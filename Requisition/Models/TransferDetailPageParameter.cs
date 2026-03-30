using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Models
{
    /// <summary>
    /// Navigation parameter for TransferDetailPage
    /// </summary>
    public class TransferDetailPageParameter
    {
        public int TransferId { get; set; }
        public bool IsReadOnly { get; set; }

        public TransferDetailPageParameter(int transferId, bool isReadOnly = false)
        {
            TransferId = transferId;
            IsReadOnly = isReadOnly;
        }
    }

    /// <summary>
    /// Static Event Hub สำหรับแจ้งเตือนการเปลี่ยนแปลงใบTransfer
    /// </summary>
    public static class TransferEvents
    {
        public static event EventHandler<int>? TransferChanged;

        public static void NotifyTransferChanged(int transferId)
        {
            System.Diagnostics.Debug.WriteLine($"🔔 Firing TransferChanged event for ID={transferId}");
            System.Diagnostics.Debug.WriteLine($"   Subscribers: {TransferChanged?.GetInvocationList().Length ?? 0}");

            TransferChanged?.Invoke(null, transferId);
        }
    }
}
