using System.Configuration;

namespace MISA.ASP.ClientApp.Models.Enums
{
    public static class CommonConst
    {
        public readonly static string ASP_API_URL = ConfigurationManager.AppSettings["ASP_Api_Url"] ?? "https://aspapp.misa.vn/Api/";

        /// <summary>
        /// Cờ cấu hình chạy qua luồng cũ (Thuế điện tử) thay vì Dịch vụ công.
        /// - Nếu cờ có giá trị = true: Kể cả isFromDvc = true vẫn chạy qua luồng cũ (Thuế điện tử).
        /// - Nếu không có cờ hoặc giá trị = false: Chạy theo cấu hình hiện tại.
        /// </summary>
        public static bool UseOldETaxFlow
        {
            get
            {
                var val = ConfigurationManager.AppSettings["UseOldETaxFlow"];
                return bool.TryParse(val, out bool result) && result;
            }
        }
    }
}
