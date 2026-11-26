using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class SelfTest
    {
        public static int Run()
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;
            try
            {
                string summary = "{\"groups\":[{\"displayName\":\"Gemini Models\",\"buckets\":[" +
                    "{\"bucketId\":\"gemini-5h\",\"window\":\"5h\",\"remainingFraction\":0.68,\"resetTime\":\"2026-11-25T23:00:00Z\"}," +
                    "{\"bucketId\":\"gemini-weekly\",\"window\":\"weekly\",\"remainingFraction\":1,\"resetTime\":\"2026-12-01T00:00:00Z\"}]}," +
                    "{\"displayName\":\"Claude and GPT models\",\"buckets\":[" +
                    "{\"bucketId\":\"3p-5h\",\"window\":\"5h\",\"remainingFraction\":0.5,\"resetTime\":\"2026-11-25T22:00:00Z\"}]}]}";

                object parsed = JsonUtil.Unwrap(JsonUtil.Deserialize(summary));
                object[] groups = JsonUtil.AsArray(JsonUtil.GetPath(parsed, "groups"));
                Check(groups != null && groups.Length == 2, "groups parsed", log, ref failures);
                Dictionary<string, object> first = JsonUtil.AsObject(groups[0]);
                Check(JsonUtil.GetString(first, "displayName") == "Gemini Models", "group name", log, ref failures);
                object[] buckets = JsonUtil.AsArray(JsonUtil.GetPath(first, "buckets"));
                Check(buckets != null && buckets.Length == 2, "buckets parsed", log, ref failures);
                Dictionary<string, object> bucket = JsonUtil.AsObject(buckets[0]);
                Check(Math.Abs((JsonUtil.GetDouble(bucket, "remainingFraction") ?? 0) - 0.68) < 0.0001,
                    "remaining fraction", log, ref failures);
                Check(JsonUtil.ParseTime(JsonUtil.GetPath(bucket, "resetTime")).HasValue,
                    "reset time", log, ref failures);

                string jwtPayload = JsonUtil.Base64UrlEncode("{\"email\":\"demo@example.com\"}");
                string jwt = "eyJhbGciOiJub25lIn0." + jwtPayload + ".sig";
                Check(JsonUtil.EmailFromJwt(jwt) == "demo@example.com", "jwt email", log, ref failures);

                TokenData token = TokenData.FromJson(
                    "{\"token\":{\"access_token\":\"a\",\"refresh_token\":\"r\",\"id_token\":\"" + jwt + "\"," +
                    "\"expiry\":\"2099-01-01T00:00:00Z\"}}", false);
                Check(token != null && token.Email == "demo@example.com", "token email", log, ref failures);
                Check(token != null && token.AccessUsable(60), "token usable", log, ref failures);

                Check(JsonUtil.CanonicalEmail("A.B+tag@googlemail.com") == "ab@gmail.com",
                    "canonical email", log, ref failures);

                List<QuotaGroup> quotaGroups = QuotaService.ParseSummaryForTest(summary);
                Check(quotaGroups.Count == 2, "quota parser groups", log, ref failures);
                Check(quotaGroups[0].IsGemini(), "quota parser gemini", log, ref failures);
                Check(quotaGroups[1].IsThirdParty(), "quota parser third party", log, ref failures);
                Check(quotaGroups[0].Buckets.Count == 2 &&
                    Math.Abs((quotaGroups[0].Buckets[0].Remaining ?? 0) - 0.68) < 0.0001,
                    "quota parser values", log, ref failures);
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("EXCEPTION " + ex);
            }
            log.AppendLine(failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED " + failures);
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"),
                    log.ToString(), new UTF8Encoding(true));
            }
            catch { }
            return failures == 0 ? 0 : 1;
        }

        private static void Check(bool condition, string name, StringBuilder log, ref int failures)
        {
            log.AppendLine((condition ? "PASS " : "FAIL ") + name);
            if (!condition) failures++;
        }
    }
}
