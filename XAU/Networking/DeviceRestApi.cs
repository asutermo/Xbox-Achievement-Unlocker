using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XAU.ViewModels.Pages;
using XAU.ViewModels.Windows;

public class DeviceRestApi
{
    private readonly HttpClient _httpClient;
    private readonly Signer _signer;
    private const string DeviceUrl = "https://device.auth.xboxlive.com/device/authenticate";
    private const string UserAgent = "XblAuthManager";

    public DeviceRestApi()
    {
        var handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler);
        _signer = new Signer(new ECDCertificatePopCryptoProvider());
    }

    private void SetDefaultHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, HeaderValues.AcceptEncoding);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, HeaderValues.Accept);
    }

    public async Task GetDeviceTokenAsync()
    {
        string id = Guid.NewGuid().ToString("D"); // Replace with your actual id
        string serialNumber = Guid.NewGuid().ToString("D"); // Replace with your actual serial number
        string json = $@"
        {{
            ""RelyingParty"": ""http://auth.xboxlive.com"",
            ""TokenType"": ""JWT"",
            ""Properties"": {{
                ""AuthMethod"": ""ProofOfPossession"",
                ""Id"": ""{id}"",
                ""DeviceType"": ""Scarlett"",
                ""SerialNumber"": ""{serialNumber}"",
                ""Version"": ""DeviceVersion"",
                ""ProofKey"": {_signer.ProofKey}
            }}
        }}";
        var req = new HttpRequestMessage
        {
            RequestUri = new Uri(DeviceUrl),
            Method = HttpMethod.Post,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var signature = _signer.SignRequest(DeviceUrl, HeaderValues.Signature, json);
        req.Headers.Add("Signature", signature);
        _httpClient.DefaultRequestHeaders.Add("Signature", signature);
        var response = await _httpClient.SendAsync(req);
        Console.WriteLine(response);
    }
}
