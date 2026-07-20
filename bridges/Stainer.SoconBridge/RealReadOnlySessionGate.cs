namespace Stainer.SoconBridge
{
    /// <summary>
    /// Dual gate for real read-only sessions. A real session is allowed ONLY
    /// when BOTH hold:
    /// 1. The local config sets <c>realReadOnlyEnabled</c> = true.
    /// 2. The bridge process is launched with <c>--enable-real-read-only</c>.
    /// When <see cref="IsEnabled"/> is false, every session command returns
    /// <c>RealReadOnlyNotEnabled</c> and the real adapter is NEVER constructed
    /// nor called. This is the single fail-closed chokepoint for real hardware
    /// access.
    /// </summary>
    internal sealed class RealReadOnlySessionGate
    {
        private readonly SoconReadOnlyConfig config;
        private readonly bool enableRealReadOnlyArg;

        public RealReadOnlySessionGate(SoconReadOnlyConfig config, bool enableRealReadOnlyArg)
        {
            this.config = config ?? SoconReadOnlyConfig.Default();
            this.enableRealReadOnlyArg = enableRealReadOnlyArg;
        }

        /// <summary>
        /// True only when BOTH conditions hold.
        /// </summary>
        public bool IsEnabled
        {
            get { return config.RealReadOnlyEnabled && enableRealReadOnlyArg; }
        }
    }
}
