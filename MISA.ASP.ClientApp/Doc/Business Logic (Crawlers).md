# Business Logic (Crawlers)

The application uses two crawler classes to interact with Vietnamese tax authority systems:

## 1. `ETaxCrawler` (BL/ETaxCrawler.cs)
Crawls the **thuedientu.gdt.gov.vn** system (Electronic Tax System).

### Authentication Flow (7 Steps)
1. **Step1_GotoJumpPage**: Initial page to get session ID (`dse_sessionId`)
2. **Step2_GotoIndexPage**: Jump to index page
3. **Step3_GotoHomePage**: Navigate to home page
4. **Step4_GotoLoginPage**: Navigate to login page
5. **Step5_ResolveCaptcha**: Get captcha image and solve via `MisaCaptchaClient`
6. **Step6_PostLoginForm**: POST login form with username, password, and captcha code
   - Throws `InvalidCaptchaException` if captcha mismatch
   - Throws `InvalidIdentityException` if login credentials invalid
7. **Step7_GotoMainPage**: Navigate to main page, validate tax code matches

### Synchronization Methods
- **GetTaxDecRegistered**: Extract tax declarations already registered
  - Uses Steps 9-10: Navigate to declaration registration page, extract data from HTML table
- **GetTaxDecSubmitted**: Extract submitted tax declarations
  - Uses Steps 9-11: Navigate to search page, extract with pagination
  - Download transaction files (Step 13)
  - Extract tax agency code and financial amounts from XML
  - Get notifications (Step 14)
- **CheckETaxAccount**: Quick account validity check
  - Logs in and checks if main page is accessible

### Key Features
- Cookie-based session maintenance
- HTML Agility Pack for DOM parsing
- Regex-based pattern extraction
- Base64 captcha image solving
- Local file storage in `OutputFiles/{profileID}/{customerID}/{transactionID}`
- Error exceptions: `InvalidCaptchaException`, `InvalidIdentityException`, `PasswordExpiredException`, `InvalidTaxCodeException`, `UnRecognizedMainPageException`

## 2. `PublicServiceCrawler` (BL/PublicServiceCrawler.cs)
Crawls the **dichvucong.gdt.gov.vn** system (Public Service Portal).

### Authentication Flow
1. **Step1_GotoHomeLogin**: Initial home login page
2. **Step2_GotoLoginPage**: Get CSRF token from login page
3. **Step3_ResolveCaptcha**: Solve captcha via external API (Render.com) or Python CLI
4. **Step4_PostLoginLDAP**: POST login with username, base64 password, and captcha

### Query Methods
- **Step10_ClickButtonTraCuu**: Search tax declarations with date range and captcha
- **Step11_ExtractTaxDecSubmitted**: Extract submitted declarations with pagination
- **Step13_GetDetailPage**: Get detail page for a specific declaration (gets CSRF token)
- **Step13_DownloadTransactionFile**: Download declaration files (ZIP or XML)
- **Step14_GetThongBaoPageData**: Get notification data from detail page
- **ProcessTransactionFile**: Full processing pipeline for one declaration

### Key Features
- CSRF token management for POST requests
- Retry pattern via `CommonPattern.Retry` (5 retries by default)
- JSON-based file download API (`/tthc/tchs/downloadhoso`)
- ZIP file extraction for declaration packages
- XML parsing for tax amounts and agency codes
- Notification file downloading with retry logic

## Common to Both Crawlers
- `HttpClient` with cookie container for session persistence
- Custom HTTP headers (User-Agent, Accept-Language, etc.)
- `FileUtil.BASE_PATH` for file storage location
- `LogUtil` for tracing and error logging
- `CancellationToken` for token cancellation
- `MisaCaptchaClient` for captcha solving