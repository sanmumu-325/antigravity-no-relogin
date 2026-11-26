using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace AgyManager
{
    internal static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 200
        };

        public static object Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return Serializer.DeserializeObject(json);
        }

        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static object[] AsArray(object value)
        {
            return value as object[];
        }

        public static string GetString(Dictionary<string, object> obj, string key)
        {
            if (obj == null || key == null) return null;
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return null;
            string text = value as string;
            if (text != null) return text;
            if (value is bool) return ((bool)value) ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static double? GetDouble(Dictionary<string, object> obj, string key)
        {
            if (obj == null || key == null) return null;
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return null;
            if (value is double) return (double)value;
            if (value is float) return (double)(float)value;
            if (value is decimal) return (double)(decimal)value;
            if (value is int) return (int)value;
            if (value is long) return (long)value;
            if (value is string)
            {
                double parsed;
                if (double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        public static bool? GetBool(Dictionary<string, object> obj, string key)
        {
            if (obj == null || key == null) return null;
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return null;
            if (value is bool) return (bool)value;
            if (value is string)
            {
                bool parsed;
                if (bool.TryParse((string)value, out parsed)) return parsed;
            }
            return null;
        }

        public static object GetPath(object root, params string[] keys)
        {
            object current = root;
            foreach (string key in keys)
            {
                Dictionary<string, object> obj = AsObject(current);
                if (obj == null) return null;
                if (!obj.TryGetValue(key, out current)) return null;
            }
            return current;
        }

        public static object Unwrap(object root)
        {
            Dictionary<string, object> obj = AsObject(root);
            if (obj != null)
            {
                object response;
                if (obj.TryGetValue("response", out response))
                {
                    Dictionary<string, object> inner = AsObject(response);
                    if (inner != null) return inner;
                }
            }
            return root;
        }

        public static DateTimeOffset? ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            DateTimeOffset parsed;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return parsed;
            }
            double seconds;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            {
                return FromUnix(seconds);
            }
            return null;
        }

        public static DateTimeOffset? ParseTime(object value)
        {
            if (value == null) return null;
            if (value is DateTime) return new DateTimeOffset((DateTime)value);
            if (value is DateTimeOffset) return (DateTimeOffset)value;
            if (value is int) return FromUnix((int)value);
            if (value is long) return FromUnix((long)value);
            if (value is double) return FromUnix((double)value);
            if (value is decimal) return FromUnix((double)(decimal)value);
            return ParseTime(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static DateTimeOffset FromUnix(double value)
        {
            if (value > 100000000000.0) value = value / 1000.0;
            return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(value);
        }

        public static string Base64UrlDecode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            try
            {
                byte[] bytes = Convert.FromBase64String(padded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        public static string Base64UrlEncode(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string EmailFromJwt(string jwt)
        {
            if (string.IsNullOrEmpty(jwt)) return null;
            string[] parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            string payload = Base64UrlDecode(parts[1]);
            if (payload == null) return null;
            Dictionary<string, object> obj = AsObject(Deserialize(payload));
            if (obj == null) return null;
            string email = GetString(obj, "email");
            if (email == null) email = GetString(obj, "preferred_username");
            return email;
        }

        public static bool IsEmail(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int at = text.IndexOf('@');
            return at > 0 && at < text.Length - 1 && text.IndexOf('.', at) > at + 1;
        }

        public static string CanonicalEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "";
            string value = email.Trim().ToLowerInvariant();
            int at = value.IndexOf('@');
            if (at <= 0) return value;
            string local = value.Substring(0, at);
            string domain = value.Substring(at + 1);
            if (domain == "googlemail.com") domain = "gmail.com";
            if (domain == "gmail.com")
            {
                int plus = local.IndexOf('+');
                if (plus >= 0) local = local.Substring(0, plus);
                local = local.Replace(".", "");
            }
            return local + "@" + domain;
        }
    }
}
