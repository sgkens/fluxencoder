using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// Windows Credential Manager (generic credentials, persisted per user), through the Win32 Cred* functions. Source-
/// generated P/Invoke, so it works under Native AOT.
/// </summary>
/// <remarks>
/// Not yet verified on Windows: this repository's test runs are Linux-only until CI covers Windows (CI-E014).
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialStore : ISecretStore
{
    private const string TargetPrefix = "FluxEncoder:";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Name => "Windows Credential Manager";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CredRead(TargetPrefix + key, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new InvalidOperationException($"{Name} could not read the secret (error {error}).");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return Task.FromResult<string?>(null);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPointer = Marshal.AllocHGlobal(blob.Length);
        var target = Marshal.StringToHGlobalUni(TargetPrefix + key);
        var user = Marshal.StringToHGlobalUni(key);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"{Name} refused to store the secret (error {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(CredDelete(TargetPrefix + key, CredTypeGeneric, 0));

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, int type, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);
}
