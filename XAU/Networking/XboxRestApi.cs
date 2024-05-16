using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

public class XboxRestAPI
{
    private readonly HttpClient _httpClient;

    // User specifics
    private readonly string _xauth;
    private readonly string _currentSystemLanguage;

    public XboxRestAPI(string xauth, string currentSystemLanguage)
    {
        _xauth = xauth;
        _currentSystemLanguage = currentSystemLanguage;
        // This is a placeholder for the Xbox REST API
        var handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler);
    }

    public async Task<Profile?> GetProfileAsync(string xuid)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.ContractVersion, HeaderValues.ContractVersion5);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, HeaderValues.AcceptEncoding);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, HeaderValues.Accept);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, _currentSystemLanguage);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Host, Hosts.PeopleHub);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Connection, HeaderValues.KeepAlive);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Authorization, _xauth);

        var responseString = await _httpClient.GetStringAsync(
            $"https://peoplehub.xboxlive.com/users/me/people/xuids({xuid})/decoration/detail,preferredColor,presenceDetail,multiplayerSummary");
        return JsonConvert.DeserializeObject<Profile>(responseString);
    }

    public async Task<GameTitle?> GetGameTitleAsync(string xuid, string titleId)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.ContractVersion, HeaderValues.ContractVersion2);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, HeaderValues.AcceptEncoding);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, HeaderValues.Accept);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.Authorization, _xauth);
        _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, _currentSystemLanguage);

        // TODO: request as a model
        StringContent requestbody = new StringContent("{\"pfns\":null,\"titleIds\":[\"" + titleId + "\"]}");
        var gameTitleResponse = await _httpClient.PostAsync("https://titlehub.xboxlive.com/users/xuid(" + xuid + ")/titles/batch/decoration/GamePass,Achievement,Stats", requestbody).Result.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<GameTitle>(gameTitleResponse);

    }
}
