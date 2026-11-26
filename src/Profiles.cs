using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AgyManager
{
    internal sealed class ProfileStore
    {
        public List<Profile> LoadAll()
        {
            List<Profile> profiles = new List<Profile>();
            if (!Directory.Exists(AppPaths.ProfileDir)) return profiles;
            foreach (string directory in Directory.GetDirectories(AppPaths.ProfileDir))
            {
                try
                {
                    Profile profile = LoadOne(directory);
                    if (profile != null) profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    Log.Error("读取账号目录失败 " + directory + ": " + ex.Message);
                }
            }
            return profiles.OrderByDescending(p => p.SavedAt ?? "").ToList();
        }

        public Profile LoadOne(string directory)
        {
            string metaPath = Path.Combine(directory, "meta.json");
            Profile profile = new Profile();
            profile.Directory = directory;
            profile.Name = Path.GetFileName(directory);
            profile.HasTokenFile = File.Exists(Path.Combine(directory, "token.json"));
            profile.HasKeyring = File.Exists(Path.Combine(directory, "keyring.json"));
            if (File.Exists(metaPath))
            {
                Dictionary<string, object> meta = JsonUtil.AsObject(
                    JsonUtil.Deserialize(File.ReadAllText(metaPath, Encoding.UTF8)));
                profile.Name = JsonUtil.GetString(meta, "name") ?? profile.Name;
                profile.Email = JsonUtil.GetString(meta, "email");
                profile.SavedAt = JsonUtil.GetString(meta, "savedAt");
                profile.Plan = JsonUtil.GetString(meta, "plan");
            }
            if (string.IsNullOrEmpty(profile.Email))
            {
                TokenData token = TokenStore.LoadProfileToken(profile);
                if (token != null && JsonUtil.IsEmail(token.Email)) profile.Email = token.Email;
            }
            profile.Quota = LoadQuota(directory);
            if (profile.Quota != null && profile.Plan == null) profile.Plan = profile.Quota.Plan;
            return profile;
        }

        public Profile SaveCurrent(string emailOverride)
        {
            TokenData live = TokenStore.LoadLive();
            if (live == null)
            {
                throw new InvalidOperationException(
                    "没有找到登录令牌。请先在反重力里登录账号，再点“保存当前账号”。");
            }
            string email = JsonUtil.IsEmail(live.Email) ? live.Email : emailOverride;
            if (!JsonUtil.IsEmail(email))
            {
                throw new InvalidOperationException("无法确定当前账号邮箱，请手动输入后再保存。");
            }
            email = email.Trim().ToLowerInvariant();
            string slug = SlugFromEmail(email);
            string directory = Path.Combine(AppPaths.ProfileDir, slug);
            Profile existing = Directory.Exists(directory) ? LoadOne(directory) : null;
            Profile profile = new Profile();
            profile.Directory = directory;
            profile.Email = email;
            profile.Name = existing != null && !string.IsNullOrEmpty(existing.Name) ? existing.Name : slug;
            profile.Plan = existing != null ? existing.Plan : null;
            profile.SavedAt = DateTime.Now.ToString("s");
            Directory.CreateDirectory(directory);
            TokenStore.Snapshot(profile, email);
            WriteMeta(profile);
            profile.HasTokenFile = File.Exists(Path.Combine(directory, "token.json"));
            profile.HasKeyring = File.Exists(Path.Combine(directory, "keyring.json"));
            Log.Info("Saved account " + email + " to " + directory);
            return profile;
        }

        public void Rename(Profile profile, string label)
        {
            if (profile == null || string.IsNullOrWhiteSpace(label)) return;
            profile.Name = label.Trim();
            WriteMeta(profile);
        }

        public void Delete(Profile profile)
        {
            if (profile == null || !Directory.Exists(profile.Directory)) return;
            string full = Path.GetFullPath(profile.Directory);
            string root = Path.GetFullPath(AppPaths.ProfileDir);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("账号目录不在配置目录内，已拒绝删除。");
            Directory.Delete(full, true);
            Log.Info("Deleted profile " + full);
        }

        public void Switch(Profile profile)
        {
            if (profile == null) throw new InvalidOperationException("没有选择账号。");
            if (!HasRestorableState(profile))
                throw new InvalidOperationException("这个账号没有可恢复的令牌，请重新登录并保存。");
            StopAntigravity();
            TokenStore.Restore(profile);
            TokenStore.PatchAppStorage(profile.Email);
            StartAntigravity();
            Log.Info("Switched to " + profile.Email);
        }

        private static bool HasRestorableState(Profile profile)
        {
            return File.Exists(Path.Combine(profile.Directory, "token.json"))
                || File.Exists(Path.Combine(profile.Directory, "cli-token.json"))
                || File.Exists(Path.Combine(profile.Directory, "keyring.json"));
        }

        public static void StopAntigravity()
        {
            List<Process> targets = new List<Process>();
            CollectProcess("Antigravity", true, targets);
            CollectProcess("language_server", false, targets);
            CollectProcess("agy", false, targets);
            if (targets.Count == 0) return;
            Log.Info("Stopping " + targets.Count + " Antigravity processes");
            foreach (Process process in targets)
            {
                try { process.Kill(); }
                catch (Exception ex) { Log.Error("Kill failed: " + ex.Message); }
            }
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                bool any = false;
                foreach (Process process in targets)
                {
                    try
                    {
                        if (!process.HasExited) { any = true; break; }
                    }
                    catch { }
                }
                if (!any) return;
                Thread.Sleep(250);
            }
        }

        private static void CollectProcess(string name, bool killUnknownPath, List<Process> targets)
        {
            Process[] processes = Process.GetProcessesByName(name);
            foreach (Process process in processes)
            {
                string path = null;
                try { path = process.MainModule.FileName; }
                catch { }
                bool isOurs;
                if (path == null)
                {
                    isOurs = killUnknownPath;
                }
                else
                {
                    isOurs = path.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0
                        || path.IndexOf("gemini", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (isOurs) targets.Add(process);
                else process.Dispose();
            }
        }

        public static void StartAntigravity()
        {
            if (!File.Exists(AppPaths.AppExe)) return;
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = AppPaths.AppExe;
            info.WorkingDirectory = Path.GetDirectoryName(AppPaths.AppExe);
            info.UseShellExecute = true;
            Process.Start(info);
        }

        private static string SlugFromEmail(string email)
        {
            string value = email.Replace("@", "_at_");
            value = Regex.Replace(value, "[^A-Za-z0-9._-]", "_");
            value = value.Trim('.', '_', '-');
            if (value.Length > 80) value = value.Substring(0, 80);
            if (string.IsNullOrEmpty(value)) value = "account-" + DateTime.Now.ToString("HHmmss");
            return value.ToLowerInvariant();
        }

        private static void WriteMeta(Profile profile)
        {
            Directory.CreateDirectory(profile.Directory);
            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["name"] = profile.Name;
            meta["email"] = profile.Email;
            meta["savedAt"] = profile.SavedAt ?? DateTime.Now.ToString("s");
            if (!string.IsNullOrEmpty(profile.Plan)) meta["plan"] = profile.Plan;
            File.WriteAllText(Path.Combine(profile.Directory, "meta.json"),
                JsonUtil.Serialize(meta), new UTF8Encoding(false));
        }

        public static QuotaSnapshot LoadQuota(string directory)
        {
            try
            {
                string path = Path.Combine(directory, "quota.json");
                if (!File.Exists(path)) return null;
                Dictionary<string, object> obj = JsonUtil.AsObject(
                    JsonUtil.Deserialize(File.ReadAllText(path, Encoding.UTF8)));
                if (obj == null) return null;
                QuotaSnapshot snapshot = new QuotaSnapshot();
                snapshot.Email = JsonUtil.GetString(obj, "email");
                snapshot.Plan = JsonUtil.GetString(obj, "plan");
                snapshot.Error = JsonUtil.GetString(obj, "error");
                snapshot.Source = JsonUtil.GetString(obj, "source");
                snapshot.FetchedAt = JsonUtil.ParseTime(JsonUtil.GetPath(obj, "fetchedAt"));
                object[] groups = JsonUtil.AsArray(JsonUtil.GetPath(obj, "groups"));
                if (groups != null)
                {
                    foreach (object groupObj in groups)
                    {
                        Dictionary<string, object> groupDict = JsonUtil.AsObject(groupObj);
                        if (groupDict == null) continue;
                        QuotaGroup group = new QuotaGroup();
                        group.Name = JsonUtil.GetString(groupDict, "name");
                        object[] buckets = JsonUtil.AsArray(JsonUtil.GetPath(groupDict, "buckets"));
                        if (buckets != null)
                        {
                            foreach (object bucketObj in buckets)
                            {
                                Dictionary<string, object> bucketDict = JsonUtil.AsObject(bucketObj);
                                if (bucketDict == null) continue;
                                QuotaBucket bucket = new QuotaBucket();
                                bucket.Id = JsonUtil.GetString(bucketDict, "id");
                                bucket.DisplayName = JsonUtil.GetString(bucketDict, "displayName");
                                bucket.Window = JsonUtil.GetString(bucketDict, "window");
                                bucket.Remaining = JsonUtil.GetDouble(bucketDict, "remaining");
                                bucket.Reset = JsonUtil.ParseTime(JsonUtil.GetPath(bucketDict, "reset"));
                                group.Buckets.Add(bucket);
                            }
                        }
                        snapshot.Groups.Add(group);
                    }
                }
                return snapshot;
            }
            catch (Exception ex)
            {
                Log.Error("读取额度缓存失败: " + ex.Message);
                return null;
            }
        }

        public static void SaveQuota(Profile profile, QuotaSnapshot snapshot)
        {
            try
            {
                Dictionary<string, object> obj = new Dictionary<string, object>();
                obj["email"] = profile.Email;
                obj["plan"] = snapshot.Plan;
                obj["fetchedAt"] = snapshot.FetchedAt.HasValue
                    ? snapshot.FetchedAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                    : null;
                obj["error"] = snapshot.Error;
                obj["source"] = snapshot.Source;
                List<object> groups = new List<object>();
                foreach (QuotaGroup group in snapshot.Groups)
                {
                    Dictionary<string, object> groupObj = new Dictionary<string, object>();
                    groupObj["name"] = group.Name;
                    List<object> buckets = new List<object>();
                    foreach (QuotaBucket bucket in group.Buckets)
                    {
                        Dictionary<string, object> bucketObj = new Dictionary<string, object>();
                        bucketObj["id"] = bucket.Id;
                        bucketObj["displayName"] = bucket.DisplayName;
                        bucketObj["window"] = bucket.Window;
                        bucketObj["remaining"] = bucket.Remaining;
                        bucketObj["reset"] = bucket.Reset.HasValue
                            ? bucket.Reset.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                            : null;
                        buckets.Add(bucketObj);
                    }
                    groupObj["buckets"] = buckets;
                    groups.Add(groupObj);
                }
                obj["groups"] = groups;
                File.WriteAllText(Path.Combine(profile.Directory, "quota.json"),
                    JsonUtil.Serialize(obj), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("保存额度缓存失败: " + ex.Message);
            }
        }
    }
}
