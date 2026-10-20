using System;
using System.Collections.Generic;
using System.Text;

namespace MISA.ASP.ClientApp.Models.Enums
{
    public enum FileTypeEnum
    {
        Temp = 0,
        TaxDeclaration = 28,
        TaxNotification = 29,
    }

    public enum CrawlErrorType
    {
        InvalidCaptcha = 1,
        InvalidIdentity = 2,
        InvalidTaxCode = 3,
        PasswordExpired = 4,
        UnRecognizedMainPage = 5,
        Other = 99
    }

    public enum ActionErrorType
    {
        InvalidNetwork = 1,
        Other = 99
    }

    public enum ActionType
    {
        SyncTaxDecRegistered = 1,
        SyncTaxDecSubmitted = 2,
        CheckETaxAccount = 3,
        OpenITaxViewer = 4,
        SyncPaymentRequest = 5,
        SyncDvcPaymentRequest = 17,
    }

    public enum EnumTaxAccountType
    {
        Enterprise = 1,
        TaxAgent = 2,
        Household = 3
    }

    public enum EnumSyncFromType
    {
        ETax = 1,
        Dvc = 2
    }
}
