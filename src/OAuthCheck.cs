using System;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class OAuthCheck
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                string info = OAuthClientProvider.Describe();
                log.AppendLine("configFile=" + AppPaths.OauthClientsFile);
                log.AppendLine(info);
                int pairs = OAuthClientProvider.Load().Count;
                log.AppendLine(pairs > 0 ? "OAUTHCHECK OK" : "OAUTHCHECK NO CLIENTS");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "oauthcheck.log"), log.ToString(), new UTF8Encoding(true));
                return pairs > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION " + ex);
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "oauthcheck.log"), log.ToString(), new UTF8Encoding(true));
                }
                catch { }
                return 1;
            }
        }
    }
}
