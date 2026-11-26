using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace AgyManager
{
    internal static class QuotaCheck
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                ProfileStore store = new ProfileStore();
                List<Profile> profiles = store.LoadAll();
                Profile profile = profiles.Count > 0 ? profiles[0] : store.SaveCurrent(null);
                QuotaService service = new QuotaService();
                QuotaSnapshot snapshot = service.FetchAsync(profile, CancellationToken.None)
                    .GetAwaiter().GetResult();
                log.AppendLine("email=" + profile.Email);
                log.AppendLine("plan=" + snapshot.Plan);
                log.AppendLine("error=" + snapshot.Error);
                log.AppendLine("warning=" + snapshot.Warning);
                foreach (QuotaGroup group in snapshot.Groups)
                {
                    log.AppendLine("group=" + group.Name);
                    foreach (QuotaBucket bucket in group.Buckets)
                    {
                        log.AppendLine("  bucket=" + bucket.Id + " window=" + bucket.Window +
                            " remaining=" + bucket.Remaining + " reset=" + bucket.Reset);
                    }
                }
                bool ok = snapshot.HasAnyValue();
                log.AppendLine(ok ? "QUOTACHECK OK" : "QUOTACHECK FAILED");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quotacheck.log"),
                    log.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION " + ex);
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "quotacheck.log"), log.ToString(), new UTF8Encoding(true));
                }
                catch { }
                return 1;
            }
        }
    }
}
