using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AgyManager
{
    internal sealed class KeyringRecord
    {
        public string Target { get; set; }
        public string Username { get; set; }
        public int Persist { get; set; }
        public string Blob { get; set; }
    }

    internal static class Keyring
    {
        private const uint CredTypeGeneric = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint reserved, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(string filter, uint flags, out uint count, out IntPtr credentials);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CredFree(IntPtr credential);

        public static bool MatchesTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            return target.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0
                || target.IndexOf("jetski", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<KeyringRecord> List()
        {
            return List(true);
        }

        public static List<KeyringRecord> ListManaged()
        {
            return List(true);
        }

        private static List<KeyringRecord> List(bool includeGeminiCli)
        {
            List<KeyringRecord> records = new List<KeyringRecord>();
            uint count = 0;
            IntPtr pointer = IntPtr.Zero;
            if (!CredEnumerate(null, 0, out count, out pointer)) return records;
            try
            {
                int size = IntPtr.Size;
                for (uint i = 0; i < count; i++)
                {
                    IntPtr itemPointer = Marshal.ReadIntPtr(pointer, (int)i * size);
                    Credential credential = (Credential)Marshal.PtrToStructure(itemPointer, typeof(Credential));
                    if (credential.Type != CredTypeGeneric) continue;
                    string target = Marshal.PtrToStringUni(credential.TargetName);
                    if (!MatchesTarget(target)) continue;
                    string username = credential.UserName == IntPtr.Zero
                        ? ""
                        : Marshal.PtrToStringUni(credential.UserName);
                    int blobSize = (int)credential.CredentialBlobSize;
                    string blob = "";
                    if (blobSize > 0 && credential.CredentialBlob != IntPtr.Zero)
                    {
                        byte[] bytes = new byte[blobSize];
                        Marshal.Copy(credential.CredentialBlob, bytes, 0, blobSize);
                        blob = Encoding.UTF8.GetString(bytes);
                    }
                    records.Add(new KeyringRecord
                    {
                        Target = target,
                        Username = username,
                        Persist = (int)credential.Persist,
                        Blob = blob
                    });
                }
            }
            finally
            {
                CredFree(pointer);
            }
            return records;
        }

        public static void DeleteAll()
        {
            foreach (KeyringRecord record in ListManaged())
            {
                CredDelete(record.Target, CredTypeGeneric, 0);
            }
        }

        public static void Write(KeyringRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.Target)) return;
            IntPtr targetPointer = Marshal.StringToHGlobalUni(record.Target);
            IntPtr userPointer = Marshal.StringToHGlobalUni(record.Username ?? "antigravity");
            byte[] bytes = Encoding.UTF8.GetBytes(record.Blob ?? "");
            IntPtr blobPointer = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
            try
            {
                if (bytes.Length > 0) Marshal.Copy(bytes, 0, blobPointer, bytes.Length);
                Credential credential = new Credential();
                credential.Type = CredTypeGeneric;
                credential.TargetName = targetPointer;
                credential.UserName = userPointer;
                credential.CredentialBlob = blobPointer;
                credential.CredentialBlobSize = (uint)bytes.Length;
                credential.Persist = (uint)(record.Persist == 1 || record.Persist == 3 ? record.Persist : 2);
                if (!CredWrite(ref credential, 0))
                {
                    throw new InvalidOperationException(
                        "写入 Windows 凭据管理器失败（错误码 " + Marshal.GetLastWin32Error() + "）");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(targetPointer);
                Marshal.FreeHGlobal(userPointer);
                Marshal.FreeHGlobal(blobPointer);
            }
        }

        public static void DeleteTarget(string target)
        {
            if (string.IsNullOrEmpty(target) || !MatchesTarget(target)) return;
            CredDelete(target, CredTypeGeneric, 0);
        }

        public static string FirstTokenBlob()
        {
            return FirstTokenBlob(true);
        }

        public static string FirstManagedTokenBlob()
        {
            return FirstTokenBlob(false);
        }

        private static string FirstTokenBlob(bool includeGeminiCli)
        {
            foreach (KeyringRecord record in List(includeGeminiCli))
            {
                string blob = record.Blob;
                if (string.IsNullOrEmpty(blob)) continue;
                if (blob.StartsWith("go-keyring-base64:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        blob = Encoding.UTF8.GetString(
                            Convert.FromBase64String(blob.Substring("go-keyring-base64:".Length)));
                    }
                    catch
                    {
                        continue;
                    }
                }
                object parsed = JsonUtil.Deserialize(blob);
                object payload = JsonUtil.GetPath(parsed, "token") ?? parsed;
                Dictionary<string, object> obj = JsonUtil.AsObject(payload);
                if (obj != null &&
                    (JsonUtil.GetString(obj, "access_token") != null ||
                     JsonUtil.GetString(obj, "refresh_token") != null))
                {
                    return blob;
                }
            }
            return null;
        }
    }
}
