using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

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

    internal sealed class DefaultProcessArchitectureProbe : IProcessArchitectureProbe
    {
        public bool IsX86Process()
        {
            return !Environment.Is64BitProcess;
        }
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
        {
            Status = status;
            Details = details ?? new BridgeResponseDetails();
            Warnings = warnings ?? new List<string>();
        }

        public BridgeStatus Status { get; private set; }

        public BridgeResponseDetails Details { get; private set; }

        public List<string> Warnings { get; private set; }

        public bool Success
        {
            get { return Status == BridgeStatus.DeploymentValidated; }
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

        private readonly SdkDeploymentValidatorOptions options;
        private readonly IProcessArchitectureProbe architectureProbe;
        private readonly IPeArchitectureInspector peInspector;

        public SdkDeploymentValidator(
            SdkDeploymentValidatorOptions options,
            IProcessArchitectureProbe architectureProbe,
            IPeArchitectureInspector peInspector)
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

            this.options = options;
            this.architectureProbe = architectureProbe;
            this.peInspector = peInspector;
        }

        public SdkDeploymentValidationResult Validate()
        {
            var details = new BridgeResponseDetails();
            var warnings = new List<string>();

            var isX86Process = architectureProbe.IsX86Process();
            details.IsX86Process = isX86Process;
            if (!isX86Process)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.ArchitectureInvalid, details, warnings);
            }

            var sdkDirectory = ResolveSdkDirectory();
            details.SdkPathConfigured = !string.IsNullOrWhiteSpace(sdkDirectory);
            details.SdkDirectoryExists = details.SdkPathConfigured == true && Directory.Exists(sdkDirectory);

            if (details.SdkPathConfigured != true || details.SdkDirectoryExists != true)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.SdkPathMissing, details, warnings);
            }

            var missingCoreFiles = FindMissingFiles(sdkDirectory, CoreFiles);
            details.CoreFilesPresent = missingCoreFiles.Count == 0;
            if (missingCoreFiles.Count > 0)
            {
                details.MissingFiles = missingCoreFiles;
                return new SdkDeploymentValidationResult(BridgeStatus.SdkFilesMissing, details, warnings);
            }

            var canBootloader = Path.Combine(sdkDirectory, "can_bootloader.dll");
            var peResult = peInspector.Inspect(canBootloader);
            details.CanBootloaderMachine = peResult.MachineHex;
            details.CanBootloaderIsX86 = peResult.IsX86Native;
            if (!peResult.IsValidPe || !peResult.IsX86Native)
            {
                return new SdkDeploymentValidationResult(BridgeStatus.SdkFilesMissing, details, warnings);
            }

            var missingRuntimeDependencies = FindMissingFiles(sdkDirectory, RuntimeDependencyFiles);
            details.RuntimeDependenciesPresent = missingRuntimeDependencies.Count == 0;
            if (missingRuntimeDependencies.Count > 0)
            {
                details.MissingFiles = missingRuntimeDependencies;
                warnings.Add(BridgeWarningCodes.SdkRuntimeDependenciesWarning);
                details.WarningCodes = new List<string>(warnings);
            }

            return new SdkDeploymentValidationResult(BridgeStatus.DeploymentValidated, details, warnings);
        }

        private string ResolveSdkDirectory()
        {
            var localConfigPath = Path.Combine(options.BaseDirectory ?? string.Empty, "SoconBridge.config.local.json");
            var localConfigValue = ReadLocalSdkDirectory(localConfigPath);
            if (!string.IsNullOrWhiteSpace(localConfigValue))
            {
                return localConfigValue.Trim();
            }

            var envValue = options.EnvironmentSdkDirectoryProvider == null ? null : options.EnvironmentSdkDirectoryProvider();
            return string.IsNullOrWhiteSpace(envValue) ? null : envValue.Trim();
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
    }
}
