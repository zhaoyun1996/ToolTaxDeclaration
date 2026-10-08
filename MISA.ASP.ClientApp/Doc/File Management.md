# File Management

The application manages tax declaration files with a structured organization pattern.

## 1. File Storage Path Structure

### Base Path
- `FileUtil.BASE_PATH`: Combines `AppDomain.CurrentDomain.BaseDirectory` with `AppDomain.CurrentDomain.RelativeSearchPath`
- Default: `bin\Debug\` or `bin\Release\` depending on build configuration

### Output Directory Structure
Files are stored under `OutputFiles/{profileID}/{customerID}/`:

#### For Submitted Declarations (`frmTaxDecSubmitted`)
```
OutputFiles/{profileID}/{customerID}/{transactionID}/
├── Declarations/       # Main declaration file
├── Documents/          # Other PDFs, reports, annexes
└── Notifications/      # Notification files
```

#### For Registered Declarations (`frmTaxDecRegistered`)
Similar structure but without the nested transaction folder for simple registered declarations.

## 2. File Organization Logic (frmTaxDecSubmitted.UploadFileToServer)

### File Categorization
When uploading files, the application categorizes existing files in the base folder:

```csharp
// Build lookup of notification file names
var notiNames = new HashSet<string>(
    iTaxDec.Notifications?.Select(n => n.FileName ?? string.Empty) ?? Enumerable.Empty<string>(),
    StringComparer.OrdinalIgnoreCase);

// For each file in base folder
foreach (var filePath in allFiles)
{
    var fileName = Path.GetFileName(filePath);
    string targetFolder;
    
    // Main declaration file stays in Declarations
    if (!string.IsNullOrEmpty(iTaxDec.FileName) &&
        fileName.Equals(iTaxDec.FileName, StringComparison.OrdinalIgnoreCase))
    {
        targetFolder = declFolder;
    }
    // Notification files go to Notifications
    else if (notiNames.Contains(fileName))
    {
        targetFolder = notiFolder;
    }
    // Everything else goes to Documents
    else
    {
        targetFolder = docFolder;
    }
    
    // Move file to target folder
    var destPath = Path.Combine(targetFolder, fileName);
    if (!File.Exists(destPath))
    {
        File.Move(filePath, destPath);
    }
}
```

### File Types and Destinations
| File Type | Target Folder |
|---|---|
| Main declaration (iTaxDec.FileName) | `Declarations/` |
| Notification files (matching iTaxDec.Notifications) | `Notifications/` |
| Other files (PDF, XML, reports, etc.) | `Documents/` |

## 3. Upload Workflow

### `frmTaxDecSubmitted.UploadFileToServer`
1. Iterates through all customer details
2. For each customer, iterates through their tax declarations
3. Creates folder structure: `Declarations`, `Documents`, `Notifications`
4. Moves existing files to proper folders
5. Uploads files in three phases:
   - **Phase 1**: Main declaration file (`UploadTaxDecFileToServer` with `FileTypeEnum.TaxDeclaration`)
   - **Phase 2**: Document files from `Documents/` folder
   - **Phase 3**: Notification files from `Notifications/` folder

### `frmPaymentRequest.UploadFileToServer`
1. Iterates through payment requests
2. For each request with payment data:
   - Checks local folder: `OutputFiles/{profileID}/{customerID}/PaymentRequest/`
   - Uploads each file with 200ms delay between uploads
   - Uses `UploadPaymentRequestFileToServer` method

### `ETaxCrawler` File Downloading
- **Step 13_DownloadTransactionFile**: Downloads main declaration file from etax system
  - URL: `/etaxnnt/Request?...dse_nextEventName=downTkhai or downBke`
  - Saves to: `OutputFiles/{profileID}/{customerID}/{transactionID}/{filename}`
  - If XML file: Extracts tax amounts (DebitAmount, CreditAmount), tax agency code
  - Parses XML using namespace `msbld: http://kekhaithue.gdt.gov.vn/TKhaiThue`

- **Step 17_DownloadNotificationFile**: Downloads notification files
  - URL: POST to `/etaxnnt/Request` with `dse_nextEventName=download`
  - Saves to: `OutputFiles/{profileID}/{customerID}/{transactionID}/{filename}`
  - If XML file: Extracts notification code, status, name

- **PublicServiceCrawler Step13_DownloadTransactionFile**:
  - API: `/tthc/tchs/downloadhoso` with JSON body `{ maHoSo: transactionID }`
  - Returns base64-encoded file content and filename
  - Handles ZIP files (extracts to Declarations/ Documents folders)
  - Handles XML files (extracts tax amounts, agency code)

## 4. File Download from Server (ASPClient)

### `UploadTaxDecFileToServer`
```csharp
public async Task UploadTaxDecFileToServer(int profileID, FileTypeEnum fileType, 
    string localFilePath, string localFileName, int customerID, string transactionID)
{
    using (var multipartFormContent = new MultipartFormDataContent())
    {
        var fileStreamContent = new StreamContent(File.OpenRead($"{localFilePath}\\{localFileName}"));
        multipartFormContent.Add(fileStreamContent, name: "file", fileName: localFileName);
        var message = await _client.PostAsync(
            $"api/{profileID}/EtaxDeclaration/UploadTaxDecFile?type={fileType}&customerID={customerID}&transactionID={transactionID}", 
            multipartFormContent);
    }
}
```

### `GetTaxDecFileFromServer`
```csharp
public async Task<HttpResponseMessage> GetTaxDecFileFromServer(int profileID, int customerID, 
    string transactionID, FileTypeEnum fileType, string fileName)
{
    var message = await _client.GetAsync(
        $"api/{profileID}/ETaxDeclaration/GetTaxDecFile?type={fileType}&customerID={customerID}&transactionID={transactionID}&fileName={fileName}");
    return message;
}
```

### `UploadPaymentRequestFileToServer`
```csharp
public async Task UploadPaymentRequestFileToServer(int profileID, FileTypeEnum fileType, 
    string localFilePath, string localFileName, int customerID)
{
    using (var multipartFormContent = new MultipartFormDataContent())
    {
        var fileStreamContent = new StreamContent(File.OpenRead($"{localFilePath}\\{localFileName}"));
        multipartFormContent.Add(fileStreamContent, name: "file", fileName: localFileName);
        var message = await _client.PostAsync(
            $"api/{profileID}/EtaxPaymentRequest/UploadPaymentRequestFile?type={fileType}&customerID={customerID}", 
            multipartFormContent);
    }
}
```

## 5. File Viewing (ETaxViewer)
- **`GetDefaultApplication()`**: Checks Windows Registry for iTaxViewer installation
  - First checks `Registry.ClassesRoot.OpenSubKey("iTaxViewerFile\\shell\\open\\command")`
  - Falls back to `.html` file association
- **`OpenFile(string data)`**: 
  1. Deserializes `ViewTaxFileInput` from JSON
  2. Constructs file path from `FileUtil.BASE_PATH`
  3. Downloads from server if file doesn't exist locally
  3. Opens file with default application via `Process.Start()`

## 6. Local File Utilities (`Utils/FileHandler/FileUtil.cs`)
- **`BASE_PATH`**: Application base directory for file operations
- **`ConvertStreamToByteArray(this Stream instream)`**: Extension method to convert streams to byte arrays
  - Handles `MemoryStream` directly
  - Copies other stream types to new `MemoryStream`
  - Returns `null` on error (logged via `LogUtil`)