using System;
using System.Xml.Serialization;

[XmlRoot(ElementName = "Envelope", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
public class ResponseEnvelope
{
    [XmlElement(ElementName = "Header")]
    public ResponseHeader Header { get; set; }

    [XmlElement(ElementName = "Body")]
    public ResponseBody Body { get; set; }

    [XmlAttribute(AttributeName = "S", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string S { get; set; }

    [XmlAttribute(AttributeName = "wsse", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsse { get; set; }

    [XmlAttribute(AttributeName = "wsu", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsu { get; set; }

    [XmlAttribute(AttributeName = "wsa", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string Wsa { get; set; }
}

public class ResponseHeader
{
    [XmlElement(ElementName = "Action", Namespace = "http://www.w3.org/2005/08/addressing")]
    public ResponseAction Action { get; set; }

    [XmlElement(ElementName = "To", Namespace = "http://www.w3.org/2005/08/addressing")]
    public ResponseTo To { get; set; }

    [XmlElement(ElementName = "Security", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public ResponseSecurity Security { get; set; }

    [XmlElement(ElementName = "pp", Namespace = "http://schemas.microsoft.com/Passport/SoapServices/SOAPFault")]
    public ResponsePp Pp { get; set; }
}

public class ResponseAction
{
    [XmlAttribute(AttributeName = "mustUnderstand", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
    public string MustUnderstand { get; set; }

    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlText]
    public string Text { get; set; }
}

public class ResponseTo
{
    [XmlAttribute(AttributeName = "mustUnderstand", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
    public string MustUnderstand { get; set; }

    [XmlAttribute(AttributeName = "Id", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Id { get; set; }

    [XmlText]
    public string Text { get; set; }
}

public class ResponseSecurity
{
    [XmlElement(ElementName = "Timestamp", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public ResponseTimestamp Timestamp { get; set; }

    [XmlAttribute(AttributeName = "mustUnderstand", Namespace = "http://www.w3.org/2003/05/soap-envelope")]
    public string MustUnderstand { get; set; }
}

public class ResponseTimestamp
{
    [XmlElement(ElementName = "Created")]
    public string Created { get; set; }

    [XmlElement(ElementName = "Expires")]
    public string Expires { get; set; }
}

public class ResponsePp
{
    // Add properties as needed based on your XML structure
}

public class ResponseBody
{
    [XmlElement(ElementName = "RequestSecurityTokenResponseCollection", Namespace = "http://schemas.xmlsoap.org/ws/2005/02/trust")]
    public ResponseRequestSecurityTokenResponseCollection RequestSecurityTokenResponseCollection { get; set; }
}

public class ResponseRequestSecurityTokenResponseCollection
{
    [XmlElement(ElementName = "RequestSecurityTokenResponse")]
    public ResponseRequestSecurityTokenResponse[] RequestSecurityTokenResponse { get; set; }
}

public class ResponseRequestSecurityTokenResponse
{
    [XmlElement(ElementName = "TokenType")]
    public string TokenType { get; set; }

    [XmlElement(ElementName = "AppliesTo", Namespace = "http://schemas.xmlsoap.org/ws/2004/09/policy")]
    public ResponseAppliesTo AppliesTo { get; set; }

    [XmlElement(ElementName = "Lifetime")]
    public ResponseLifetime Lifetime { get; set; }

    [XmlElement(ElementName = "RequestedSecurityToken")]
    public ResponseRequestedSecurityToken RequestedSecurityToken { get; set; }

    [XmlElement(ElementName = "RequestedAttachedReference")]
    public ResponseRequestedAttachedReference RequestedAttachedReference { get; set; }

    [XmlElement(ElementName = "RequestedUnattachedReference")]
    public ResponseRequestedUnattachedReference RequestedUnattachedReference { get; set; }
}

public class ResponseAppliesTo
{
    [XmlElement(ElementName = "EndpointReference", Namespace = "http://www.w3.org/2005/08/addressing")]
    public ResponseEndpointReference EndpointReference { get; set; }
}

public class ResponseEndpointReference
{
    [XmlElement(ElementName = "Address")]
    public string Address { get; set; }
}

public class ResponseLifetime
{
    [XmlElement(ElementName = "Created", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Created { get; set; }

    [XmlElement(ElementName = "Expires", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")]
    public string Expires { get; set; }
}

public class ResponseRequestedSecurityToken
{
    [XmlElement(ElementName = "BinarySecurityToken", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public ResponseBinarySecurityToken BinarySecurityToken { get; set; }

    [XmlElement(ElementName = "EncryptedData", Namespace = "http://www.w3.org/2001/04/xmlenc#")]
    public ResponseEncryptedData EncryptedData { get; set; }
}

public class ResponseBinarySecurityToken
{
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }

    [XmlText]
    public string Token { get; set; }
}

public class ResponseEncryptedData
{
    [XmlAttribute(AttributeName = "Id")]
    public string Id { get; set; }

    [XmlAttribute(AttributeName = "Type")]
    public string Type { get; set; }

    [XmlElement(ElementName = "EncryptionMethod")]
    public ResponseEncryptionMethod EncryptionMethod { get; set; }

    [XmlElement(ElementName = "KeyInfo", Namespace = "http://www.w3.org/2000/09/xmldsig#")]
    public ResponseKeyInfo KeyInfo { get; set; }

    [XmlElement(ElementName = "CipherData")]
    public ResponseCipherData CipherData { get; set; }
}

public class ResponseEncryptionMethod
{
    [XmlAttribute(AttributeName = "Algorithm")]
    public string Algorithm { get; set; }
}

public class ResponseKeyInfo
{
    [XmlElement(ElementName = "KeyName")]
    public string KeyName { get; set; }
}

public class ResponseCipherData
{
    [XmlElement(ElementName = "CipherValue")]
    public string CipherValue { get; set; }
}

public class ResponseRequestedAttachedReference
{
    [XmlElement(ElementName = "SecurityTokenReference", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public ResponseSecurityTokenReference SecurityTokenReference { get; set; }
}

public class ResponseRequestedUnattachedReference
{
    [XmlElement(ElementName = "SecurityTokenReference", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public ResponseSecurityTokenReference SecurityTokenReference { get; set; }
}

public class ResponseSecurityTokenReference
{
    [XmlElement(ElementName = "Reference", Namespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")]
    public ResponseReference Reference { get; set; }
}

public class ResponseReference
{
    [XmlAttribute(AttributeName = "URI")]
    public string Uri { get; set; }
}
