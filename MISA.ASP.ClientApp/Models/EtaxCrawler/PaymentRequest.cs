using MISA.ASP.ClientApp.Utils.Converter;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MISA.ASP.ClientApp.Models.EtaxCrawler
{
    public class PaymentRequest
    {
        public string TaxCode { get; set; }
        [JsonIgnore]
        public string Order { get; set; }
        public string TransactionID { get; set; }
        public string HiddenPaymentRequestID { get; set; }
        public string PaymentRequestID { get; set; }
        public string Amount { get; set; }
        public string AmountType { get; set; }
        public string StateTitle { get; set; }
        public string RefNo { get; set; }
        public string CreatedDate { get; set; }
        public string SentDate { get; set; }
        public string TaxSubmittedDate { get; set; }
        public string BankName { get; set; }
        public string BankCode { get; set; }
        public string BankAccount { get; set; }
        [JsonConverter(typeof(BoolConverter))]
        public bool IsHideDownloadLink { get; set; }
        [JsonConverter(typeof(BoolConverter))]
        public bool HasDownloadError { get; set; }
        public string DownloadErrorMsg { get; set; }
        public string FileName { get; set; }

    }
}
