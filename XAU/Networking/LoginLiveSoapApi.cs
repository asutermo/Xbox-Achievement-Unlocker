using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class LoginLiveSoapApi
{
    private readonly HttpClient _httpClient;

    // User specifics
    public LoginLiveSoapApi()
    {
        var handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler);
    }

    private void SetDefaultHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "MSAWindows/50 (OS 10.0.25398.0.0 xb_flt_2406zn; IDK 10.0.25398.4897 xb_flt_2406zn; Cfg 16.000.29325.00; Test 0)");
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, "gzip, deflate, br");
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept,
            "*/*");
        _httpClient.DefaultRequestHeaders.Add("Content-Type",
            "application/soap+xml;");
    }

}
