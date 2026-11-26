using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AgyManager
{
    internal static class RefreshCheck
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                ProfileStore store = new ProfileStore();
                Profile profile = store.SaveCurrent(null);
                TokenData token = TokenStore.LoadProfileToken(profile);
                if (token == null)
                {
                    log.AppendLine("no token");
                    return 1;
                }
                log.AppendLine("email=" + token.Email);
                log.AppendLine("expiry=" + token.Expiry);
                log.AppendLine("usableNow=" + token.AccessUsable(0));
                log.AppendLine("refreshLen=" + (token.RefreshToken ?? "").Length);
                try
                {
                    OAuthRefresher.RefreshAsync(token, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    log.AppendLine("refresh=OK");
                    log.AppendLine("client=" + ConfigStore.GetPreferredClient());
                    log.AppendLine("newExpiry=" + token.Expiry);
                }
                catch (Exception ex)
                {
                    log.AppendLine("refresh=FAILED");
                    log.AppendLine(ex.ToString());
                }
                bool ok = token.AccessUsable(0);
                log.AppendLine(ok ? "REFRESHCHECK OK" : "REFRESHCHECK FAILED");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION " + ex);
                return 1;
            }
            finally
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "refreshcheck.log"), log.ToString(), new UTF8Encoding(true));
                }
                catch { }
            }
        }
    }
}
