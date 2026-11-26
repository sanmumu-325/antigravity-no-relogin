using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AgyManager
{
    internal sealed class TokenRevokedException : Exception
    {
        public TokenRevokedException(string message) : base(message) { }
    }

    internal static class AppPaths
    {
        public static readonly string Home = ResolveHome();
        public static readonly string GeminiDir = Path.Combine(Home, ".gemini");
        public static readonly string TokenFile = Path.Combine(GeminiDir, "jetski-standalone-oauth-token");
        public static readonly string CliTokenFile = Path.Combine(GeminiDir, "antigravity-cli", "antigravity-oauth-token");
        public static readonly string OauthCredsFile = Path.Combine(GeminiDir, "oauth_creds.json");
        public static readonly string RootDir = Path.Combine(Home, ".antigravity-switch");
        public static readonly string ProfileDir = Path.Combine(RootDir, "profiles");
        public static readonly string BackupDir = Path.Combine(RootDir, "backups");
        public static readonly string LogDir = Path.Combine(RootDir, "logs");
        public static readonly string ConfigFile = Path.Combine(RootDir, "config.json");
        public static readonly string OauthClientsFile = Path.Combine(RootDir, "oauth-clients.json");
        public static readonly string AppStorageFile = Path.Combine(
            Environment.GetEnvironmentVariable("APPDATA") ?? Path.Combine(Home, "AppData", "Roaming"),
            "Antigravity", "app_storage.json");
        public static readonly string AppExe = FindAppExe();

        private static string FindAppExe()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Path.Combine(Home, "AppData", "Local");
            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles")
                ?? @"C:\Program Files";
            string programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)")
                ?? @"C:\Program Files (x86)";
            string[] candidates =
            {
                Path.Combine(local, "Programs", "antigravity", "Antigravity.exe"),
                Path.Combine(local, "Programs", "Antigravity", "Antigravity.exe"),
                Path.Combine(programFiles, "Antigravity", "Antigravity.exe"),
                Path.Combine(programFilesX86, "Antigravity", "Antigravity.exe"),
                Path.Combine(programFiles, "Google", "Antigravity", "Antigravity.exe")
            };
            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return candidates[0];
        }

        private static string ResolveHome()
        {
            string home = Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home;
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    string path = Path.Combine(AppPaths.LogDir, "app.log");
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > 2 * 1024 * 1024)
                    {
                        File.Copy(path, path + ".old", true);
                        File.Delete(path);
                    }
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message +
                        Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }

    internal static class ConfigStore
    {
        public static string GetPreferredClient()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return null;
                Dictionary<string, object> obj = JsonUtil.AsObject(
                    JsonUtil.Deserialize(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8)));
                return JsonUtil.GetString(obj, "oauthClient");
            }
            catch
            {
                return null;
            }
        }

        public static void SetPreferredClient(string key)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RootDir);
                Dictionary<string, object> obj = new Dictionary<string, object> { { "oauthClient", key } };
                File.WriteAllText(AppPaths.ConfigFile, JsonUtil.Serialize(obj), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    internal static class OAuthRefresher
    {
        public static async Task RefreshAsync(TokenData token, CancellationToken cancellationToken)
        {
            if (token == null) throw new InvalidOperationException("没有可用的登录令牌。");
            if (string.IsNullOrEmpty(token.RefreshToken))
                throw new TokenRevokedException("令牌没有 refresh_token，请重新登录反重力。");

            List<OAuthClientPair> clients = OAuthClientProvider.Load();
            if (clients.Count == 0)
            {
                throw new InvalidOperationException(
                    "没有找到 OAuth 客户端参数。请确认 Antigravity 已安装，或在 " +
                    AppPaths.OauthClientsFile + " 中配置 clientId/clientSecret。");
            }
            string preferred = ConfigStore.GetPreferredClient();
            List<OAuthClientPair> ordered = new List<OAuthClientPair>();
            foreach (OAuthClientPair pair in clients)
            {
                if (pair.Key == preferred) ordered.Add(pair);
            }
            foreach (OAuthClientPair pair in clients)
            {
                if (!ordered.Contains(pair)) ordered.Add(pair);
            }

            string lastError = null;
            using (HttpClient client = HttpUtil.CreateClient(TimeSpan.FromSeconds(15)))
            {
                foreach (OAuthClientPair pair in ordered)
                {
                    List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>();
                    form.Add(new KeyValuePair<string, string>("client_id", pair.ClientId));
                    form.Add(new KeyValuePair<string, string>("client_secret", pair.ClientSecret));
                    form.Add(new KeyValuePair<string, string>("refresh_token", token.RefreshToken));
                    form.Add(new KeyValuePair<string, string>("grant_type", "refresh_token"));
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post,
                        "https://oauth2.googleapis.com/token"))
                    {
                        request.Content = new FormUrlEncodedContent(form);
                        request.Headers.TryAddWithoutValidation("User-Agent", "antigravity/2.0 windows/amd64");
                        using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (response.IsSuccessStatusCode)
                            {
                                Dictionary<string, object> obj = JsonUtil.AsObject(JsonUtil.Deserialize(body));
                                string access = JsonUtil.GetString(obj, "access_token");
                                if (string.IsNullOrEmpty(access))
                                {
                                    lastError = "刷新响应缺少 access_token";
                                    continue;
                                }
                                double? expires = JsonUtil.GetDouble(obj, "expires_in");
                                string newRefresh = JsonUtil.GetString(obj, "refresh_token");
                                string idToken = JsonUtil.GetString(obj, "id_token");
                                token.ApplyRefresh(access, expires.HasValue ? (int)expires.Value : 3600,
                                    newRefresh, idToken);
                                ConfigStore.SetPreferredClient(pair.Key);
                                Log.Info("OAuth token refreshed with client " + pair.Key);
                                return;
                            }

                            Dictionary<string, object> errorObj =
                                JsonUtil.AsObject(JsonUtil.Deserialize(body));
                            string errorCode = JsonUtil.GetString(errorObj, "error");
                            string description = JsonUtil.GetString(errorObj, "error_description");
                            lastError = "HTTP " + (int)response.StatusCode + " " + errorCode + " " + description;
                            if (errorCode == "invalid_grant" ||
                                (description != null && description.IndexOf("expired or revoked",
                                    StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                throw new TokenRevokedException(
                                    "登录已失效（refresh_token 被撤销），请重新登录反重力。");
                            }
                        }
                    }
                }
            }
            throw new InvalidOperationException("刷新登录令牌失败：" + (lastError ?? "未知错误"));
        }
    }

    internal static class TokenStore
    {
        public static TokenData LoadLive()
        {
            TokenData token = LoadFile(AppPaths.TokenFile, false);
            if (token != null) return token;
            return LoadFromKeyring();
        }

        public static TokenData LoadFile(string path, bool fromKeyring)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = ReadAllTextShared(path);
                return TokenData.FromJson(text, fromKeyring);
            }
            catch (Exception ex)
            {
                Log.Error("读取令牌失败 " + path + ": " + ex.Message);
                return null;
            }
        }

        public static TokenData LoadFromKeyring()
        {
            string blob = Keyring.FirstTokenBlob();
            if (string.IsNullOrEmpty(blob)) return null;
            return TokenData.FromJson(blob, true);
        }

        public static TokenData LoadProfileToken(Profile profile)
        {
            TokenData token = LoadFile(Path.Combine(profile.Directory, "token.json"), false);
            if (token != null) return token;
            token = LoadFile(Path.Combine(profile.Directory, "cli-token.json"), false);
            if (token != null) return token;
            try
            {
                string keyringPath = Path.Combine(profile.Directory, "keyring.json");
                if (!File.Exists(keyringPath)) return null;
                object parsed = JsonUtil.Deserialize(File.ReadAllText(keyringPath, Encoding.UTF8));
                object[] records = parsed as object[];
                if (records == null)
                {
                    object single = parsed;
                    records = new object[] { single };
                }
                foreach (object item in records)
                {
                    Dictionary<string, object> obj = JsonUtil.AsObject(item);
                    string blob = JsonUtil.GetString(obj, "blob");
                    if (string.IsNullOrEmpty(blob)) continue;
                    blob = DecodeKeyringBlob(blob);
                    TokenData candidate = TokenData.FromJson(blob, true);
                    if (candidate != null && (candidate.HasAccessToken() ||
                        !string.IsNullOrEmpty(candidate.RefreshToken))) return candidate;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取账号快照失败: " + ex.Message);
            }
            return null;
        }

        public static void SaveProfileToken(Profile profile, TokenData token)
        {
            string json = token.ToJson();
            File.WriteAllText(Path.Combine(profile.Directory, "token.json"), json, new UTF8Encoding(false));
            string cliPath = Path.Combine(profile.Directory, "cli-token.json");
            if (File.Exists(cliPath))
            {
                File.WriteAllText(cliPath, json, new UTF8Encoding(false));
            }
        }

        public static string LiveEmail()
        {
            TokenData token = LoadLive();
            if (token != null && JsonUtil.IsEmail(token.Email)) return token.Email;
            return null;
        }

        public static string Snapshot(Profile profile, string email)
        {
            Directory.CreateDirectory(profile.Directory);
            string tokenPath = Path.Combine(profile.Directory, "token.json");
            string cliPath = Path.Combine(profile.Directory, "cli-token.json");
            string oauthPath = Path.Combine(profile.Directory, "oauth-creds.json");
            string keyringPath = Path.Combine(profile.Directory, "keyring.json");

            if (File.Exists(AppPaths.TokenFile)) CopyFile(AppPaths.TokenFile, tokenPath);
            else if (File.Exists(tokenPath)) File.Delete(tokenPath);
            if (File.Exists(AppPaths.CliTokenFile)) CopyFile(AppPaths.CliTokenFile, cliPath);
            if (File.Exists(AppPaths.OauthCredsFile)) CopyFile(AppPaths.OauthCredsFile, oauthPath);
            List<KeyringRecord> keyring = Keyring.ListManaged();
            if (keyring.Count > 0)
            {
                List<object> records = new List<object>();
                foreach (KeyringRecord record in keyring)
                {
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["target"] = record.Target;
                    item["username"] = record.Username;
                    item["persist"] = record.Persist;
                    item["blob"] = record.Blob;
                    records.Add(item);
                }
                File.WriteAllText(keyringPath, JsonUtil.Serialize(records), new UTF8Encoding(false));
            }
            else if (File.Exists(keyringPath))
            {
                File.Delete(keyringPath);
            }

            if (!string.IsNullOrEmpty(email) && !File.Exists(tokenPath) &&
                !File.Exists(cliPath) && !File.Exists(oauthPath) && keyring.Count == 0)
            {
                throw new InvalidOperationException(
                    "没有找到登录令牌。请先在反重力里登录一个账号，再点“保存当前账号”。");
            }
            return tokenPath;
        }

        public static void Restore(Profile profile)
        {
            string tokenPath = Path.Combine(profile.Directory, "token.json");
            string cliPath = Path.Combine(profile.Directory, "cli-token.json");
            string oauthPath = Path.Combine(profile.Directory, "oauth-creds.json");
            string keyringPath = Path.Combine(profile.Directory, "keyring.json");

            BackupCurrent();
            if (File.Exists(tokenPath)) CopyFile(tokenPath, AppPaths.TokenFile);
            else if (File.Exists(AppPaths.TokenFile)) File.Delete(AppPaths.TokenFile);

            if (File.Exists(cliPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.CliTokenFile));
                CopyFile(cliPath, AppPaths.CliTokenFile);
            }
            if (File.Exists(oauthPath)) CopyFile(oauthPath, AppPaths.OauthCredsFile);

            Keyring.DeleteAll();
            if (File.Exists(keyringPath))
            {
                object parsed = JsonUtil.Deserialize(File.ReadAllText(keyringPath, Encoding.UTF8));
                object[] records = parsed as object[];
                if (records == null) records = new object[] { parsed };
                foreach (object item in records)
                {
                    Dictionary<string, object> obj = JsonUtil.AsObject(item);
                    if (obj == null) continue;
                    KeyringRecord record = new KeyringRecord
                    {
                        Target = JsonUtil.GetString(obj, "Target") ?? JsonUtil.GetString(obj, "target"),
                        Username = JsonUtil.GetString(obj, "Username") ?? JsonUtil.GetString(obj, "username"),
                        Persist = (int)(JsonUtil.GetDouble(obj, "Persist")
                            ?? JsonUtil.GetDouble(obj, "persist") ?? 2),
                        Blob = JsonUtil.GetString(obj, "Blob") ?? JsonUtil.GetString(obj, "blob")
                    };
                    if (!string.IsNullOrEmpty(record.Target) && record.Blob != null)
                    {
                        record.Blob = DecodeKeyringBlob(record.Blob);
                        Keyring.Write(record);
                    }
                }
            }
        }

        public static void BackupCurrent()
        {
            Directory.CreateDirectory(AppPaths.BackupDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            CopyIfExists(AppPaths.TokenFile, Path.Combine(AppPaths.BackupDir, "token-" + stamp + ".bak"));
            CopyIfExists(AppPaths.AppStorageFile,
                Path.Combine(AppPaths.BackupDir, "app_storage-" + stamp + ".bak"));
        }

        public static void PatchAppStorage(string email)
        {
            if (string.IsNullOrEmpty(email) || !File.Exists(AppPaths.AppStorageFile)) return;
            BackupCurrent();
            string text = File.ReadAllText(AppPaths.AppStorageFile, Encoding.UTF8);
            string escaped = email.Replace("\\", "\\\\").Replace("\"", "\\\"");
            Regex regex = new Regex(
                "(\"agy\\.onboarding\\.lastLoginUsername\"\\s*:\\s*)\"[^\"]*\"",
                RegexOptions.CultureInvariant);
            if (regex.IsMatch(text))
            {
                text = regex.Replace(text, "$1\"" + escaped + "\"", 1);
            }
            else
            {
                int index = text.LastIndexOf('}');
                if (index > 0)
                {
                    text = text.Insert(index,
                        ",\"agy.onboarding.lastLoginUsername\":\"" + escaped + "\"");
                }
            }
            File.WriteAllText(AppPaths.AppStorageFile, text, new UTF8Encoding(false));
        }

        public static void CopyFile(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write,
                FileShare.None))
            {
                input.CopyTo(output);
            }
        }

        private static void CopyIfExists(string source, string destination)
        {
            if (!File.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            CopyFile(source, destination);
        }

        private static string ReadAllTextShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private static string DecodeKeyringBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return blob;
            const string prefix = "go-keyring-base64:";
            if (!blob.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return blob;
            try
            {
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(blob.Substring(prefix.Length)));
            }
            catch
            {
                return blob;
            }
        }
    }
}
