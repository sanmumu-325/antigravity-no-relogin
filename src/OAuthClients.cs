using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgyManager
{
    internal static class OAuthClientProvider
    {
        private static readonly object Gate = new object();
        private static List<OAuthClientPair> cached;
        private static string lastInfo = "";

        private const string ClientIdSuffix = ".apps.googleusercontent.com";

        public static List<OAuthClientPair> Load()
        {
            lock (Gate)
            {
                if (cached != null) return new List<OAuthClientPair>(cached);

                List<OAuthClientPair> pairs = new List<OAuthClientPair>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                int configCount = 0;
                foreach (OAuthClientPair pair in LoadConfig())
                {
                    string fingerprint = pair.ClientId + "\n" + pair.ClientSecret;
                    if (!seen.Add(fingerprint)) continue;
                    pairs.Add(pair);
                    configCount++;
                }

                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> secrets = new HashSet<string>(StringComparer.Ordinal);
                List<string> scanned = new List<string>();
                foreach (string path in CandidateBinaries())
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        ScanBinary(path, ids, secrets);
                        scanned.Add(path);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("OAuth parameter scan failed for " + path + ": " + ex.Message);
                    }
                }

                List<string> idList = new List<string>(ids);
                List<string> secretList = new List<string>(secrets);
                idList.Sort(StringComparer.Ordinal);
                secretList.Sort(StringComparer.Ordinal);
                int autoIndex = 0;
                foreach (string id in idList)
                {
                    foreach (string secret in secretList)
                    {
                        string fingerprint = id + "\n" + secret;
                        if (!seen.Add(fingerprint)) continue;
                        pairs.Add(new OAuthClientPair("auto-" + (++autoIndex), id, secret));
                    }
                }

                cached = pairs;
                lastInfo = string.Format(
                    "config={0} autoIds={1} autoSecrets={2} pairs={3} scanned={4}",
                    configCount, idList.Count, secretList.Count, pairs.Count,
                    string.Join(";", scanned.ToArray()));
                Log.Info("OAuth clients: " + lastInfo);
                return new List<OAuthClientPair>(pairs);
            }
        }

        public static string Describe()
        {
            Load();
            return lastInfo;
        }

        private static List<OAuthClientPair> LoadConfig()
        {
            List<OAuthClientPair> result = new List<OAuthClientPair>();
            try
            {
                if (!File.Exists(AppPaths.OauthClientsFile)) return result;
                object root = JsonUtil.Deserialize(
                    File.ReadAllText(AppPaths.OauthClientsFile, Encoding.UTF8));
                object[] items = root as object[];
                if (items == null)
                {
                    items = JsonUtil.AsArray(JsonUtil.GetPath(root, "clients"));
                }
                if (items == null)
                {
                    items = new object[] { root };
                }
                int index = 0;
                foreach (object item in items)
                {
                    Dictionary<string, object> obj = JsonUtil.AsObject(item);
                    if (obj == null) continue;
                    string id = JsonUtil.GetString(obj, "clientId")
                        ?? JsonUtil.GetString(obj, "client_id");
                    string secret = JsonUtil.GetString(obj, "clientSecret")
                        ?? JsonUtil.GetString(obj, "client_secret");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) continue;
                    string key = JsonUtil.GetString(obj, "key");
                    if (string.IsNullOrEmpty(key)) key = "local-" + (++index);
                    result.Add(new OAuthClientPair(key, id.Trim(), secret.Trim()));
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取 oauth-clients.json 失败: " + ex.Message);
            }
            return result;
        }

        private static IEnumerable<string> CandidateBinaries()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> paths = new List<string>();
            string appDir = Path.GetDirectoryName(AppPaths.AppExe);
            if (!string.IsNullOrEmpty(appDir))
            {
                AddCandidate(paths, seen, Path.Combine(appDir, "resources", "bin", "language_server.exe"));
                AddCandidate(paths, seen, Path.Combine(appDir, "resources", "app.asar"));
            }
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(local))
            {
                AddCandidate(paths, seen, Path.Combine(local,
                    "Programs", "antigravity", "resources", "bin", "language_server.exe"));
                AddCandidate(paths, seen, Path.Combine(local,
                    "Programs", "antigravity", "resources", "app.asar"));
                AddCandidate(paths, seen, Path.Combine(local,
                    "Programs", "Antigravity", "resources", "bin", "language_server.exe"));
            }
            return paths;
        }

        private static void AddCandidate(List<string> paths, HashSet<string> seen, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (seen.Add(path)) paths.Add(path);
        }

        private static void ScanBinary(string path, HashSet<string> ids, HashSet<string> secrets)
        {
            const int chunkSize = 4 * 1024 * 1024;
            const int overlap = 512;
            Encoding latin1 = Encoding.GetEncoding(28591);
            byte[] buffer = new byte[chunkSize + overlap];
            int carry = 0;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                while (true)
                {
                    int read = stream.Read(buffer, carry, chunkSize);
                    if (read <= 0) break;
                    int total = carry + read;
                    string text = latin1.GetString(buffer, 0, total);
                    FindClientIds(text, ids);
                    FindClientSecrets(text, secrets);
                    carry = Math.Min(overlap, total);
                    if (carry > 0) Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
                }
            }
        }

        private static void FindClientIds(string text, HashSet<string> ids)
        {
            int index = 0;
            while (index < text.Length)
            {
                int suffix = text.IndexOf(ClientIdSuffix, index, StringComparison.Ordinal);
                if (suffix < 0) break;
                int start = suffix - 1;
                while (start >= 0 && IsIdChar(text[start])) start--;
                int length = suffix - start - 1;
                if (length >= 16 && length <= 160)
                {
                    string value = text.Substring(start + 1, length + ClientIdSuffix.Length);
                    ids.Add(value);
                }
                index = suffix + ClientIdSuffix.Length;
            }
        }

        private static void FindClientSecrets(string text, HashSet<string> secrets)
        {
            string prefix = "GOCSP" + "X-";
            int index = 0;
            while (index < text.Length)
            {
                int found = text.IndexOf(prefix, index, StringComparison.Ordinal);
                if (found < 0) break;
                int end = found + prefix.Length;
                while (end < text.Length && IsSecretChar(text[end])) end++;
                int bodyLength = end - found - prefix.Length;
                if (bodyLength >= 12 && bodyLength <= 200)
                {
                    secrets.Add(text.Substring(found, end - found));
                }
                index = end > found ? end : found + prefix.Length;
            }
        }

        private static bool IsIdChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_';
        }

        private static bool IsSecretChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') || c == '-' || c == '_';
        }
    }
}
