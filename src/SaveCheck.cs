using System;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class SaveCheck
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                ProfileStore store = new ProfileStore();
                Profile profile = store.SaveCurrent(null);
                log.AppendLine("savedEmail=" + profile.Email);
                log.AppendLine("directory=" + profile.Directory);
                log.AppendLine("tokenFile=" + File.Exists(Path.Combine(profile.Directory, "token.json")));
                log.AppendLine("keyringFile=" + File.Exists(Path.Combine(profile.Directory, "keyring.json")));
                TokenData loaded = TokenStore.LoadProfileToken(profile);
                log.AppendLine("loaded=" + (loaded == null ? "null" : loaded.Email));
                bool ok = loaded != null && JsonUtil.IsEmail(loaded.Email);
                log.AppendLine(ok ? "SAVECHECK OK" : "SAVECHECK FAILED");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "savecheck.log"),
                    log.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION " + ex);
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "savecheck.log"), log.ToString(), new UTF8Encoding(true));
                }
                catch { }
                return 1;
            }
        }
    }
}
