using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgyManager
{
    internal sealed class HttpResult
    {
        public int Status;
        public string Body;
        public string Error;

        public bool Ok
        {
            get { return Error == null && Status >= 200 && Status < 300; }
        }
    }

    internal sealed class LoadInfo
    {
        public string Project;
        public string Plan;
    }

    internal sealed class QuotaService
    {
        private static readonly string[] Hosts =
        {
            "https://daily-cloudcode-pa.googleapis.com",
            "https://daily-cloudcode-pa.sandbox.googleapis.com",
            "https://cloudcode-pa.googleapis.com"
        };

        public async Task<QuotaSnapshot> FetchAsync(Profile profile, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linked.CancelAfter(TimeSpan.FromSeconds(50));
                return await FetchCoreAsync(profile, linked.Token).ConfigureAwait(false);
            }
        }

        private async Task<QuotaSnapshot> FetchCoreAsync(Profile profile, CancellationToken cancellationToken)
        {
            QuotaSnapshot snapshot = new QuotaSnapshot();
            snapshot.Email = profile.Email;
            snapshot.FetchedAt = DateTimeOffset.Now;
            TokenData token = TokenStore.LoadProfileToken(profile);
            if (token == null)
            {
                snapshot.Error = "没有可用的登录令牌，请重新登录并保存账号。";
                return snapshot;
            }

            if (!token.AccessUsable(120) && !string.IsNullOrEmpty(token.RefreshToken))
            {
                try
                {
                    await OAuthRefresher.RefreshAsync(token, cancellationToken).ConfigureAwait(false);
                    TokenStore.SaveProfileToken(profile, token);
                    Log.Info("Refreshed token for " + profile.Email);
                }
                catch (TokenRevokedException ex)
                {
                    snapshot.Error = ex.Message;
                    return snapshot;
                }
                catch (Exception ex)
                {
                    snapshot.Warning = "令牌刷新失败，正在尝试旧令牌：" + ex.Message;
                    Log.Error("Refresh failed for " + profile.Email + ": " + ex);
                }
            }

            if (!token.AccessUsable(0))
            {
                snapshot.Error = string.IsNullOrEmpty(token.RefreshToken)
                    ? "access_token 已过期且没有 refresh_token，请重新登录反重力后保存该账号。"
                    : "access_token 已过期，自动续期没有成功。通常是网络/代理问题：确认能访问 Google 后再点“刷新全部额度”，无需重新登录。";
                return snapshot;
            }

            LoadInfo info = await LoadCodeAssistAsync(token.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            if (info != null && !string.IsNullOrEmpty(info.Plan))
            {
                snapshot.Plan = info.Plan;
                profile.Plan = info.Plan;
            }

            if (cancellationToken.IsCancellationRequested) return snapshot;

            SummaryResult summary = await FetchSummaryAsync(token.AccessToken, info, cancellationToken)
                .ConfigureAwait(false);
            string summaryError = summary.Error;
            List<QuotaGroup> groups = summary.Groups;
            if (groups.Count == 0 || !HasGemini(groups))
            {
                List<QuotaGroup> geminiFallback =
                    await FetchUserQuotaAsync(token.AccessToken, info, cancellationToken)
                    .ConfigureAwait(false);
                MergeGroups(groups, geminiFallback);
            }
            if (groups.Count == 0 || !HasThirdParty(groups))
            {
                List<QuotaGroup> modelFallback =
                    await FetchAvailableModelsAsync(token.AccessToken, info, cancellationToken)
                    .ConfigureAwait(false);
                foreach (QuotaGroup group in modelFallback)
                {
                    if ((group.IsGemini() && !HasGemini(groups)) ||
                        (group.IsThirdParty() && !HasThirdParty(groups)))
                    {
                        MergeGroups(groups, new List<QuotaGroup> { group });
                    }
                }
            }

            snapshot.Groups = groups;
            if (groups.Count == 0)
            {
                if (summaryError != null &&
                    summaryError.IndexOf("401", StringComparison.Ordinal) >= 0)
                {
                    snapshot.Error = "登录已过期，请重新登录反重力并更新该账号。";
                }
                else
                {
                    snapshot.Error = "没有取到额度数据" +
                        (string.IsNullOrEmpty(summaryError) ? "。" : "：" + summaryError);
                }
            }
            else if (snapshot.FindGemini() == null || snapshot.FindThirdParty() == null)
            {
                snapshot.Warning = "部分模型组暂未返回，可稍后刷新。";
            }
            return snapshot;
        }

        private static string AppVersion()
        {
            try
            {
                if (System.IO.File.Exists(AppPaths.AppExe))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(AppPaths.AppExe);
                    if (!string.IsNullOrEmpty(info.FileVersion))
                    {
                        string version = info.FileVersion.Trim();
                        if (version.Length > 24) version = version.Substring(0, 24);
                        return version;
                    }
                }
            }
            catch { }
            return "2.35.1";
        }

        private static string UserAgent(bool cli)
        {
            if (cli) return "antigravity/cli/1.5.0 windows/amd64";
            return "antigravity/" + AppVersion() + " windows/amd64";
        }

        private static async Task<LoadInfo> LoadCodeAssistAsync(string accessToken,
            CancellationToken cancellationToken)
        {
            string body = "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\"}}";
            foreach (string host in Hosts)
            {
                HttpResult result = await PostJsonAsync(host + "/v1internal:loadCodeAssist",
                    accessToken, UserAgent(false), body, cancellationToken).ConfigureAwait(false);
                if (!result.Ok) continue;
                object root = JsonUtil.Unwrap(JsonUtil.Deserialize(result.Body));
                Dictionary<string, object> obj = JsonUtil.AsObject(root);
                if (obj == null) continue;
                LoadInfo info = new LoadInfo();
                object project = JsonUtil.GetPath(obj, "cloudaicompanionProject");
                info.Project = project as string;
                if (info.Project == null)
                {
                    Dictionary<string, object> projectObj = JsonUtil.AsObject(project);
                    if (projectObj != null) info.Project = JsonUtil.GetString(projectObj, "id");
                }
                info.Plan = ReadTier(obj, "paidTier") ?? ReadTier(obj, "currentTier");
                if (!string.IsNullOrEmpty(info.Project) || !string.IsNullOrEmpty(info.Plan))
                {
                    Log.Info("loadCodeAssist ok on " + host + " project=" + info.Project +
                        " plan=" + info.Plan);
                    return info;
                }
            }
            return new LoadInfo();
        }

        private static string ReadTier(Dictionary<string, object> obj, string key)
        {
            Dictionary<string, object> tier = JsonUtil.AsObject(JsonUtil.GetPath(obj, key));
            if (tier == null) return null;
            string name = JsonUtil.GetString(tier, "name");
            if (!string.IsNullOrEmpty(name)) return NormalizePlan(name);
            string id = JsonUtil.GetString(tier, "id");
            if (!string.IsNullOrEmpty(id)) return NormalizePlan(id);
            return null;
        }

        private static string NormalizePlan(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string lower = raw.ToLowerInvariant();
            if (lower.IndexOf("ultra", StringComparison.Ordinal) >= 0) return "Google AI Ultra";
            if (lower.IndexOf("pro", StringComparison.Ordinal) >= 0) return "Google AI Pro";
            if (lower.IndexOf("free", StringComparison.Ordinal) >= 0) return "免费层";
            return raw;
        }

        private sealed class Attempt
        {
            public string Host;
            public bool CliUserAgent;
            public bool WithProject;
            public string FixedProject;
        }

        private sealed class SummaryResult
        {
            public List<QuotaGroup> Groups = new List<QuotaGroup>();
            public string Error;
        }

        private static async Task<SummaryResult> FetchSummaryAsync(string accessToken, LoadInfo info,
            CancellationToken cancellationToken)
        {
            List<QuotaGroup> groups = new List<QuotaGroup>();
            List<Attempt> attempts = new List<Attempt>();
            attempts.Add(new Attempt { Host = Hosts[0], CliUserAgent = true, WithProject = true });
            attempts.Add(new Attempt { Host = Hosts[0], CliUserAgent = false, WithProject = true });
            attempts.Add(new Attempt { Host = Hosts[0], CliUserAgent = true, WithProject = false });
            attempts.Add(new Attempt { Host = Hosts[1], CliUserAgent = true, WithProject = true });
            attempts.Add(new Attempt { Host = Hosts[2], CliUserAgent = true, WithProject = true });
            attempts.Add(new Attempt { Host = Hosts[0], CliUserAgent = true, FixedProject = "aicode-consumers" });
            attempts.Add(new Attempt { Host = Hosts[2], CliUserAgent = false, WithProject = true });
            attempts.Add(new Attempt { Host = Hosts[0], CliUserAgent = false, WithProject = false });

            string lastError = null;
            bool triedCli = false;
            bool triedDesktop = false;
            foreach (Attempt attempt in attempts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (groups.Count > 0 && ((HasGemini(groups) && HasThirdParty(groups)) ||
                    (triedCli && triedDesktop && HasGemini(groups)))) break;
                string body;
                if (!string.IsNullOrEmpty(attempt.FixedProject))
                    body = "{\"project\":" + Quote(attempt.FixedProject) + "}";
                else
                    body = attempt.WithProject && !string.IsNullOrEmpty(info.Project)
                        ? "{\"project\":" + Quote(info.Project) + "}"
                        : "{}";
                HttpResult result = await PostJsonAsync(
                    attempt.Host + "/v1internal:retrieveUserQuotaSummary",
                    accessToken, UserAgent(attempt.CliUserAgent), body,
                    cancellationToken).ConfigureAwait(false);
                if (!result.Ok)
                {
                    if (result.Error != null)
                        lastError = result.Error;
                    else if (result.Status != 0)
                        lastError = "HTTP " + result.Status + " " + Trim(result.Body);
                    continue;
                }
                List<QuotaGroup> parsed = ParseSummary(result.Body);
                if (parsed.Count == 0) continue;
                MergeGroups(groups, parsed);
                if (attempt.Host == Hosts[0])
                {
                    if (attempt.CliUserAgent) triedCli = true;
                    else triedDesktop = true;
                }
                Log.Info("quota summary ok on " + attempt.Host + " groups=" + parsed.Count +
                    " cli=" + attempt.CliUserAgent);
            }
            if (groups.Count == 0 && lastError != null) Log.Error("quota summary failed: " + lastError);
            SummaryResult summaryResult = new SummaryResult();
            summaryResult.Groups = groups;
            summaryResult.Error = lastError;
            return summaryResult;
        }

        private static async Task<List<QuotaGroup>> FetchUserQuotaAsync(string accessToken, LoadInfo info,
            CancellationToken cancellationToken)
        {
            List<QuotaGroup> groups = new List<QuotaGroup>();
            string[] hosts = { Hosts[0], Hosts[2] };
            foreach (string host in hosts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                string body = !string.IsNullOrEmpty(info.Project)
                    ? "{\"project\":" + Quote(info.Project) + "}"
                    : "{}";
                HttpResult result = await PostJsonAsync(host + "/v1internal:retrieveUserQuota",
                    accessToken, UserAgent(true), body, cancellationToken).ConfigureAwait(false);
                if (!result.Ok) continue;
                groups = ParseUserQuota(result.Body);
                if (groups.Count > 0) break;
            }
            return groups;
        }

        private static async Task<List<QuotaGroup>> FetchAvailableModelsAsync(string accessToken,
            LoadInfo info, CancellationToken cancellationToken)
        {
            List<QuotaGroup> resultGroups = new List<QuotaGroup>();
            string[] hosts = { Hosts[0], Hosts[2] };
            foreach (string host in hosts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                HttpResult result = await PostJsonAsync(host + "/v1internal:fetchAvailableModels",
                    accessToken, UserAgent(true), "{}", cancellationToken).ConfigureAwait(false);
                if (!result.Ok) continue;
                resultGroups = ParseAvailableModels(result.Body);
                if (resultGroups.Count > 0) break;
            }
            return resultGroups;
        }

        private static async Task<HttpResult> PostJsonAsync(string url, string accessToken, string userAgent,
            string body, CancellationToken cancellationToken)
        {
            HttpResult result = new HttpResult();
            try
            {
                using (HttpClient client = HttpUtil.CreateClient(TimeSpan.FromSeconds(8)))
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
                    request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                    request.Headers.TryAddWithoutValidation("Accept", "application/json");
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        result.Status = (int)response.StatusCode;
                        result.Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            result.Error = "HTTP " + result.Status;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                result.Error = "网络连接失败：" + ex.Message +
                    (ex.InnerException != null ? "（" + ex.InnerException.Message + "）" : "");
            }
            catch (OperationCanceledException)
            {
                result.Error = "timeout";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        private static List<QuotaGroup> ParseSummary(string json)
        {
            List<QuotaGroup> groups = new List<QuotaGroup>();
            object root = JsonUtil.Unwrap(JsonUtil.Deserialize(json));
            object[] groupArray = JsonUtil.AsArray(JsonUtil.GetPath(root, "groups"));
            if (groupArray == null) return groups;
            foreach (object groupValue in groupArray)
            {
                Dictionary<string, object> groupObj = JsonUtil.AsObject(groupValue);
                if (groupObj == null) continue;
                QuotaGroup group = new QuotaGroup();
                group.Name = JsonUtil.GetString(groupObj, "displayName")
                    ?? JsonUtil.GetString(groupObj, "display_name");
                object[] bucketArray = JsonUtil.AsArray(JsonUtil.GetPath(groupObj, "buckets"));
                if (bucketArray == null) continue;
                foreach (object bucketValue in bucketArray)
                {
                    Dictionary<string, object> bucketObj = JsonUtil.AsObject(bucketValue);
                    if (bucketObj == null) continue;
                    QuotaBucket bucket = new QuotaBucket();
                    bucket.Id = JsonUtil.GetString(bucketObj, "bucketId")
                        ?? JsonUtil.GetString(bucketObj, "bucket_id");
                    bucket.DisplayName = JsonUtil.GetString(bucketObj, "displayName")
                        ?? JsonUtil.GetString(bucketObj, "display_name");
                    bucket.Window = NormalizeWindow(JsonUtil.GetString(bucketObj, "window"), bucket.Id);
                    bucket.Remaining = JsonUtil.GetDouble(bucketObj, "remainingFraction");
                    if (!bucket.Remaining.HasValue)
                        bucket.Remaining = JsonUtil.GetDouble(bucketObj, "remaining_fraction");
                    bucket.Reset = JsonUtil.ParseTime(JsonUtil.GetPath(bucketObj, "resetTime"));
                    if (!bucket.Reset.HasValue)
                        bucket.Reset = JsonUtil.ParseTime(JsonUtil.GetPath(bucketObj, "reset_time"));
                    bucket.Disabled = JsonUtil.GetBool(bucketObj, "disabled") ?? false;
                    if (bucket.Disabled) bucket.Remaining = null;
                    if (bucket.Remaining.HasValue)
                    {
                        double value = bucket.Remaining.Value;
                        if (value > 1.0 && value <= 100.0) value = value / 100.0;
                        if (value < 0) value = 0;
                        if (value > 1) value = 1;
                        bucket.Remaining = value;
                    }
                    group.Buckets.Add(bucket);
                }
                if (group.Buckets.Count == 0) continue;
                if (string.IsNullOrEmpty(group.Name)) group.Name = InferGroupName(group);
                groups.Add(group);
            }
            return groups;
        }

        internal static List<QuotaGroup> ParseSummaryForTest(string json)
        {
            return ParseSummary(json);
        }

        private static List<QuotaGroup> ParseUserQuota(string json)
        {
            List<QuotaGroup> groups = new List<QuotaGroup>();
            object root = JsonUtil.Unwrap(JsonUtil.Deserialize(json));
            object[] items = JsonUtil.AsArray(JsonUtil.GetPath(root, "buckets"));
            if (items == null) items = JsonUtil.AsArray(JsonUtil.GetPath(root, "models"));
            if (items == null)
            {
                Dictionary<string, object> models =
                    JsonUtil.AsObject(JsonUtil.GetPath(root, "models"));
                if (models != null)
                {
                    List<object> values = new List<object>();
                    foreach (KeyValuePair<string, object> pair in models)
                    {
                        Dictionary<string, object> item = JsonUtil.AsObject(pair.Value);
                        if (item != null)
                        {
                            item["modelId"] = pair.Key;
                            values.Add(item);
                        }
                    }
                    items = values.ToArray();
                }
            }
            if (items == null)
            {
                object[] rootArray = JsonUtil.AsArray(root);
                if (rootArray != null) items = rootArray;
            }
            if (items == null) return groups;

            double worst = 1.0;
            DateTimeOffset? reset = null;
            int count = 0;
            foreach (object item in items)
            {
                Dictionary<string, object> obj = JsonUtil.AsObject(item);
                if (obj == null) continue;
                double? remaining = JsonUtil.GetDouble(obj, "remainingFraction");
                if (!remaining.HasValue) remaining = JsonUtil.GetDouble(obj, "remaining_fraction");
                if (!remaining.HasValue)
                {
                    Dictionary<string, object> quotaInfo =
                        JsonUtil.AsObject(JsonUtil.GetPath(obj, "quotaInfo"));
                    if (quotaInfo != null)
                    {
                        remaining = JsonUtil.GetDouble(quotaInfo, "remainingFraction");
                        if (!remaining.HasValue)
                            remaining = JsonUtil.GetDouble(quotaInfo, "remaining_fraction");
                    }
                }
                if (!remaining.HasValue) continue;
                double value = remaining.Value;
                if (value > 1.0 && value <= 100.0) value = value / 100.0;
                if (value < worst) worst = value;
                DateTimeOffset? itemReset = JsonUtil.ParseTime(JsonUtil.GetPath(obj, "resetTime"));
                if (!itemReset.HasValue) itemReset = JsonUtil.ParseTime(JsonUtil.GetPath(obj, "reset_time"));
                if (itemReset.HasValue && (!reset.HasValue || itemReset.Value < reset.Value)) reset = itemReset;
                count++;
            }
            if (count == 0) return groups;
            QuotaGroup group = new QuotaGroup();
            group.Name = "Gemini Models";
            QuotaBucket bucket = new QuotaBucket();
            bucket.Id = "gemini-requests";
            bucket.DisplayName = "按请求额度";
            bucket.Window = "requests";
            bucket.Remaining = worst;
            bucket.Reset = reset;
            group.Buckets.Add(bucket);
            groups.Add(group);
            return groups;
        }

        private static List<QuotaGroup> ParseAvailableModels(string json)
        {
            List<QuotaGroup> groups = new List<QuotaGroup>();
            object root = JsonUtil.Unwrap(JsonUtil.Deserialize(json));
            Dictionary<string, object> models = JsonUtil.AsObject(JsonUtil.GetPath(root, "models"));
            if (models == null)
            {
                object[] modelArray = JsonUtil.AsArray(JsonUtil.GetPath(root, "models"));
                if (modelArray != null)
                {
                    models = new Dictionary<string, object>();
                    foreach (object item in modelArray)
                    {
                        Dictionary<string, object> itemObj = JsonUtil.AsObject(item);
                        string id = null;
                        if (itemObj != null)
                        {
                            id = JsonUtil.GetString(itemObj, "modelId")
                                ?? JsonUtil.GetString(itemObj, "id")
                                ?? JsonUtil.GetString(itemObj, "name");
                        }
                        if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
                        models[id] = item;
                    }
                }
            }
            if (models == null) return groups;

            double geminiWorst = 1.0;
            DateTimeOffset? geminiReset = null;
            bool hasGemini = false;
            double thirdWorst = 1.0;
            DateTimeOffset? thirdReset = null;
            bool hasThird = false;

            foreach (KeyValuePair<string, object> pair in models)
            {
                Dictionary<string, object> model = JsonUtil.AsObject(pair.Value);
                if (model == null) continue;
                Dictionary<string, object> quotaInfo = JsonUtil.AsObject(
                    JsonUtil.GetPath(model, "quotaInfo"));
                if (quotaInfo == null) continue;
                double? remaining = JsonUtil.GetDouble(quotaInfo, "remainingFraction");
                if (!remaining.HasValue) continue;
                double value = remaining.Value;
                if (value > 1.0 && value <= 100.0) value = value / 100.0;
                string id = pair.Key.ToLowerInvariant();
                DateTimeOffset? reset = JsonUtil.ParseTime(JsonUtil.GetPath(quotaInfo, "resetTime"));
                if (id.StartsWith("gemini", StringComparison.Ordinal))
                {
                    hasGemini = true;
                    if (value < geminiWorst) { geminiWorst = value; geminiReset = reset; }
                }
                else if (id.StartsWith("claude", StringComparison.Ordinal) ||
                         id.StartsWith("gpt", StringComparison.Ordinal))
                {
                    hasThird = true;
                    if (value < thirdWorst) { thirdWorst = value; thirdReset = reset; }
                }
            }
            if (hasGemini)
            {
                QuotaGroup group = new QuotaGroup();
                group.Name = "Gemini Models";
                QuotaBucket bucket = new QuotaBucket();
                bucket.Id = "gemini-pool";
                bucket.DisplayName = "共享池";
                bucket.Window = "pool";
                bucket.Remaining = geminiWorst;
                bucket.Reset = geminiReset;
                group.Buckets.Add(bucket);
                groups.Add(group);
            }
            if (hasThird)
            {
                QuotaGroup group = new QuotaGroup();
                group.Name = "Claude and GPT models";
                QuotaBucket bucket = new QuotaBucket();
                bucket.Id = "3p-pool";
                bucket.DisplayName = "共享池";
                bucket.Window = "pool";
                bucket.Remaining = thirdWorst;
                bucket.Reset = thirdReset;
                group.Buckets.Add(bucket);
                groups.Add(group);
            }
            return groups;
        }

        private static void MergeGroups(List<QuotaGroup> target, List<QuotaGroup> incoming)
        {
            foreach (QuotaGroup group in incoming)
            {
                QuotaGroup existing = null;
                foreach (QuotaGroup candidate in target)
                {
                    bool same = (group.IsGemini() && candidate.IsGemini())
                        || (group.IsThirdParty() && candidate.IsThirdParty())
                        || string.Equals(candidate.Name, group.Name, StringComparison.OrdinalIgnoreCase);
                    if (same) { existing = candidate; break; }
                }
                if (existing == null)
                {
                    target.Add(group);
                    continue;
                }
                if (string.IsNullOrEmpty(existing.Name) || existing.Name == "Gemini Models")
                {
                    if (!string.IsNullOrEmpty(group.Name)) existing.Name = group.Name;
                }
                foreach (QuotaBucket bucket in group.Buckets)
                {
                    QuotaBucket match = null;
                    foreach (QuotaBucket candidate in existing.Buckets)
                    {
                        if (string.Equals(candidate.Window, bucket.Window, StringComparison.OrdinalIgnoreCase))
                        {
                            match = candidate;
                            break;
                        }
                    }
                    if (match == null)
                    {
                        existing.Buckets.Add(bucket);
                        continue;
                    }
                    if (bucket.Remaining.HasValue &&
                        (!match.Remaining.HasValue || bucket.Remaining.Value < match.Remaining.Value))
                    {
                        match.Remaining = bucket.Remaining;
                        match.Reset = bucket.Reset;
                    }
                    if (string.IsNullOrEmpty(match.Id)) match.Id = bucket.Id;
                }
                existing.Buckets.Sort(delegate(QuotaBucket a, QuotaBucket b)
                {
                    return WindowOrder(a.Window).CompareTo(WindowOrder(b.Window));
                });
            }
        }

        private static int WindowOrder(string window)
        {
            if (window == "5h") return 0;
            if (window == "weekly") return 1;
            if (window == "requests") return 2;
            if (window == "pool") return 3;
            return 4;
        }

        private static bool HasGemini(List<QuotaGroup> groups)
        {
            foreach (QuotaGroup group in groups) if (group.IsGemini()) return true;
            return false;
        }

        private static bool HasThirdParty(List<QuotaGroup> groups)
        {
            foreach (QuotaGroup group in groups) if (group.IsThirdParty()) return true;
            return false;
        }

        private static string InferGroupName(QuotaGroup group)
        {
            if (group.IsGemini()) return "Gemini Models";
            if (group.IsThirdParty()) return "Claude and GPT models";
            return "模型额度";
        }

        private static string NormalizeWindow(string window, string bucketId)
        {
            string value = (window ?? "").ToLowerInvariant();
            string id = (bucketId ?? "").ToLowerInvariant();
            if (value == "5h" || value == "five_hour" || id.IndexOf("5h", StringComparison.Ordinal) >= 0)
                return "5h";
            if (value == "weekly" || value == "7d" || value == "week" ||
                id.IndexOf("weekly", StringComparison.Ordinal) >= 0)
                return "weekly";
            if (value == "requests" || value == "request") return "requests";
            return value.Length > 0 ? value : "other";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string text = value.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 200) text = text.Substring(0, 200);
            return text;
        }
    }
}
