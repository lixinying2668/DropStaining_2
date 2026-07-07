namespace Stainer.SoconBridge
{
    internal enum BridgeStatus
    {
        Starting,
        Offline,
        SdkPathMissing,
        SdkFilesMissing,
        ArchitectureInvalid,
        DeploymentValidated
    }

    internal static class BridgeWarningCodes
    {
        public const string SdkRuntimeDependenciesWarning = "SdkRuntimeDependenciesWarning";
    }
}
