# User Interface Forms

The application provides several Windows Forms for different tax declaration operations:

## Forms

### 1. `frmTaxDecRegistered` - Tờ khai đã đăng ký
- **Purpose**: Synchronize tax declarations that have been registered
- **Key Operations**:
  - Deserialize input data containing customer details
  - Crawl tax declaration data from etax system (or local mode)
  - Handle errors: InvalidCaptcha, InvalidIdentity, PasswordExpired, InvalidTaxCode
  - Upload synchronized data to server via ASPClient
  - Show success/error forms upon completion
- **Error Types Managed**:
  - `CrawlErrorType.InvalidCaptcha` (1)
  - `CrawlErrorType.InvalidIdentity` (2)
  - `CrawlErrorType.PasswordExpired` (4)
  - `CrawlErrorType.InvalidTaxCode` (3)
  - `CrawlErrorType.Other` (99)

### 2. `frmTaxDecSubmitted` - Tờ khai đã nộp
- **Purpose**: Synchronize submitted tax declarations and upload files
- **Key Operations**:
  - Deserialize input with from/to date range
  - Crawl tax declaration data from etax system
  - Organize downloaded files into folders: Declarations, Documents, Notifications
  - Upload files to server (main declaration + documents + notifications)
  - UploadFileToServer method handles file categorization and upload
- **File Folders**:
  - `Declarations`: Main declaration file (iTaxDec.FileName)
  - `Documents`: Other PDF/report files
  - `Notifications`: Notification files

### 3. `frmPaymentRequest` - Tra cứu giấy nộp tiền
- **Purpose**: Query tax payment receipts and upload payment request files
- **Key Operations**:
  - Crawl payment request data from etax system
  - Upload payment request files to server
  - Individual file upload with 200ms delay between files
- **Error Handling**: Per-customer and per-file error handling without throwing exceptions

### 4. `frmCheckETaxAccount` - Kiểm tra tài khoản thuế điện tử
- **Purpose**: Validate electronic tax account credentials
- **Key Operations**:
  - Crawl etax account status using provided credentials
  - Set IsValid flag based on success/failure
  - Handle errors: InvalidCaptcha, InvalidIdentity, PasswordExpired, InvalidTaxCode, UnRecognizedMainPage
  - Update etax account state on server via ASPClient
- **Error Types**:
  - `CrawlErrorType.InvalidCaptcha` (1)
  - `CrawlErrorType.InvalidIdentity` (2)
  - `CrawlErrorType.PasswordExpired` (4)
  - `CrawlErrorType.InvalidTaxCode` (3)
  - `CrawlErrorType.UnRecognizedMainPage` (5)

### 5. `frmAboutUs` - About form
- **Purpose**: Display application information
- **Entry Point**: Main form when application starts

### 6. `frmSuccess` / `frmError` - Notification forms
- **Purpose**: Show success or error messages to users
- **Messages**: Vietnamese language messages about operation results