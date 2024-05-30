using System.Text.Json.Serialization;

public class DisplayClaims
{
    public string? gtg { get; set; } // gamertag
    public string? mgt { get; set; } // modern gamertag
    public string? umg { get; set; } // unique modern tag
    public string? mgs { get; set; } // modern tag suffix
    public string? xui { get; set; } // xbox user id
    public string? uhs { get; set; } // user hash
    public string? agg { get; set; } // age group
    public string? usr { get; set; } // user settings restrictions
    public string? utr { get; set; } // user title restrictions
    public string? prv { get; set; } // privileges
}

public class DeviceToken
{
    public DisplayClaims? DisplayClaims { get; set; }
    public string? IssueInstant { get; set; }
    public string? Token { get; set; }
    public string? ExpireOn { get; set; }
}

public class XboxDeviceTypes
{
    public const string Win32 = "Win32";
    public const string iOS = "iOS";
    public const string Android = "Android";
    public const string Nintendo = "Nintendo";
    public const string Durango = "Durango";
    public const string XboxOne = "XboxOne";
    public const string Edmonton = "Edmonton";
    public const string Scarlett = "Scarlett";

}
