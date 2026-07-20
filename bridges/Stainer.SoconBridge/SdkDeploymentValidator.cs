using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Stainer.SoconBridge
{
    internal interface ISdkDeploymentValidator
    {
        SdkDeploymentValidationResult Validate();
    }

    internal interface IProcessArchitectureProbe
    {
        bool IsX86Process();
    }

    internal interface IManagedAssemblyLoadProbe
    {
        ManagedAssemblyLoadResult Load(string filePath, IEnumerable<ManagedTypeRequirement> typeRequirements);
    }

    internal sealed class DefaultProcessArchitectureProbe : IProcessArchitectureProbe
    {
        public bool IsX86Process()
        {
            return !Environment.Is64BitProcess;
        }
    }

    internal sealed class ReflectionOnlyManagedAssemblyLoadProbe : IManagedAssemblyLoadProbe
    {
        public ManagedAssemblyLoadResult Load(string filePath, IEnumerable<ManagedTypeRequirement> typeRequirements)
        {
            try
            {
                var assembly = Assembly.ReflectionOnlyLoadFrom(filePath);
                var assemblyName = assembly.GetName();
                var typeChecks = new List<ManagedTypeCheckResult>();

                if (typeRequirements != null)
                {
                    foreach (var requirement in typeRequirements)
                    {
                        var type = assembly.GetType(requirement.FullName, false, false);
                        typeChecks.Add(new ManagedTypeCheckResult(
                            requirement.DisplayName,
                            requirement.FullName,
                            type != null));
                    }
                }

                return ManagedAssemblyLoadResult.Succeeded(
                    Path.GetFileName(filePath),
                    assemblyName.FullName,
                    assemblyName.Version == null ? null : assemblyName.Version.ToString(),
                    typeChecks);
            }
            catch (Exception ex)
            {
                return ManagedAssemblyLoadResult.Failed(Path.GetFileName(filePath), ex);
            }
        }
    }

    internal sealed class ManagedTypeRequirement
    {
        public ManagedTypeRequirement(string displayName, string fullName)
        {
            DisplayName = displayName;
            FullName = fullName;
        }

        public string DisplayName { get; private set; }

        public string FullName { get; private set; }
    }

    internal sealed class ManagedTypeCheckResult
    {
        public ManagedTypeCheckResult(string displayName, string fullName, bool available)
        {
            DisplayName = displayName;
            FullName = fullName;
            Available = available;
        }

        public string DisplayName { get; private set; }

        public string FullName { get; private set; }

        public bool Available { get; private set; }
    }

    internal sealed class ManagedAssemblyLoadResult
    {
        private ManagedAssemblyLoadResult(
            string fileName,
            bool success,
            string assemblyName,
            string assemblyVersion,
            List<ManagedTypeCheckResult> typeChecks,
            string exceptionType,
            string exceptionMessage)
        {
            FileName = fileName;
            Success = success;
            AssemblyName = assemblyName;
            AssemblyVersion = assemblyVersion;
            TypeChecks = typeChecks ?? new List<ManagedTypeCheckResult>();
            ExceptionType = exceptionType;
            ExceptionMessage = exceptionMessage;
        }

        public string FileName { get; private set; }

        public bool Success { get; private set; }

        public string AssemblyName { get; private set; }

        public string AssemblyVersion { get; private set; }

        public List<ManagedTypeCheckResult> TypeChecks { get; private set; }

        public string ExceptionType { get; private set; }

        public string ExceptionMessage { get; private set; }

        public static ManagedAssemblyLoadResult Succeeded(
            string fileName,
            string assemblyName,
            string assemblyVersion,
            List<ManagedTypeCheckResult> typeChecks)
        {
            return new ManagedAssemblyLoadResult(
                fileName,
                true,
                assemblyName,
                assemblyVersion,
                typeChecks,
                null,
                null);
        }

        public static ManagedAssemblyLoadResult Failed(string fileName, Exception exception)
        {
            return Failed(fileName, exception, null);
        }

        public static ManagedAssemblyLoadResult Failed(string fileName, Exception exception, IEnumerable<string> knownPaths)
        {
            return new ManagedAssemblyLoadResult(
                fileName,
                false,
                null,
                null,
                null,
                exception == null ? null : exception.GetType().Name,
                exception == null ? null : PathScrubber.Scrub(exception.Message, knownPaths));
        }
    }

    internal sealed class SdkRuntimeValidationDetails
    {
        public SdkRuntimeValidationDetails()
        {
            FileChecks = new List<string>();
            ManagedAssemblyLoads = new List<ManagedAssemblyLoadResult>();
            TypeChecks = new List<ManagedTypeCheckResult>();
            ExceptionDetails = new List<string>();
        }

        public string ProcessArchitecture { get; set; }

        public string SdkDirectory { get; set; }

        public string SdkPathSource { get; set; }

        public bool? SdkPathConfigured { get; set; }

        public bool? SdkDirectoryExists { get; set; }

        public bool? CoreFilesPresent { get; set; }

        public bool? ManagedAssemblyLoadSucceeded { get; set; }

        public bool? NativeDllCheckSucceeded { get; set; }

        public string CanBootloaderMachine { get; set; }

        public string CanBootloaderFileVersion { get; set; }

        public bool? RequiredTypesAvailable { get; set; }

        public List<string> FileChecks { get; private set; }

        public List<ManagedAssemblyLoadResult> ManagedAssemblyLoads { get; private set; }

        public List<ManagedTypeCheckResult> TypeChecks { get; private set; }

        public List<string> ExceptionDetails { get; private set; }
    }

    internal sealed class SdkDeploymentValidatorOptions
    {
        public SdkDeploymentValidatorOptions(string baseDirectory)
        {
            BaseDirectory = baseDirectory;
            EnvironmentSdkDirectoryProvider = DefaultEnvironmentSdkDirectoryProvider;
        }

        public string BaseDirectory { get; private set; }

        public Func<string> EnvironmentSdkDirectoryProvider { get; set; }

        private static string DefaultEnvironmentSdkDirectoryProvider()
        {
            return Environment.GetEnvironmentVariable("STAINER_SOCON_SDK_DIR");
        }
    }

    internal sealed class SdkDeploymentValidationResult
    {
        public SdkDeploymentValidationResult(BridgeStatus status, BridgeResponseDetails details, List<string> warnings)
            : this(status, details, warnings, new SdkRuntimeValidationDetails())
        {
        }

        public SdkDeploymentValidationResult(
            BridgeStatus status,
            BridgeResponseDetails details,
            List<string> warnings,
            SdkRuntimeValidationDetails runtimeDetails)
        {
            Status = status;
            Details = details ?? new BridgeResponseDetails();
            Warnings = warnings ?? new List<string>();
            RuntimeDetails = runtimeDetails ?? new SdkRuntimeValidationDetails();
        }

        public BridgeStatus Status { get; private set; }

        public BridgeResponseDetails Details { get; private set; }

        public List<string> Warnings { get; private set; }

        public SdkRuntimeValidationDetails RuntimeDetails { get; private set; }

        public bool Success
        {
            get { return Status == BridgeStatus.DeploymentValidated; }
        }

        public bool DiagnosticSuccess
        {
            get { return Success && RuntimeDetails.RequiredTypesAvailable == true; }
        }
    }

    internal sealed class SdkDeploymentValidator : ISdkDeploymentValidator
    {
        private static readonly string[] CoreFiles =
        {
            "SOCON.API.dll",
            "SOCON.Utility.dll",
            "can_bootloader.dll"
        };

        private static readonly string[] RuntimeDependencyFiles =
        {
            "SOCON.ScEventBus.dll",
            "C1.C1Zip.4.dll"
        };

        private static readonly ManagedTypeRequirement[] SoconApiRequiredTypes =
        {
            new ManagedTypeRequirement("SCDevice", "SOCON.API.SCDevice"),
            new ManagedTypeRequirement("SCDeviceMA", "SOCON.API.SCDeviceMA"),
            new ManagedTypeRequirement("ConnectType", "SOCON.API.Utility+e_ConnectType"),
            new ManagedTypeRequirement("DeviceTypeEnum", "SOCON.API.Utility+DeviceTypeEnum"),
            new ManagedTypeRequirement("ProtocolTypeEnum", "SOCON.API.Utility+ProtocolTypeEnum")
        };

        private readonly SdkDeploymentValidatorOptions options;
        private readonly IProcessArchitectureProbe architectureProbe;
        private readonly IPeArchitectureInspector peInspector;
        private readonly IManagedAssemblyLoadProbe managedAssemblyLoadProbe;

        public SdkDeploymentValidator(
            SdkDeploymentValidatorOptions options,
            IProcessArchitectureProbe architectureProbe,
            IPeArchitectureInspector peInspector,
            IManagedAssemblyLoadProbe managedAssemblyLoadProbe)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (architectureProbe == null)
            {
                throw new ArgumentNullException("architectureProbe");
            }

            if (peInspector == null)
            {
                throw new ArgumentNullException("peInspector");
            }

            if (managedAssemblyLoadProbe == null)
            {
                throw new ArgumentNullException("managedAssemblyLoadProbe");
            }

            this.options = options;
            this.architectureProbe = architectureProbe;
            this.peInspector = peInspector;
            this.managedAssemblyLoadProbe = managedAssemblyLoadProbe;
        }

        public SdkDeploymentValidationResult Validate()
        {
            var details = new BridgeResponseDetails();
            var runtimeDetails = new SdkRuntimeValidationDetails();
            var warnings = new List<string>();

            var isX86Process = architectureProbe.IsX86Process();
            details.IsX86Process = isX86Process;
            runtimeDetails.ProcessArchitecture = isX86Process ? "x86" : "not-x86";
            if (!isX86Process)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.ArchitectureInvalid, details, warnings, runtimeDetails);
            }

            var sdkResolution = ResolveSdkDirectory();
            var sdkDirectory = sdkResolution.Directory;
            details.SdkPathConfigured = !string.IsNullOrWhiteSpace(sdkDirectory);
            details.SdkDirectoryExists = details.SdkPathConfigured == true && Directory.Exists(sdkDirectory);
            runtimeDetails.SdkDirectory = string.IsNullOrWhiteSpace(sdkDirectory) ? null : "(configured)";
            runtimeDetails.SdkPathSource = sdkResolution.Source;
            runtimeDetails.SdkPathConfigured = details.SdkPathConfigured;
            runtimeDetails.SdkDirectoryExists = details.SdkDirectoryExists;

            if (details.SdkPathConfigured != true || details.SdkDirectoryExists != true)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.SdkPathMissing, details, warnings, runtimeDetails);
            }

            var missingCoreFiles = FindMissingFiles(sdkDirectory, CoreFiles);
            details.CoreFilesPresent = missingCoreFiles.Count == 0;
            runtimeDetails.CoreFilesPresent = details.CoreFilesPresent;
            AddFileChecks(runtimeDetails, sdkDirectory, CoreFiles);
            if (missingCoreFiles.Count > 0)
            {
                details.MissingFiles = missingCoreFiles;
                return new SdkDeploymentValidationResult(BridgeStatus.SdkFilesMissing, details, warnings, runtimeDetails);
            }

            var managedLoadResults = LoadManagedAssemblies(sdkDirectory, runtimeDetails);
            if (!managedLoadResults)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.SdkFilesMissing, details, warnings, runtimeDetails);
            }

            var canBootloader = Path.Combine(sdkDirectory, "can_bootloader.dll");
            var peResult = peInspector.Inspect(canBootloader);
            details.CanBootloaderMachine = peResult.MachineHex;
            details.CanBootloaderIsX86 = peResult.IsX86Native;
            runtimeDetails.CanBootloaderMachine = peResult.MachineHex;
            runtimeDetails.NativeDllCheckSucceeded = peResult.IsValidPe && peResult.IsX86Native;
            runtimeDetails.CanBootloaderFileVersion = ReadFileVersion(canBootloader, runtimeDetails);
            if (!peResult.IsValidPe || !peResult.IsX86Native)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.SdkFilesMissing, details, warnings, runtimeDetails);
            }

            var missingRuntimeDependencies = FindMissingFiles(sdkDirectory, RuntimeDependencyFiles);
            details.RuntimeDependenciesPresent = missingRuntimeDependencies.Count == 0;
            if (missingRuntimeDependencies.Count > 0)
            {
                details.MissingFiles = missingRuntimeDependencies;
                warnings.Add(BridgeWarningCodes.SdkRuntimeDependenciesWarning);
                details.WarningCodes = new List<string>(warnings);
                details.SdkVersionStatus = "NotChecked";
                return new SdkDeploymentValidationResult(BridgeStatus.DeploymentValidated, details, warnings, runtimeDetails);
            }

            // Vendor components carry independent FileVersion values (for
            // example, SOCON.API and SOCON.Utility do not share a release
            // number). No vendor package manifest has been supplied that
            // could safely establish cross-DLL version compatibility, so do
            // not reject a complete, loadable deployment on that basis.
            details.SdkVersionStatus = "NotChecked";
            return new SdkDeploymentValidationResult(BridgeStatus.DeploymentValidated, details, warnings, runtimeDetails);
        }

        private SdkDirectoryResolution ResolveSdkDirectory()
        {
            var localConfigPath = Path.Combine(options.BaseDirectory ?? string.Empty, "SoconBridge.config.local.json");
            var localConfigValue = ReadLocalSdkDirectory(localConfigPath);
            if (!string.IsNullOrWhiteSpace(localConfigValue))
            {
                return new SdkDirectoryResolution(
                    localConfigValue.Trim(),
                    "SoconBridge.config.local.json");
            }

            var envValue = options.EnvironmentSdkDirectoryProvider == null ? null : options.EnvironmentSdkDirectoryProvider();
            return string.IsNullOrWhiteSpace(envValue)
                ? new SdkDirectoryResolution(null, "not configured")
                : new SdkDirectoryResolution(envValue.Trim(), "STAINER_SOCON_SDK_DIR");
        }

        private static string ReadLocalSdkDirectory(string localConfigPath)
        {
            if (string.IsNullOrWhiteSpace(localConfigPath) || !File.Exists(localConfigPath))
            {
                return null;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(BridgeConfig));
                using (var stream = new FileStream(localConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var config = serializer.ReadObject(stream) as BridgeConfig;
                    return config == null ? null : config.SdkDirectory;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<string> FindMissingFiles(string sdkDirectory, IEnumerable<string> fileNames)
        {
            var missing = new List<string>();
            foreach (var fileName in fileNames)
            {
                if (!File.Exists(Path.Combine(sdkDirectory, fileName)))
                {
                    missing.Add(fileName);
                }
            }

            return missing;
        }

        private static void AddFileChecks(SdkRuntimeValidationDetails runtimeDetails, string sdkDirectory, IEnumerable<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                var state = File.Exists(Path.Combine(sdkDirectory, fileName)) ? "present" : "missing";
                runtimeDetails.FileChecks.Add(fileName + "=" + state);
            }
        }

        private bool LoadManagedAssemblies(string sdkDirectory, SdkRuntimeValidationDetails runtimeDetails)
        {
            var allSucceeded = true;
            var managedFiles = new[]
            {
                "SOCON.API.dll",
                "SOCON.Utility.dll"
            };

            foreach (var managedFile in managedFiles)
            {
                var typeRequirements = GetTypeRequirements(managedFile);
                var result = managedAssemblyLoadProbe.Load(Path.Combine(sdkDirectory, managedFile), typeRequirements);
                runtimeDetails.ManagedAssemblyLoads.Add(result);
                runtimeDetails.TypeChecks.AddRange(result.TypeChecks);
                if (!result.Success)
                {
                    allSucceeded = false;
                    runtimeDetails.ExceptionDetails.Add(
                        result.FileName + ": " + result.ExceptionType + ": " + PathScrubber.Scrub(result.ExceptionMessage, sdkDirectory));
                }
            }

            runtimeDetails.ManagedAssemblyLoadSucceeded = allSucceeded;
            runtimeDetails.RequiredTypesAvailable =
                runtimeDetails.TypeChecks.Count == SoconApiRequiredTypes.Length
                && runtimeDetails.TypeChecks.TrueForAll(delegate(ManagedTypeCheckResult check) { return check.Available; });
            return allSucceeded;
        }

        private static ManagedTypeRequirement[] GetTypeRequirements(string managedFile)
        {
            if (string.Equals(managedFile, "SOCON.API.dll", StringComparison.OrdinalIgnoreCase))
            {
                return SoconApiRequiredTypes;
            }

            return new ManagedTypeRequirement[0];
        }

        private static string ReadFileVersion(string filePath, SdkRuntimeValidationDetails runtimeDetails)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            }
            catch (Exception ex)
            {
                runtimeDetails.ExceptionDetails.Add(
                    Path.GetFileName(filePath) + " file version: " + ex.GetType().Name + ": " + PathScrubber.Scrub(ex.Message, filePath));
                return null;
            }
        }

        private sealed class SdkDirectoryResolution
        {
            public SdkDirectoryResolution(string directory, string source)
            {
                Directory = directory;
                Source = source;
            }

            public string Directory { get; private set; }

            public string Source { get; private set; }
        }
    }

    internal static class PathScrubber
    {
        private const string Placeholder = "<path>";
        private static readonly Regex AbsolutePathPattern = new Regex(
            @"[A-Za-z]:[\\/][^\s""'<>|*?]+",
            RegexOptions.Compiled);

        private static readonly Regex UncPathPattern = new Regex(
            @"\\\\[^\s""'<>|*?]+",
            RegexOptions.Compiled);

        public static string Scrub(string value, params string[] knownPaths)
        {
            return Scrub(value, (IEnumerable<string>)knownPaths);
        }

        public static string Scrub(string value, IEnumerable<string> knownPaths)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var scrubbed = value;
            if (knownPaths != null)
            {
                foreach (var path in knownPaths)
                {
                    if (!string.IsNullOrEmpty(path) && scrubbed.IndexOf(path, StringComparison.Ordinal) >= 0)
                    {
                        scrubbed = scrubbed.Replace(path, Placeholder);
                    }
                }
            }

            scrubbed = AbsolutePathPattern.Replace(scrubbed, Placeholder);
            scrubbed = UncPathPattern.Replace(scrubbed, Placeholder);
            return scrubbed;
        }
    }
}
