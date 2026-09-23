using System.ComponentModel;
using System.Runtime.InteropServices;
using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Infrastructure;

public sealed partial class WindowsVolumeInfoProvider : IVolumeInfoProvider
{
    public unsafe VolumeInfo ValidateAndGet(string requestedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedRoot);
        var normalized = PathNormalizer.Normalize(requestedRoot);
        var pathRoot = Path.GetPathRoot(normalized);
        if (pathRoot is null || !PathNormalizer.EqualsPath(normalized, pathRoot))
            throw new ScanValidationException("ROOT_NOT_DRIVE", "The scan root must be a drive root.");

        var driveLetter = char.ToUpperInvariant(pathRoot[0]);
        if (driveLetter is not ('C' or 'D'))
            throw new ScanValidationException("DRIVE_NOT_SUPPORTED", "V1 supports only C: and D: fixed drives.");

        var drive = new DriveInfo(pathRoot);
        if (!drive.IsReady) throw new ScanValidationException("VOLUME_NOT_READY", "The selected drive is not ready.");
        if (drive.DriveType != DriveType.Fixed) throw new ScanValidationException("VOLUME_NOT_FIXED", "The selected drive is not a fixed drive.");

        var identity = drive.Name.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var fileSystem = drive.DriveFormat;
        if (OperatingSystem.IsWindows())
        {
            Span<char> volumeName = stackalloc char[261];
            Span<char> fileSystemName = stackalloc char[261];
            fixed (char* volumeNamePointer = volumeName)
            fixed (char* fileSystemNamePointer = fileSystemName)
            {
                if (GetVolumeInformation(pathRoot, volumeNamePointer, (uint)volumeName.Length, out var serial, out _, out _,
                        fileSystemNamePointer, (uint)fileSystemName.Length) == 0)
                {
                    throw new ScanValidationException("VOLUME_INFO_FAILED", new Win32Exception(Marshal.GetLastPInvokeError()).Message);
                }

                identity = serial.ToString("X8");
                var terminator = fileSystemName.IndexOf('\0');
                fileSystem = new string(fileSystemName[..(terminator < 0 ? fileSystemName.Length : terminator)]);
            }
        }

        return new VolumeInfo(pathRoot, identity, fileSystem, true);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetVolumeInformation(
        string rootPathName,
        char* volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        char* fileSystemNameBuffer,
        uint fileSystemNameSize);
}

public sealed class DefaultAppPathProvider : IAppPathProvider
{
    public DefaultAppPathProvider(string? appDataRoot = null)
    {
        AppDataRoot = PathNormalizer.Normalize(appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartDiskCleaner"));
    }

    public string AppDataRoot { get; }
}
