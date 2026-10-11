using MISA.ASP.ClientApp.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec
{
    public class ViewPaymentRequestFileInput : BaseASPInput
    {
        public int CustomerID { get; set; }
        public string FileName { get; set; }

        public FileTypeEnum FileType { get; set; }
    }
}
