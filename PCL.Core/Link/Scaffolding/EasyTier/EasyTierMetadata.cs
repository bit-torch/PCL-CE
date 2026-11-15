using System;
using System.IO;
using System.Runtime.InteropServices;
using PCL.Core.App;

namespace PCL.Core.Link.Scaffolding.EasyTier;

public static class EasyTierMetadata
{
    public const string CurrentEasyTierVer = "2.6.4";

    public static string? DirectoryOverride { get; set; }
    public static string ExecutableName(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
    public static string ExecutablePath(string name) => Path.Combine(EasyTierFilePath, ExecutableName(name));
    public static string PlatformName => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    public static string ArchitectureName => RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? (OperatingSystem.IsWindows() ? "arm64" : "aarch64") : "x86_64";

    public static string EasyTierFilePath => DirectoryOverride ?? Path.Combine(Paths.SharedLocalData, "EasyTier",
        CurrentEasyTierVer,
        $"easytier-{PlatformName}-{ArchitectureName}");
}