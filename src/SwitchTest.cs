using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class SwitchTest
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;
            try
            {
                string tokenA = BuildToken("a@example.com", "a");
                string tokenB = BuildToken("b@example.com", "b");
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.TokenFile));
                File.WriteAllText(AppPaths.TokenFile, tokenA, new UTF8Encoding(false));
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.AppStorageFile));
                File.WriteAllText(AppPaths.AppStorageFile,
                    "{\"x\":1,\"agy.onboarding.lastLoginUsername\":\"old@example.com\"}",
                    new UTF8Encoding(false));

                ProfileStore store = new ProfileStore();
                Profile a = store.SaveCurrent("a@example.com");
                Check(a.Email == "a@example.com", "save a", log, ref failures);
                Check(File.Exists(Path.Combine(a.Directory, "token.json")), "a token file", log, ref failures);

                File.WriteAllText(AppPaths.TokenFile, tokenB, new UTF8Encoding(false));
                Profile b = store.SaveCurrent("b@example.com");
                Check(b.Email == "b@example.com", "save b", log, ref failures);
                Check(TokenStore.LiveEmail() == "b@example.com", "live b", log, ref failures);

                store.Switch(a);
                Check(TokenStore.LiveEmail() == "a@example.com", "switch to a", log, ref failures);
                string storage = File.ReadAllText(AppPaths.AppStorageFile, Encoding.UTF8);
                Check(storage.IndexOf("a@example.com", StringComparison.Ordinal) >= 0,
                    "storage email a", log, ref failures);

                store.Switch(b);
                Check(TokenStore.LiveEmail() == "b@example.com", "switch to b", log, ref failures);
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("EXCEPTION " + ex);
            }
            log.AppendLine(failures == 0 ? "SWITCHTEST OK" : "SWITCHTEST FAILED " + failures);
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "switchtest.log"),
                    log.ToString(), new UTF8Encoding(true));
            }
            catch { }
            return failures == 0 ? 0 : 1;
        }

        private static string BuildToken(string email, string tag)
        {
            string jwt = "eyJhbGciOiJub25lIn0." +
                JsonUtil.Base64UrlEncode("{\"email\":\"" + email + "\"}") + ".sig";
            return "{\"token\":{\"access_token\":\"" + tag + "-access\"," +
                "\"refresh_token\":\"" + tag + "-refresh\"," +
                "\"id_token\":\"" + jwt + "\"," +
                "\"expiry\":\"2099-01-01T00:00:00Z\"}}";
        }

        private static void Check(bool condition, string name, StringBuilder log, ref int failures)
        {
            log.AppendLine((condition ? "PASS " : "FAIL ") + name);
            if (!condition) failures++;
        }
    }
}
