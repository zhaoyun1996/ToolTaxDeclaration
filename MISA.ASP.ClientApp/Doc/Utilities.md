# Utilities

The application uses several utility classes across different categories:

## 1. Clients (`Utils/Clients/`)

### `ASPClient`
- **Purpose**: Client for communicating with the ASP API server
- **Base Address**: `ConfigurationManager.AppSettings["ASP_Api_Url"]` (default: `https://aspapp.misa.vn/Api/`)
- **Key Methods**:
  - `SetAuthentication(string accessToken)`: Adds Bearer token to headers
  - `GetLastestVersion()`: Gets latest app version from API
  - `GetActionInfor(string actionID)`: Gets action details by ID
  - `UpdateETaxAccountState()`: Updates etax account state
  - `InsertTaxDecRegistered/Submitted/PaymentRequest()`: Sync tax declaration/payment data
  - `UploadTaxDecFileToServer()`: Upload tax declaration files (multipart form)
  - `GetTaxDecFileFromServer()`: Download tax files from server
  - `UploadPaymentRequestFileToServer()`: Upload payment request files

### `MisaCaptchaClient`
- **Purpose**: Solve captchas via external API
- **Base Address**: `CommonConst.ASP_API_URL`
- **Method**: `Decaptcha(CaptchaRequest, domainName)`: 
  - Sends captcha image (Base64 or Stream) to API
  - Returns decoded captcha text
  - Supports both Base64 image and Stream upload

## 2. Converter (`Utils/Converter/`)

### `BoolConverter`
- Custom `JsonConverter` for boolean properties in EtaxCrawler models
- Serializes `bool` to `1`/`0` in JSON
- Deserializes `"1"` to `true`, other strings to `false`

## 3. Design Pattern (`Utils/DesignPattern/`)

### `CommonPattern`
- **Static class** with retry logic for transient failures
- **`Retry(Func<Task> fun, RetryTimes, WaitTime)`**: Retries async action without return value
- **`Retry<T>(Func<Task<T>> fun, RetryTimes, WaitTime)`**: Retries async action with return value
- **Retry Condition**: Throws `IBreakRetryException` immediately without retry
- **Default Values**: `RetryTimes = 1`, `WaitTime = 500ms`

Used by both `ETaxCrawler` and `PublicServiceCrawler` for:
- Login retry attempts
- Page navigation retries
- File download retries
- Notification download retries

## 4. File Handler (`Utils/FileHandler/`)

### `FileUtil`
- **Static class** with file path utilities
- **`BASE_PATH`**: Combines `AppDomain.CurrentDomain.BaseDirectory` with `RelativeSearchPath`
- **`ConvertStreamToByteArray(Stream)`**: Extension method to convert stream to byte array
  - Handles `MemoryStream` directly
  - Copies other stream types to `MemoryStream`

Used for:
- File storage path management
- File I/O operations during crawler file downloads

## 5. Logging (`Utils/Logging/`)

### `LogUtil`
- **Internal class** using NLog for logging
- **Logger**: `MainLogger` (lazily initialized as `LogManager.GetLogger("*")`)
- **Methods**:
  - `LogError(string message)`, `LogError(Exception ex)`, `LogError(Exception ex, string message)`
  - `LogError(Exception ex, HttpResponseMessage responseMessage)`: Logs response details
  - `LogInfo(string message)`, `LogTrace(string message)`

Log categories used in the application:
- `LogUtil.LogTrace()`: Detailed tracing (development)
- `LogUtil.LogInfo()`: Information messages
- `LogUtil.LogError()`: Error logging with optional exception and context

## 6. URI Handler (`Utils/UriHandler/`)

### `IUriHandler` Interface
- **`HandleUri(Uri uri)`**: Method to handle URI navigation

### `UriHandler`
- **Implements**: `IUriHandler`, inherits `MarshalByRefObject`
- **IPC Channel**: `ASPSingleInstanceChannel` for single-instance application
- **`Register()`**: Registers IPC server channel
- **`GetHandler()`**: Gets IPC client channel connection
- **Usage**: Maintains single application instance, handles URI protocols from command line

## Configuration Constants (`Models/Enums/CommonConst.cs`)
- **`ASP_API_URL`**: `ConfigurationManager.AppSettings["ASP_Api_Url"] ?? "https://aspapp.misa.vn/Api/"`
- Used by `MisaCaptchaClient` as base address