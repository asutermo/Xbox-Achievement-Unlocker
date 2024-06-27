using System;
using System.Xml.Serialization;

[XmlRoot(ElementName = "Envelope", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
public class RequestEnvelope
{
    [XmlElement(ElementName = "Header")]
    public RequestHeader Header { get; set; }

    [XmlElement(ElementName = "Body")]
    public RequestBody Body { get; set; }

    [XmlAttribute(AttributeName = "s", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string S { get; set; }

    [XmlAttribute(AttributeName = "ps", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Ps { get; set; }

    [XmlAttribute(AttributeName = "wsse", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsse { get; set; }

    [XmlAttribute(AttributeName = "saml", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Saml { get; set; }

    [XmlAttribute(AttributeName = "wsp", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsp { get; set; }

    [XmlAttribute(AttributeName = "wsu", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsu { get; set; }

    [XmlAttribute(AttributeName = "wsa", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsa { get; set; }

    [XmlAttribute(AttributeName = "wssc", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wssc { get; set; }

    [XmlAttribute(AttributeName = "wst", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wst { get; set; }
}

public class RequestHeader
{
    [XmlElement(ElementName = "Action", Namespace = "http://www.w3.org/2005/08/addressing")]
    public Action Action { get; set; }

    [XmlElement(ElementName = "To", Namespace = "http://www.w3.org/2005/08/addressing")]
    public RequestTo To { get; set; }

    [XmlElement(ElementName = "MessageID", Namespace = "http://www.w3.org/2005/08/addressing")]
    public string MessageID { get; set; }

    [XmlElement(ElementName = "AuthInfo", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public RequestAuthInfo AuthInfo { get; set; }

    [XmlElement(ElementName = "Security", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public RequestSecurity Security { get; set; }
}

public class RequestAction
{
    [XmlAttribute(AttributeName = "mustUnderstand", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
    public string MustUnderstand { get; set; }

    [XmlText]
    public string Text { get; set; }
}

public class RequestTo
{
    [XmlAttribute(AttributeName = "mustUnderstand", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
    public string MustUnderstand { get; set; }

    [XmlText]
    public string Text { get; set; }
}

public class RequestAuthInfo
{
    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlElement(ElementName = "HostingApp", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string HostingApp { get; set; }

    [XmlElement(ElementName = "BinaryVersion", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string BinaryVersion { get; set; } = "50";

    [XmlElement(ElementName = "UIVersion", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string UIVersion { get; set; } = "1";

    [XmlElement(ElementName = "InlineUX", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string InlineUX { get; set; } = "TokenBroker";

    [XmlElement(ElementName = "DeviceType", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string DeviceType { get; set; } = "XBox";

    [XmlElement(ElementName = "Cookies", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string Cookies { get; set; }

    [XmlElement(ElementName = "RequestParams", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string RequestParams { get; set; }

    [XmlElement(ElementName = "ClientCapabilities", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public string ClientCapabilities { get; set; }
}

public class RequestSecurity
{
    [XmlElement(ElementName = "EncryptedData", Namespace = "http://www.w3.org/2001/04/xmlenc#")]
    public RequestEncryptedData EncryptedData { get; set; }

    [XmlElement(ElementName = "BinarySecurityToken", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public RequestBinarySecurityToken BinarySecurityToken { get; set; }

    [XmlElement(ElementName = "UsernameToken", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public RequestUsernameToken UsernameToken { get; set; }

    [XmlElement(ElementName = "Timestamp", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public RequestTimestamp Timestamp { get; set; }

    [XmlElement(ElementName = "Signature", Namespace = "http://www.w3.org/2000/09/xmldsig#")]
    public RequestSignature Signature { get; set; }
}

public class RequestEncryptedData
{
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }

    [XmlAttribute(AttributeName = "Type")]
    public string Type { get; set; }

    [XmlElement(ElementName = "EncryptionMethod")]
    public RequestEncryptionMethod EncryptionMethod { get; set; }

    [XmlElement(ElementName = "KeyInfo", Namespace = "http://www.w3.org/2000/09/xmldsig#")]
    public RequestKeyInfo KeyInfo { get; set; }

    [XmlElement(ElementName = "CipherData")]
    public RequestCipherData CipherData { get; set; }
}

public class RequestEncryptionMethod
{
    [XmlAttribute(AttributeName = "Algorithm")]
    public string Algorithm { get; set; }
}

public class RequestKeyInfo
{
    [XmlElement(ElementName = "KeyName")]
    public string KeyName { get; set; }
}

public class RequestCipherData
{
    [XmlElement(ElementName = "CipherValue")]
    public string CipherValue { get; set; }
}

public class RequestBinarySecurityToken
{
    [XmlAttribute(AttributeName = "ValueType")]
    public string ValueType { get; set; }

    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; }

    [XmlText]
    public string Token { get; set; }
}

public class RequestUsernameToken
{
    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlElement(ElementName = "UsernameHint", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public string UsernameHint { get; set; }

    [XmlElement(ElementName = "LoginOption", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public string LoginOption { get; set; }
}

public class RequestTimestamp
{
    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlElement(ElementName = "Created")]
    public string Created { get; set; }

    [XmlElement(ElementName = "Expires")]
    public string Expires { get; set; }
}

public class RequestSignature
{
    [XmlElement(ElementName = "SignedInfo")]
    public RequestSignedInfo SignedInfo { get; set; }

    [XmlElement(ElementName = "SignatureValue")]
    public string SignatureValue { get; set; }

    [XmlElement(ElementName = "KeyInfo")]
    public RequestKeyInfo KeyInfo { get; set; }
}

public class RequestSignedInfo
{
    [XmlElement(ElementName = "CanonicalizationMethod")]
    public RequestCanonicalizationMethod CanonicalizationMethod { get; set; }

    [XmlElement(ElementName = "SignatureMethod")]
    public RequestSignatureMethod SignatureMethod { get; set; }

    [XmlElement(ElementName = "Reference")]
    public RequestReference[] Reference { get; set; }
}

public class RequestCanonicalizationMethod
{
    [XmlAttribute(AttributeName = "Algorithm")]
    public string Algorithm { get; set; }
}

public class RequestSignatureMethod
{
    [XmlAttribute(AttributeName = "Algorithm")]
    public string Algorithm { get; set; }
}

public class RequestReference
{
    [XmlAttribute(AttributeName = "URI")]
    public string Uri { get; set; }

    [XmlElement(ElementName = "Transforms")]
    public RequestTransforms Transforms { get; set; }

    [XmlElement(ElementName = "DigestMethod")]
    public RequestDigestMethod DigestMethod { get; set; }

    [XmlElement(ElementName = "DigestValue")]
    public string DigestValue { get; set; }
}

public class RequestTransforms
{
    [XmlElement(ElementName = "Transform")]
    public RequestTransform Transform { get; set; }
}

public class RequestTransform
{
    [XmlAttribute(AttributeName = "Algorithm")]
    public string Algorithm { get; set; }
}

public class RequestDigestMethod
{
    [XmlAttribute(AttributeName = "Algorithm")]
    public string Algorithm { get; set; }
}

public class RequestBody
{
    [XmlElement(ElementName = "RequestMultipleSecurityTokens", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/PPCRL")]
    public RequestMultipleSecurityTokens RequestMultipleSecurityTokens { get; set; }
}

public class RequestMultipleSecurityTokens
{
    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlElement(ElementName = "RequestSecurityToken", Namespace = "http://schemas.xmlsoap.org/ws/2005/02/trust")]
    public RequestSecurityToken[] RequestSecurityToken { get; set; }
}

public class RequestSecurityToken
{
    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlElement(ElementName = "RequestType")]
    public string RequestType { get; set; }

    [XmlElement(ElementName = "AppliesTo", Namespace = "http://schemas.xmlsoap.org/ws/2004/09/policy")]
    public RequestAppliesTo AppliesTo { get; set; }

    [XmlElement(ElementName = "PolicyReference", Namespace = "http://schemas.xmlsoap.org/ws/2004/09/policy")]
    public RequestPolicyReference PolicyReference { get; set; }
}

public class RequestAppliesTo
{
    [XmlElement(ElementName = "EndpointReference", Namespace = "http://www.w3.org/2005/08/addressing")]
    public RequestEndpointReference EndpointReference { get; set; }
}

public class RequestEndpointReference
{
    [XmlElement(ElementName = "Address")]
    public string Address { get; set; }
}

public class RequestPolicyReference
{
    [XmlAttribute(AttributeName = "URI")]
    public string Uri { get; set; }
}
