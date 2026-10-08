# Design Patterns

The application uses a limited set of design patterns, primarily concentrated in the Utils layer.

## 1. Common Pattern (`Utils/DesignPattern/CommonPattern.cs`)

### Purpose
Provides retry logic for transient failures in crawler operations. This is critical for network-dependent operations that may fail due to temporary issues (timeouts, network glitches, session timeouts).

### `Retry<T>(Func<Task<T>> fun, int RetryTimes = 1, int WaitTime = 500)`
- **Type**: Generic async retry method
- **Parameters**:
  - `fun`: Async function to execute
  - `RetryTimes`: Number of retry attempts (default: 1 = no retry, just execute once)
  - `WaitTime`: Milliseconds to wait between retries (default: 500ms)
- **Behavior**:
  - Loops `RetryTimes - 1` times attempting the function
  - If exception is `IBreakRetryException`, re-throws immediately without waiting
  - Otherwise, logs the exception message and waits `WaitTime` before retry
  - After all attempts, executes the function one final time (outside the loop)
  - Returns the result of the final successful execution

### `Retry(Func<Task> fun, int RetryTimes = 1, int WaitTime = 500)`
- **Non-generic version** of the above, returns `Task` instead of `Task<T>`

### Usage in Crawlers

#### `PublicServiceCrawler.SignIn()`
```csharp
public async Task SignIn()
{
    await CommonPattern.Retry(async () =>
    {
        await Step1_GotoHomeLogin();
        await Task.Delay(500, _stoppingToken);
        await Step2_GotoLoginPage();
        await Task.Delay(500, _stoppingToken);
        var captcha = await Step3_ResolveCaptcha();
        await Task.Delay(500, _stoppingToken);
        await Step4_PostLoginLDAP(captcha);
        await Task.Delay(1000, _stoppingToken);
    }, 5); // 5 retry attempts
}
```

#### `PublicServiceCrawler.Step14_GetThongBaoPageData`
```csharp
await CommonPattern.Retry(async () =>
{
    notiOk = await Step17_DownloadNotificationFile(iTaxDec, iNoti, _csrfToken);
    if (!notiOk)
    {
        throw new Exception("Download notification file failed");
    }
    await Task.Delay(300, _stoppingToken);
}, 3, 2000); // 3 retries, 2s wait
```

#### `PublicServiceCrawler.ProcessTransactionFile`
```csharp
await CommonPattern.Retry(async () =>
{
    detailDoc = await Step13_GetDetailPage(iTaxDec.TransactionID);
    await Task.Delay(300, _stoppingToken);
}, 3, 1000); // 3 retries, 1s wait
```

### `IBreakRetryException` Interface
- **Location**: `Models/Exceptions/IBreakRetryException.cs`
- **Purpose**: Marker interface indicating the exception should break the retry cycle
- **Implementing Exceptions**:
  - `InvalidIdentityException`: Login failed - no point retrying
  - `PasswordExpiredException`: Password must be changed - no point retrying
  - `InvalidTaxCodeException`: Tax code is wrong - no point retrying

### Pattern Summary
- **Retry Pattern**: Attempt operation multiple times with configurable delays
- **Break Pattern**: Certain exceptions (identity errors, wrong tax code) should not be retried
- **Default Configuration**: 1 retry attempt with 500ms wait (effectively no retry unless explicitly configured)
- **Logging**: Exceptions are logged via `Console.WriteLine` or `LogUtil.LogError` depending on context