using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using DiagKit.Uds.Services;

namespace DiagKit.Uds.Tests;

[TestClass]
public class CanoeSeedKeyGeneratorLocalDllTests
{
    private const string EnabledParameter = "SeedKeyDllTestsEnabled";
    private const string ResourceDirectoryParameter = "SeedKeyDllResourceDirectory";
    private const string MappingFileParameter = "SeedKeyDllMappingFile";
    private const uint SecurityLevel = 0x01;

    private static readonly byte[] Seed = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

    private static readonly LocalSeedKeyDll[] RequiredDlls =
    [
        new("SeedKeyDllx86TestFile1", Architecture.X86),
        new("SeedKeyDllx64TestFile1", Architecture.X64),
        new("SeedKeyDllx64TestFile2", Architecture.X64),
    ];

    [TestMethod]
    [TestCategory("LocalSeedKeyDll")]
    public void LocalSeedKeyDlls_AllLogicalFilesAreMapped()
    {
        var settings = LoadSettingsOrInconclusive();

        foreach (var dll in RequiredDlls)
        {
            var path = settings.ResolveRequiredPath(dll.LogicalName);
            if (!File.Exists(path))
                Assert.Fail($"Local SeedKey DLL mapping for {dll.LogicalName} does not point to an existing file.");

            var architecture = TryReadPeArchitecture(path);
            if (architecture.HasValue && architecture.Value != dll.Architecture)
                Assert.Fail($"Local SeedKey DLL mapping for {dll.LogicalName} has architecture {architecture}, expected {dll.Architecture}.");
        }
    }

    [TestMethod]
    [TestCategory("LocalSeedKeyDll")]
    public void GenerateKey_LocalSeedKeyDlls_ReturnsStableNonEmptyKeys()
    {
        var settings = LoadSettingsOrInconclusive();
        var dlls = GetCurrentProcessDlls(settings);
        if (dlls.Count == 0)
            Assert.Inconclusive($"No local SeedKey DLL mapping is configured for {RuntimeInformation.ProcessArchitecture}.");

        foreach (var dll in dlls)
        {
            var firstKey = GenerateKey(settings, dll.LogicalName);
            var secondKey = GenerateKey(settings, dll.LogicalName);

            Assert.IsGreaterThan(0, firstKey.Length, $"{dll.LogicalName} returned an empty key.");
            Assert.IsLessThanOrEqualTo(Seed.Length, firstKey.Length, $"{dll.LogicalName} returned a key longer than the configured buffer.");
            CollectionAssert.AreEqual(firstKey, secondKey, $"{dll.LogicalName} returned unstable keys for the same seed and level.");
        }
    }

    [TestMethod]
    [TestCategory("LocalSeedKeyDll")]
    public void GenerateKey_ConsecutiveLocalSeedKeyDllLoads_ReturnNonEmptyKeys()
    {
        var settings = LoadSettingsOrInconclusive();
        var dlls = GetCurrentProcessDlls(settings);
        if (dlls.Count < 2)
            Assert.Inconclusive($"At least two local SeedKey DLL mappings for {RuntimeInformation.ProcessArchitecture} are required.");

        var firstKey = GenerateKey(settings, dlls[0].LogicalName);
        var secondKey = GenerateKey(settings, dlls[1].LogicalName);

        Assert.IsGreaterThan(0, firstKey.Length, $"{dlls[0].LogicalName} returned an empty key.");
        Assert.IsGreaterThan(0, secondKey.Length, $"{dlls[1].LogicalName} returned an empty key.");
    }

    private static byte[] GenerateKey(LocalSeedKeyDllSettings settings, string logicalName)
    {
        try
        {
            using var generator = new CanoeSeedKeyGenerator(settings.ResolveRequiredPath(logicalName));
            return generator.GenerateKey(Seed, SecurityLevel, new CanoeSeedKeyOptions
            {
                KeyBufferSize = Seed.Length,
            });
        }
        catch (Exception ex) when (ex is not AssertFailedException and not AssertInconclusiveException)
        {
            Assert.Fail($"Local SeedKey DLL {logicalName} failed to generate a key. Exception type: {ex.GetType().Name}.");
            throw;
        }
    }

