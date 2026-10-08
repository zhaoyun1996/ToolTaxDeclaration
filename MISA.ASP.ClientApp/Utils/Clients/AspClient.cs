using MISA.ASP.ClientApp.Models;
using MISA.ASP.ClientApp.Models.ActionOutput.SyncTaxDec;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Utils.FileHandler;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace MISA.ASP.ClientApp.Utils.Clients
{
    internal class ASPClient
    {
        private readonly static string ASP_APP_URL = ConfigurationManager.AppSettings["ASP_App_Url"];
        private readonly static string ASP_API_URL = ConfigurationManager.AppSettings["ASP_Api_Url"];
        public HttpClient _client { get; set; }

        public ASPClient()
        {
            _client = new HttpClient() { BaseAddress = new Uri(ASP_API_URL) };
        }

        public void SetAuthentication(string accessToken)
        {
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        }

        public async Task<VersionInfor> GetLastestVersion()
        {
            try
            {
                var message = await _client.GetAsync($"api/ETaxDeclaration/GetLastestVersion");
                message.EnsureSuccessStatusCode();
                var content = await message.Content.ReadAsStringAsync();
                var contentObject = JsonConvert.DeserializeObject<ApiResult<string>>(content);
                return JsonConvert.DeserializeObject<VersionInfor>(contentObject.Data);
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
                throw ex;
            }
        }

        public async Task<ActionInfor> GetActionInfor(string actionID)
        {
            try
            {
                var message = await _client.GetAsync($"api/ETaxDeclaration/GetActionDetail?actionID={actionID}");
                message.EnsureSuccessStatusCode();
                var content = await message.Content.ReadAsStringAsync();
                var contentObject = JsonConvert.DeserializeObject<ApiResult<ActionInfor>>(content);
                return contentObject.Data;
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, "GetActionInfor");
                LogUtil.LogInfo(JsonConvert.SerializeObject(_client));
                throw ex;
            }
        }

        public async Task UpdateETaxAccountState(int profileID, CheckETaxAccountOutput output)
        {
            try
            {
                LogUtil.LogInfo("UpdateETaxAccountState:" + JsonConvert.SerializeObject(output));
                var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(output)));
                byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var message = await _client.PostAsync($"api/{profileID}/ETaxDeclaration/UpdateETaxAccountState", byteContent);
                LogUtil.LogInfo(JsonConvert.SerializeObject(message));
                LogUtil.LogInfo(await message.Content.ReadAsStringAsync());
                message.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, "UpdateETaxAccountState");
                LogUtil.LogInfo(JsonConvert.SerializeObject(_client));
                throw ex;
            }
        }

        public async Task InsertTaxDecRegistered(int profileID, SyncTaxDecRegisteredOutput output)
        {
            try
            {
                LogUtil.LogInfo("InsertTaxDecRegistered:" + JsonConvert.SerializeObject(output));
                var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(output)));
                byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var message = await _client.PostAsync($"api/{profileID}/ETaxDeclaration/SyncRegistered", byteContent);
                LogUtil.LogInfo(JsonConvert.SerializeObject(message));
                LogUtil.LogInfo(await message.Content.ReadAsStringAsync());
                message.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, "InsertTaxDecRegistered");
                LogUtil.LogInfo(JsonConvert.SerializeObject(_client));
                throw ex;
            }
        }

        public async Task InsertTaxDecSubmitted(int profileID, SyncTaxDecSubmittedOutput output)
        {
            try
            {
                LogUtil.LogInfo("InsertTaxDecSubmitted:" + JsonConvert.SerializeObject(output));
                var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(output)));
                byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var message = await _client.PostAsync($"api/{profileID}/ETaxDeclaration/SyncSubmitted", byteContent);
                LogUtil.LogInfo(JsonConvert.SerializeObject(message));
                LogUtil.LogInfo(await message.Content.ReadAsStringAsync());
                message.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, "InsertTaxDecSubmitted");
                LogUtil.LogInfo(JsonConvert.SerializeObject(_client));
                throw ex;
            }
        }

        public async Task InsertPaymentRequest(int profileID, SyncPaymentRequestOutput output)
        {
            try
            {
                LogUtil.LogInfo("InsertPaymentRequest:" + JsonConvert.SerializeObject(output));
                var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(output)));
                byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var message = await _client.PostAsync($"api/{profileID}/EPaymentRequest/SyncPaymentRequest", byteContent);
                LogUtil.LogInfo(JsonConvert.SerializeObject(message));
                LogUtil.LogInfo(await message.Content.ReadAsStringAsync());
                message.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, "InsertPaymentRequest");
                LogUtil.LogInfo(JsonConvert.SerializeObject(_client));
                throw ex;
            }
        }

        public async Task UploadTaxDecFileToServer(int profileID, FileTypeEnum fileType, string localFilePath, string localFileName, int customerID, string transactionID)
        {
            try
            {
                using (var multipartFormContent = new MultipartFormDataContent())
                {
                    var fileStreamContent = new StreamContent(File.OpenRead($"{localFilePath}\\{localFileName}"));
                    multipartFormContent.Add(fileStreamContent, name: "file", fileName: localFileName);
                    var message = await _client.PostAsync($"api/{profileID}/EtaxDeclaration/UploadTaxDecFile?type={fileType}&customerID={customerID}&transactionID={transactionID}", multipartFormContent);
                    message.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<HttpResponseMessage> GetTaxDecFileFromServer(int profileID, int customerID, string transactionID, FileTypeEnum fileType, string fileName)
        {
            try
            {
                var message = await _client.GetAsync($"api/{profileID}/ETaxDeclaration/GetTaxDecFile?type={fileType}&customerID={customerID}&transactionID={transactionID}&fileName={fileName}");
                message.EnsureSuccessStatusCode();
                return message;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task UploadPaymentRequestFileToServer(int profileID, FileTypeEnum fileType, string localFilePath, string localFileName, int customerID)
        {
            try
            {
                using (var multipartFormContent = new MultipartFormDataContent())
                {
                    var fileStreamContent = new StreamContent(File.OpenRead($"{localFilePath}\\{localFileName}"));
                    multipartFormContent.Add(fileStreamContent, name: "file", fileName: localFileName);
                    var message = await _client.PostAsync($"api/{profileID}/EPaymentRequest/UploadPaymentRequestFile?type={fileType}&customerID={customerID}", multipartFormContent);
                    message.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
