using MISA.ASP.ClientApp.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec
{
    internal class SyncTaxDecSubmittedInput : BaseASPInput
    {
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public List<SyncTaxDecSubmittedInputDetail> Details { get; set; }
    }

    public class SyncTaxDecSubmittedInputDetail
    {
        public int CustomerID { get; set; }
        public string TaxCode { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public EnumTaxAccountType AccountType { get; set; } = EnumTaxAccountType.Enterprise;
    }
}
