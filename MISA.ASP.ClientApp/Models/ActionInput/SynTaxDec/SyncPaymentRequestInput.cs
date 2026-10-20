using MISA.ASP.ClientApp.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec
{
    public class SyncPaymentRequestInput : BaseASPInput
    {
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public List<SyncPaymentRequestInputDetail> Details { get; set; }
        public EnumSyncFromType SyncFromType { get; set; }
    }

    public class SyncPaymentRequestInputDetail
    {
        public int CustomerID { get; set; }
        public string TaxCode { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