    private static List<LocalSeedKeyDll> GetCurrentProcessDlls(LocalSeedKeyDllSettings settings)
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        return RequiredDlls
            .Where(dll => dll.Architecture == architecture && settings.Contains(dll.LogicalName))
            .ToList();
    }

    private LocalSeedKeyDllSettings LoadSettingsOrInconclusive()
    {
        if (!IsEnabled())
            Assert.Inconclusive("Local SeedKey DLL tests are disabled. Enable them explicitly with runsettings.");

        var resourceDirectory = ResolveRepositoryRelativePath(
            GetParameter(ResourceDirectoryParameter) ?? @"..\resources\SeedKeyDlls");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mappingFile = GetParameter(MappingFileParameter);
        var resolvedMappingFile = string.IsNullOrWhiteSpace(mappingFile)
            ? Path.Combine(resourceDirectory, "SeedKeyDllTestFiles.local.map")
            : ResolveRepositoryRelativePath(mappingFile);

        try
        {
            if (!File.Exists(resolvedMappingFile))
                Assert.Fail("Local SeedKey DLL mapping file is required when local SeedKey DLL tests are enabled.");

            LoadMappingFile(resolvedMappingFile, values);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Assert.Fail($"Local SeedKey DLL configuration could not be loaded. Exception type: {ex.GetType().Name}.");
        }

        foreach (var dll in RequiredDlls)
        {
            var value = GetParameter(dll.LogicalName);
            if (!string.IsNullOrWhiteSpace(value))
                values[dll.LogicalName] = ResolvePath(value, resourceDirectory);
        }

        return new LocalSeedKeyDllSettings(values);
    }

    private bool IsEnabled()
        => string.Equals(GetParameter(EnabledParameter), "true", StringComparison.OrdinalIgnoreCase);

    private string? GetParameter(string name)
        => TestContext.Properties.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static void LoadMappingFile(string mappingFile, Dictionary<string, string> values)
    {
        var mappingDirectory = Path.GetDirectoryName(mappingFile) ?? Directory.GetCurrentDirectory();
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(mappingFile))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                throw new FormatException($"Invalid local SeedKey DLL mapping line {lineNumber}.");

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                throw new FormatException($"Invalid local SeedKey DLL mapping line {lineNumber}.");

            values[key] = ResolvePath(value, mappingDirectory);
        }
    }

    private static Architecture? TryReadPeArchitecture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 0x40)
                return null;

            stream.Position = 0x3C;
            var peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset <= 0 || peHeaderOffset + 6 > stream.Length)
                return null;

            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
                return null;

            return reader.ReadUInt16() switch
            {
                0x014C => Architecture.X86,
                0x8664 => Architecture.X64,
                0xAA64 => Architecture.Arm64,
                _ => null,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveRepositoryRelativePath(string path)
        => ResolvePath(path, FindRepositoryRoot() ?? Directory.GetCurrentDirectory());

    private static string ResolvePath(string path, string baseDirectory)
    {
        var trimmed = path.Trim().Trim('"');
        return Path.IsPathFullyQualified(trimmed)
            ? trimmed
            : Path.GetFullPath(trimmed, baseDirectory);
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DiagKit.Uds.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private readonly record struct LocalSeedKeyDll(string LogicalName, Architecture Architecture);

    private sealed class LocalSeedKeyDllSettings(IReadOnlyDictionary<string, string> values)
    {
        public bool Contains(string logicalName) => values.ContainsKey(logicalName);

        public string ResolveRequiredPath(string logicalName)
        {
            if (!values.TryGetValue(logicalName, out var value))
                Assert.Fail($"Local SeedKey DLL mapping is missing {logicalName}.");

            return value;
        }
    }

    public TestContext TestContext { get; set; }
}
