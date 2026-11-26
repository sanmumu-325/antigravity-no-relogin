using System;
using System.Net;
using System.Net.Http;

namespace AgyManager
{
    internal static class HttpUtil
    {
        public static HttpClient CreateClient(TimeSpan timeout)
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = WebRequest.DefaultWebProxy,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            HttpClient client = new HttpClient(handler);
            client.Timeout = timeout;
            return client;
        }
    }
}
