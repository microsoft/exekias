using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Xunit;

namespace Exekias.AzureStorageEmulator.Tests
{
    public sealed class StorageEmulatorFixture : IDisposable
    {
        public const string ConnectionString = "UseDevelopmentStorage=true";
        enum EmulatorKind { None, StorageEmulator, Azurite }
        readonly EmulatorKind emulatorKind;

        readonly string storageEmulatorPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft SDKs\Azure\Storage Emulator\AzureStorageEmulator.exe");
        // https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azurite?tabs=visual-studio#running-azurite-from-the-command-line
        readonly string azuritePath = Path.Combine(
            Environment.GetEnvironmentVariable("VSINSTALLDIR") ?? @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise",
            @"Common7\IDE\Extensions\Microsoft\Azure Storage Emulator\azurite.exe");
        Process azuriteProcess;
        public StorageEmulatorFixture()
        {
            emulatorKind = EmulatorKind.None;
            if (StorageEmulatorActive())
            {
                return;
            }
            // Try Storage Emulator first
            // https://docs.microsoft.com/en-us/azure/storage/common/storage-use-emulator#start-and-initialize-the-storage-emulator
            if (File.Exists(storageEmulatorPath))
            {
                var start = Process.Start(storageEmulatorPath, "start");
                start.WaitForExit();
                if (0 == start.ExitCode)
                {
                    emulatorKind = EmulatorKind.StorageEmulator;
                    return;
                }
                else if (-5 != start.ExitCode)
                {
                    throw new InvalidOperationException($"Cannot start storage emulator, exit code {start.ExitCode}");
                }
            }
            // Try Azurite as a second option
            // https://github.com/Azure/Azurite/blob/master/README.md#npm
            if (File.Exists(azuritePath))
            {
                azuriteProcess = Process.Start(azuritePath);
                emulatorKind = EmulatorKind.Azurite;
                
                bool connected = false;
                DateTime timeout = DateTime.UtcNow + TimeSpan.FromMinutes(1);
                while (DateTime.UtcNow < timeout && !connected)
                {
                    connected = StorageEmulatorActive();
                }
                if (!connected) throw new InvalidOperationException("Cannot connect azurite in one minute.");
            }
            else
            {
                throw new InvalidOperationException("Cannot start storage emulator");
            }

            static bool StorageEmulatorActive()
            {
                try
                {
                    using Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    socket.Connect(System.Net.IPAddress.Loopback, 10000); // Azurite Blob endpoint
                    return true;
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.ConnectionRefused) throw;
                }

                return false;
            }
        }
        bool disposed = false;
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                if (EmulatorKind.StorageEmulator == emulatorKind)
                {
                    var stop = Process.Start(storageEmulatorPath, "stop");
                    stop.WaitForExit();
                }
                else if (EmulatorKind.Azurite == emulatorKind)
                {
                    try
                    {
                        azuriteProcess.Kill(true);
                        azuriteProcess.WaitForExit();
                    }
                    finally
                    {
                        azuriteProcess.Dispose();
                    }
                }
            }
        }
        ~StorageEmulatorFixture() { Dispose(); }
    }
    [CollectionDefinition("AzureStorageEmulator")]
    public class AzureStorageEmulatorCollectionDefinition : ICollectionFixture<StorageEmulatorFixture> { }
}
