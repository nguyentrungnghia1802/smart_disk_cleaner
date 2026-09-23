using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Infrastructure;

public sealed partial class WindowsAllocatedSizeProvider : IAllocatedSizeProvider
{
    public MetadataResult<long> GetAllocatedSize(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return MetadataResult<long>.Unsupported("WINDOWS_ONLY");
        try
        {
            Marshal.SetLastPInvokeError(0);
            var low = GetCompressedFileSize(filePath, out var high);
            var error = Marshal.GetLastPInvokeError();
            if (low == uint.MaxValue && error != 0)
            {
                return MetadataResult<long>.Failed(new Win32Exception(error).NativeErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var value = ((ulong)high << 32) | low;
            return value <= long.MaxValue
                ? MetadataResult<long>.Known((long)value)
                : MetadataResult<long>.Failed("ALLOCATED_SIZE_OVERFLOW");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MetadataResult<long>.Failed(exception.GetType().Name);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);
}

public sealed partial class WindowsPhysicalFileIdentityProvider : IPhysicalFileIdentityProvider
{
    private const uint FileReadAttributes = 0x0080;
    private const uint ShareReadWriteDelete = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    public MetadataResult<PhysicalIdentityMetadata> GetIdentity(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return MetadataResult<PhysicalIdentityMetadata>.Unsupported("WINDOWS_ONLY");
        try
        {
            using var handle = CreateFile(filePath, FileReadAttributes, ShareReadWriteDelete, IntPtr.Zero,
                OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return MetadataResult<PhysicalIdentityMetadata>.Failed(Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!GetFileInformationByHandle(handle, out var information))
            {
                return MetadataResult<PhysicalIdentityMetadata>.Failed(Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            var identity = new PhysicalFileIdentity(information.VolumeSerialNumber.ToString("X8"), fileIndex.ToString("X16"));
            return MetadataResult<PhysicalIdentityMetadata>.Known(new PhysicalIdentityMetadata(identity, information.NumberOfLinks));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MetadataResult<PhysicalIdentityMetadata>.Failed(exception.GetType().Name);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}
