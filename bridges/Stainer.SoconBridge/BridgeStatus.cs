namespace Stainer.SoconBridge
{
    internal enum BridgeStatus
    {
        Starting,
        Offline,
        SdkPathMissing,
        SdkFilesMissing,
        ArchitectureInvalid,
        DeploymentValidated,
        SdkVersionInconsistent,
        RealReadOnlyNotEnabled,
        SessionClosed,
        SessionOpen,
        SessionBlocked
    }

    internal static class BridgeWarningCodes
    {
        public const string SdkRuntimeDependenciesWarning = "SdkRuntimeDependenciesWarning";
    }
}
