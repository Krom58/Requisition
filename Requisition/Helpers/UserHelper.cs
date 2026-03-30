using System;
using System.Threading.Tasks;

namespace Requisition.Helpers
{
    /// <summary>
    /// Helper สำหรับจัดการข้อมูลผู้ใช้
    /// </summary>
    public static class UserHelper
    {
        /// <summary>
        /// ดึงชื่อผู้ใช้ปัจจุบันจาก Windows
        /// </summary>
        public static Task<string> GetCurrentUsernameAsync()
        {
            return Task.FromResult(Environment.UserName);
        }

        /// <summary>
        /// ดึงชื่อผู้ใช้ปัจจุบันแบบ Sync
        /// </summary>
        public static string GetCurrentUsername()
        {
            return Environment.UserName;
        }
    }
}
