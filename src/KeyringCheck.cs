using System;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class KeyringCheck
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                foreach (KeyringRecord record in Keyring.List())
                {
                    string blob = record.Blob ?? "";
                    string kind = "plain";
                    if (blob.StartsWith("go-keyring-base64:", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = "base64";
                        try
                        {
                            blob = Encoding.UTF8.GetString(
                                Convert.FromBase64String(blob.Substring("go-keyring-base64:".Length)));
                        }
                        catch
                        {
                            kind = "base64-invalid";
                        }
                    }
                    TokenData token = TokenData.FromJson(blob, true);
                    log.AppendLine("target=" + record.Target);
                    log.AppendLine("  user=" + record.Username + " persist=" + record.Persist +
                        " blobLen=" + blob.Length + " kind=" + kind);
                    log.AppendLine("  parse=" + (token != null) +
                        (token != null ? " email=" + token.Email +
                            " access=" + token.HasAccessToken() +
                            " refresh=" + !string.IsNullOrEmpty(token.RefreshToken) : ""));
                }
                TokenData live = TokenStore.LoadLive();
                log.AppendLine("live=" + (live == null ? "null" : live.Email));
                log.AppendLine(live != null ? "KEYRINGCHECK OK" : "KEYRINGCHECK NO TOKEN");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keyringcheck.log"),
                    log.ToString(), new UTF8Encoding(true));
                return live != null ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION " + ex);
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "keyringcheck.log"), log.ToString(), new UTF8Encoding(true));
                }
                catch { }
                return 1;
            }
        }
    }
}
