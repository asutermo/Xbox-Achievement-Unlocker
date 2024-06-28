using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

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

    public async Task RequestSecurityToken()
    {
        SetDefaultHeaders();

        string fileName = "rps_soap_request_envelope.xml";
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

        XDocument soapXml = XDocument.Load(filePath);
        Console.WriteLine(soapXml.ToString());
        var content = new StringContent(soapXml.ToString(), Encoding.UTF8, "text/xml");

        // For example, change the value of an element named 'YourElement'
        var element = soapXml.Descendants().FirstOrDefault(e => e.Name.LocalName == "UsernameHint");
        if (element != null)
        {
            element.Value = "NewValue";
        }

        element = soapXml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Created");
        if (element != null)
        {
            element.Value = "NewValue";
        }

        element = soapXml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Expires");
        if (element != null)
        {
            element.Value = "NewValue";
        }

        // TODO: move url to string constants
        var response = await _httpClient.PostAsync("https://login.live.com/RST2.srf", content);
        string result = await response.Content.ReadAsStringAsync();

        Console.WriteLine(result);
    }

}
