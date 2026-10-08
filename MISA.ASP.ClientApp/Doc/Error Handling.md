# Error Handling

The application has a comprehensive error handling structure with specific exception types and error categorization.

## 1. Exception Types (`Models/Exceptions/`)

### Custom Exception Hierarchy
All exceptions inherit from `System.Exception` and are organized as follows:

#### `InvalidCaptchaException` (line 17-19)
- Thrown when captcha verification fails
- Triggered by: "Mã xác thực không chính xác" on login page or captcha errors in search pages
- Error Type: `CrawlErrorType.InvalidCaptcha` (1)

#### `InvalidIdentityException` (line 13-23), implements `IBreakRetryException`
- Thrown when username/password are incorrect
- Triggered by: "Tên đăng nhập hoặc mật khẩu của bạn không chính xác"
- Also thrown when login desc contains "tên đăng nhập hoặc mật khẩu không đúng/chưa đúng"
- Error Type: `CrawlErrorType.InvalidIdentity` (2)
- Can break retry cycle via `IBreakRetryException`

#### `PasswordExpiredException` (line 34-36), implements `IBreakRetryException`
- Thrown when password needs to be changed
- Triggered by: "Để bảo đảm an toàn vui lòng chọn 'Đồng ý' để thay đổi mật khẩu"
- Error Type: `CrawlErrorType.PasswordExpired` (4)
- Can break retry cycle via `IBreakRetryException`

#### `InvalidTaxCodeException` (line 29-32), implements `IBreakRetryException`
- Thrown when tax code doesn't match
- Triggered by: Tax code mismatch after login, or missing tax code on main page
- Error Type: `CrawlErrorType.InvalidTaxCode` (3)
- Can break retry cycle via `IBreakRetryException`

#### `UnRecognizedMainPageException` (line 25-27)
- Thrown when main page cannot be recognized after login
- Triggered by: Unable to read tax code from main page
- Error Type: `CrawlErrorType.UnRecognizedMainPage` (5)

#### `UnResolvedCaptchaException` (not shown in file but used)
- Thrown when captcha cannot be solved/decoded

#### `NotFoundSessionIdException` (used in ETaxCrawler.Step1)
- Thrown when session ID cannot be found on initial page

#### `NotFoundProcessIdException` (used in ETaxCrawler.Step9 for public service)
- Thrown when processor ID cannot be found

### `IBreakRetryException` Interface (`Models/Exceptions/IBreakRetryException.cs`)
- Marker interface for exceptions that should break the retry cycle
- Used by `CommonPattern.Retry()` to immediately throw without retrying
- Implemented by: `InvalidIdentityException`, `PasswordExpiredException`, `InvalidTaxCodeException`

## 2. Error Categorization in Crawlers

### `ETaxCrawler` Error Handling
| Exception Type | CrawlErrorType | Breaks Retry? |
|---|---|---|
| InvalidCaptchaException | InvalidCaptcha (1) | No |
| InvalidIdentityException | InvalidIdentity (2) | Yes |
| PasswordExpiredException | PasswordExpired (4) | Yes |
| InvalidTaxCodeException | InvalidTaxCode (3) | Yes |
| UnRecognizedMainPageException | UnRecognizedMainPage (5) | No |
| General Exception | Other (99) | No |

### `PublicServiceCrawler` Error Handling
| Condition | Exception Type | CrawlErrorType |
|---|---|---|
| Captcha mismatch | InvalidCaptchaException | InvalidCaptcha (1) |
| Login failed (captcha-related) | InvalidCaptchaException | InvalidCaptcha (1) |
| Login failed (wrong credentials) | InvalidIdentityException | InvalidIdentity (2) |
| Captcha unsolvable | UnResolvedCaptchaException | Not explicitly mapped |

## 3. UI Error Handling

### Error Forms
- **`frmError`**: Shows "Đã có lỗi xảy ra, vui lòng thực hiện đồng bộ lại" (An error occurred, please perform sync again)
- **`frmSuccess`**: Shows "Đã hoàn thành đồng bộ tờ khai" (Sync completed successfully)

### Error Workflow in UI Forms
1. Crawler throws specific exception
2. `Sync*` method catches exception and sets `CrawlErrorType` on output detail
3. Error is logged via `LogUtil.LogError(ex)`
4. Appropriate error form is shown to user
5. `Program.InProcessing` is set to false
6. Process continues to next customer or stops

## 4. Logging
- All errors logged via `LogUtil.LogError()` with exception and context
- `LogUtil.LogError(ex, responseMessage)`: Includes HTTP response details
- `LogUtil.LogInfo()`: Information messages for debugging
- `LogUtil.LogTrace()`: Detailed tracing (development mode)
- Errors include: exception message, stack trace, and additional context (response messages, serialized data)