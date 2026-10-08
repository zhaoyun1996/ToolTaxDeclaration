using Microsoft.Win32;
using MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Utils.Clients;
using MISA.ASP.ClientApp.Utils.FileHandler;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows.Forms;

namespace MISA.ASP.ClientApp.BL
{
    public class ETaxViewer
    {
        public string GetDefaultApplication()
        {
            string name = string.Empty;
            RegistryKey regKey = null;

            try
            {
                // Dựa vào registry để kiểm tra itaxviewer có được cài trên máy không
                regKey = Registry.ClassesRoot.OpenSubKey("iTaxViewerFile\\shell\\open\\command", false);
                if(regKey != null)
                {
                    name = regKey.GetValue(null).ToString().ToLower().Replace("" + (char)34, "");
                }
                // Nếu không cài thì dùng trình duyệt mở
                else
                {
                    var regDefault = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.html\\UserChoice", false);
                    var stringDefault = regDefault.GetValue("ProgId");

                    regKey = Registry.ClassesRoot.OpenSubKey(stringDefault + "\\shell\\open\\command", false);
                    name = regKey.GetValue(null).ToString().ToLower().Replace("" + (char)34, "");
                }

                if (!String.IsNullOrEmpty(name) && !name.EndsWith("exe"))
                    name = name.Substring(0, name.LastIndexOf(".exe") + 4);

            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
            finally
            {
                if (regKey != null)
                    regKey.Close();
            }

            return name;
        }

        public async Task OpenFile(string data)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ViewTaxFileInput>(data);
                string fileTypeFolder;
                switch (input.FileType)
                {
                    case FileTypeEnum.TaxDeclaration:
                        fileTypeFolder = "Declarations";
                        break;
                    case FileTypeEnum.TaxNotification:
                        fileTypeFolder = "Notifications";
                        break;
                    default:
                        throw new InvalidOperationException($"Loại file không hợp lệ: {input.FileType}");
                }

                string folderPath = Path.Combine(FileUtil.BASE_PATH, "OutputFiles", input.ProfileID.ToString(), input.CustomerID.ToString(), input.TransactionID, fileTypeFolder);
                string filePath = Path.Combine(folderPath, input.FileName);

                if(!File.Exists(filePath))
                {
                    var aspClient = new ASPClient();
                    aspClient.SetAuthentication(input.AccessToken);
                    var message = await aspClient.GetTaxDecFileFromServer(input.ProfileID, input.CustomerID, input.TransactionID, input.FileType, input.FileName);

                    if(message != null && message.Content != null)
                    {
                        Directory.CreateDirectory(folderPath);
                        using (var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        {
                            await message.Content.CopyToAsync(fs);
                        }
                    }
                    else
                    {
                        LogUtil.LogError($"Không tìm được file: {filePath}");
                    }
                }

                Process.Start(GetDefaultApplication(), $"\"{filePath}\"");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }
    }
}
