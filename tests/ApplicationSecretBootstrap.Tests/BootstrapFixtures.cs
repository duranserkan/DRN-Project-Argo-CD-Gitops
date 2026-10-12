using System.Net;
using System.Text;
using DRN.Framework.Utils.Data.Encodings;

namespace ApplicationSecretBootstrap.Tests;

/// <summary>Common fixture encoding and responses for the two fake Kubernetes APIs.</summary>
internal static class BootstrapFixtures
{
    internal static string Encode(string value) => Encoding.UTF8.GetBytes(value).Encode(ByteEncoding.Base64);

    internal static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    internal static HttpResponseMessage Response(HttpStatusCode status, string body = "{}") => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
