# Models and Enums

## Models Namespace: `MISA.ASP.ClientApp.Models`

### Base Result Classes
- **`BaseResult`**: Internal base class with `Success`, `Message`, `MessageDev` properties
- **`ApiResult`**: Inherits `BaseResult`, adds `Code`, `Data`, `AdditionalData`, `TotalCount`
- **`ApiResult<T>`**: Generic version of `ApiResult` with typed `Data` property

### Action Input DTOs (SynTaxDec)
- **`BaseASPInput`**: Base class with `AccessToken`, `MisaID`, `EmployeeID`, `ProfileID`
- **`SyncTaxDecRegisteredInput`**: Registered declarations input + details (customerID, taxcode, username, password)
- **`SyncTaxDecRegisteredInputDetail`**: Individual detail within registered input
- **`SyncTaxDecSubmittedInput`**: Submitted declarations input + dates (FromDate, ToDate) + details
- **`SyncTaxDecSubmittedInputDetail`**: Individual detail with `AccountType` (Enterprise/Household/TaxAgent, default: Enterprise)
- **`SyncPaymentRequestInput`**: Payment requests input + dates + details
- **`SyncPaymentRequestInputDetail`**: Individual payment request detail
- **`CheckETaxAccountInput`**: Account check input + details
- **`ViewTaxFileInput`**: View tax file input with `ProfileID`, `CustomerID`, `TransactionID`, `FileName`, `FileType`

### Action Output DTOs (SyncTaxDec)
- **`SyncTaxDecRegisteredOutput`**: Output with `ActionID`, `Details` list, `ActionErrorType`
- **`SyncTaxDecRegisteredOutputDetail`**: Per-customer output with `CustomerID`, `TaxDeclarationRegistereds`, `CrawlErrorType`
- **`SyncTaxDecSubmittedOutput`**: Submitted output with `ActionID`, `Details`, `ActionErrorType`
- **`SyncTaxDecSubmittedOutputDetail`**: Per-customer submitted output with `CustomerID`, `TaxDeclarationSubmitteds`, `CrawlErrorType`
- **`CheckETaxAccountOutput`**: Account check output with `ActionID`, `Details`, `ActionErrorType`
- **`CheckETaxAccountOutputDetail`**: Per-customer check result with `CustomerID`, `IsValid`, `CrawlErrorType`
- **`SyncPaymentRequestOutput`**: Payment request output with `ActionID`, `Details`, `ActionErrorType`
- **`SyncPaymentRequestOutputDetail`**: Per-customer payment request output with `CustomerID`, `PaymentRequests`, `CrawlErrorType`

### EtaxCrawler Models
- **`TaxDeclarationRegistered`**: Registered declaration info - `Code`, `Name`, `TaxPeriodType`, `BeginTaxPeriod`
- **`TaxDeclarationSubmitted`**: Submitted declaration with extensive properties:
  - `TaxCode`, `TransactionID`, `HideTransactionID`, `ParentTransactionID`, `Code`, `Name`
  - `TaxPeriod`, `DeclarationType`, `SubmitTimes`, `AdditionalTimes`, `SubmitDate`
  - `TaxAgencyCode`, `TaxAgencyName`, `DebitAmount`, `CreditAmount`, `FileName`
  - `State`, `IsHideDownloadLink`, `HasDownloadError`, `DownloadErrorMsg`
  - `IsHideNotificationLink`, `IsHideTransactionID`
  - `Notifications` list, `DisplayTransactionID` (computed property)
  - JSON converters for boolean properties (`BoolConverter`)
- **`PaymentRequest`**: Payment request info - `TaxCode`, `Order`, `TransactionID`, `HiddenPaymentRequestID`, `PaymentRequestID`, `Amount`, `AmountType`, `StateTitle`, `RefNo`, `CreatedDate`, `SentDate`, `TaxSubmittedDate`, `BankName`, `BankCode`, `BankAccount`, `FileName`, `IsHideDownloadLink`
- **`TaxDecNotification`**: Notification info - `NotificationID`, `Name`, `Code`, `Message`, `SendDate`, `SendBy`, `XMLTenTBao`, `XMLTrangThai`, `FileName`

### Converter
- **`BoolConverter`**: Custom `JsonConverter` for boolean JSON serialization:
  - `WriteJson`: Writes 1 or 0 based on boolean value
  - `ReadJson`: Reads string "1" as true, anything else as false
  - `CanConvert`: Returns true for `bool` type

## Enums Namespace: `MISA.ASP.ClientApp.Models.Enums`

### `FileTypeEnum`
- `Temp = 0`
- `TaxDeclaration = 28`
- `TaxNotification = 29`

### `CrawlErrorType`
- `InvalidCaptcha = 1`
- `InvalidIdentity = 2`
- `InvalidTaxCode = 3`
- `PasswordExpired = 4`
- `UnRecognizedMainPage = 5`
- `Other = 99`

### `ActionErrorType`
- `InvalidNetwork = 1`
- `Other = 99`

### `ActionType`
- `SyncTaxDecRegistered = 1`
- `SyncTaxDecSubmitted = 2`
- `SyncDvcDecSubmitted = 16`
- `CheckETaxAccount = 3`
- `OpenITaxViewer = 4`
- `SyncPaymentRequest = 5`

### `EnumTaxAccountType`
- `Enterprise = 1`
- `TaxAgent = 2`
- `Household = 3`