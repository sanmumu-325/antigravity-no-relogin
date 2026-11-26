using System;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class KeyringWriteTest
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            string target = "antigravity:selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                KeyringRecord record = new KeyringRecord();
                record.Target = target;
                record.Username = "antigravity";
                record.Persist = 2;
                record.Blob = "{\"token\":{\"access_token\":\"selftest\",\"refresh_token\":\"selftest\"}}";
                Keyring.Write(record);
                bool found = false;
                foreach (KeyringRecord item in Keyring.List())
                {
                    if (item.Target == target && item.Blob == record.Blob) found = true;
                }
                log.AppendLine("target=" + target);
                log.AppendLine("roundtrip=" + found);
                log.AppendLine(found ? "KEYRINGWRITE OK" : "KEYRINGWRITE FAILED");
                return found ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION " + ex);
                return 1;
            }
            finally
            {
                try { Keyring.DeleteTarget(target); } catch { }
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "keyringwrite.log"), log.ToString(), new UTF8Encoding(true));
                }
                catch { }
            }
        }
    }
}
