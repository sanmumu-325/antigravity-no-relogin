using System;
using System.Collections.Generic;

namespace AgyManager
{
    internal sealed class TokenData
    {
        public Dictionary<string, object> Root;
        public Dictionary<string, object> Payload;
        public string AccessToken;
        public string RefreshToken;
        public string IdToken;
        public string Email;
        public DateTimeOffset? Expiry;
        public bool FromKeyring;

        public static TokenData FromJson(string json, bool fromKeyring)
        {
            object parsed = JsonUtil.Deserialize(json);
            Dictionary<string, object> root = JsonUtil.AsObject(parsed);
            if (root == null) return null;
            Dictionary<string, object> payload = JsonUtil.AsObject(JsonUtil.GetPath(root, "token"));
            if (payload == null) payload = root;
            TokenData token = new TokenData
            {
                Root = root,
                Payload = payload,
                AccessToken = JsonUtil.GetString(payload, "access_token"),
                RefreshToken = JsonUtil.GetString(payload, "refresh_token"),
                IdToken = JsonUtil.GetString(payload, "id_token"),
                FromKeyring = fromKeyring
            };
            token.Expiry = JsonUtil.ParseTime(JsonUtil.GetPath(payload, "expiry"))
                ?? JsonUtil.ParseTime(JsonUtil.GetPath(payload, "expires_at"))
                ?? JsonUtil.ParseTime(JsonUtil.GetPath(payload, "expiresAt"));
            token.Email = JsonUtil.GetString(root, "id_token_email")
                ?? JsonUtil.GetString(root, "email")
                ?? JsonUtil.GetString(payload, "id_token_email")
                ?? JsonUtil.GetString(payload, "email")
                ?? JsonUtil.EmailFromJwt(token.IdToken);
            if (token.Email == null)
            {
                string altIdToken = JsonUtil.GetString(root, "id_token");
                if (altIdToken != null) token.Email = JsonUtil.EmailFromJwt(altIdToken);
            }
            return token;
        }

        public bool HasAccessToken()
        {
            return !string.IsNullOrEmpty(AccessToken);
        }

        public bool AccessUsable(int skewSeconds)
        {
            if (string.IsNullOrEmpty(AccessToken)) return false;
            if (!Expiry.HasValue) return true;
            return Expiry.Value > DateTimeOffset.UtcNow.AddSeconds(skewSeconds);
        }

        public void ApplyRefresh(string accessToken, int expiresIn, string refreshToken, string idToken)
        {
            AccessToken = accessToken;
            if (!string.IsNullOrEmpty(refreshToken)) RefreshToken = refreshToken;
            if (!string.IsNullOrEmpty(idToken)) IdToken = idToken;
            if (expiresIn > 0) Expiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            Payload["access_token"] = AccessToken;
            Payload["expiry"] = Expiry.HasValue
                ? Expiry.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                : null;
            if (!string.IsNullOrEmpty(RefreshToken)) Payload["refresh_token"] = RefreshToken;
            if (!string.IsNullOrEmpty(IdToken)) Payload["id_token"] = IdToken;
        }

        public string ToJson()
        {
            return JsonUtil.Serialize(Root);
        }
    }

    internal sealed class OAuthClientPair
    {
        public string Key;
        public string ClientId;
        public string ClientSecret;

        public OAuthClientPair(string key, string clientId, string clientSecret)
        {
            Key = key;
            ClientId = clientId;
            ClientSecret = clientSecret;
        }
    }

    internal sealed class QuotaBucket
    {
        public string Id;
        public string DisplayName;
        public string Window;
        public double? Remaining;
        public DateTimeOffset? Reset;
        public bool Disabled;
    }

    internal sealed class QuotaGroup
    {
        public string Name;
        public List<QuotaBucket> Buckets = new List<QuotaBucket>();

        public bool IsGemini()
        {
            if (Name != null && Name.IndexOf("gemini", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (QuotaBucket bucket in Buckets)
            {
                if (bucket.Id != null && bucket.Id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool IsThirdParty()
        {
            if (Name != null &&
                (Name.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 Name.IndexOf("gpt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 Name.IndexOf("3p", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            foreach (QuotaBucket bucket in Buckets)
            {
                if (bucket.Id != null && bucket.Id.StartsWith("3p", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    internal sealed class QuotaSnapshot
    {
        public string Email;
        public string Plan;
        public DateTimeOffset? FetchedAt;
        public string Error;
        public string Warning;
        public string Source;
        public List<QuotaGroup> Groups = new List<QuotaGroup>();

        public QuotaGroup FindGemini()
        {
            foreach (QuotaGroup group in Groups)
            {
                if (group.IsGemini()) return group;
            }
            return null;
        }

        public QuotaGroup FindThirdParty()
        {
            foreach (QuotaGroup group in Groups)
            {
                if (group.IsThirdParty()) return group;
            }
            return null;
        }

        public bool HasAnyValue()
        {
            foreach (QuotaGroup group in Groups)
            {
                foreach (QuotaBucket bucket in group.Buckets)
                {
                    if (bucket.Remaining.HasValue) return true;
                }
            }
            return Groups.Count > 0;
        }
    }

    internal sealed class Profile
    {
        public string Directory;
        public string Name;
        public string Email;
        public string SavedAt;
        public string Plan;
        public bool HasTokenFile;
        public bool HasKeyring;
        public QuotaSnapshot Quota;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Name)) return Name;
                if (!string.IsNullOrEmpty(Email)) return Email.Split('@')[0];
                return "账号";
            }
        }
    }
}
