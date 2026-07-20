using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;

namespace Stainer.SoconBridge
{
    /// <summary>
    /// Defensive loader for the real read-only extension fields of
    /// <see cref="BridgeConfig"/>. Missing file, unreadable JSON, or partial
    /// schema all yield SAFE DEFAULTS: <c>RealReadOnlyEnabled=false</c>, no
    /// axis mappings, calibration all false, no whitelist. This object NEVER
    /// logs or exposes the raw COM/NodeID/path values it parsed; callers reach
    /// them only through the whitelisted, axis-role-scoped helpers below.
    /// </summary>
    internal sealed class SoconReadOnlyConfig
    {
        public const string RequiredConnectionType = "CONN_USB";
        public const string LocalConfigFileName = "SoconBridge.config.local.json";

        private readonly BridgeConfig raw;

        private SoconReadOnlyConfig(BridgeConfig raw)
        {
            this.raw = raw ?? new BridgeConfig();
        }

        /// <summary>
        /// True only when the local config explicitly enables real-read-only.
        /// Combined with the launch flag in <see cref="RealReadOnlySessionGate"/>.
        /// </summary>
        public bool RealReadOnlyEnabled
        {
            get { return raw.RealReadOnlyEnabled; }
        }

        public string SdkDirectory
        {
            get { return raw.SdkDirectory ?? string.Empty; }
        }

        /// <summary>
        /// ConnectionType from config. Empty when not configured.
        /// </summary>
        public string ConnectionType
        {
            get { return raw.Usb2Can == null ? null : (raw.Usb2Can.ConnectionType ?? string.Empty); }
        }

        /// <summary>
        /// True only when ConnectionType == "CONN_USB" (the only allowed value).
        /// </summary>
        public bool IsConnectionUsb2Can
        {
            get { return string.Equals(ConnectionType, RequiredConnectionType, StringComparison.Ordinal); }
        }

        public int PortNumber
        {
            get { return raw.Usb2Can == null ? 0 : raw.Usb2Can.PortNumber; }
        }

        public int BaudRate
        {
            get { return raw.Usb2Can == null ? 0 : raw.Usb2Can.BaudRate; }
        }

        /// <summary>
        /// Whitelist NodeID list. Empty when not configured.
        /// </summary>
        public IReadOnlyList<int> WhitelistNodes
        {
            get { return raw.WhitelistNodes as IReadOnlyList<int> ?? new List<int>(); }
        }

        public bool HasWhitelist
        {
            get { return raw.WhitelistNodes != null && raw.WhitelistNodes.Count > 0; }
        }

        /// <summary>
        /// First whitelist NodeID, or null if the whitelist is empty. Used as
        /// the representative node for the basic-status read.
        /// </summary>
        public int? GetFirstWhitelistNode()
        {
            if (raw.WhitelistNodes == null || raw.WhitelistNodes.Count == 0)
            {
                return null;
            }
            return raw.WhitelistNodes[0];
        }

        /// <summary>
        /// Returns the configured mapping for the given axis role, or null when
        /// no mapping exists. Does NOT enforce whitelist; callers should use
        /// <see cref="IsAxisWhitelisted"/> to authorize before use.
        /// </summary>
        public AxisMapping GetAxisMapping(AxisRole role)
        {
            if (raw.AxisMappings == null)
            {
                return null;
            }

            switch (role)
            {
                case AxisRole.X: return raw.AxisMappings.X;
                case AxisRole.Y: return raw.AxisMappings.Y;
                case AxisRole.Z1: return raw.AxisMappings.Z1;
                case AxisRole.Z2: return raw.AxisMappings.Z2;
                default: return null;
            }
        }

        /// <summary>
        /// True when a mapping exists AND its NodeID is in the whitelist.
        /// </summary>
        public bool IsAxisWhitelisted(AxisRole role)
        {
            var mapping = GetAxisMapping(role);
            if (mapping == null)
            {
                return false;
            }

            return raw.WhitelistNodes != null && raw.WhitelistNodes.Contains(mapping.NodeId);
        }

        /// <summary>
        /// True only when calibration for the given axis role is explicitly
        /// marked completed in config. Calibration is performed by the vendor
        /// tool, never by this bridge.
        /// </summary>
        public bool IsAxisCalibrated(AxisRole role)
        {
            if (raw.AxisCalibration == null)
            {
                return false;
            }

            switch (role)
            {
                case AxisRole.X: return raw.AxisCalibration.X;
                case AxisRole.Y: return raw.AxisCalibration.Y;
                case AxisRole.Z1: return raw.AxisCalibration.Z1;
                case AxisRole.Z2: return raw.AxisCalibration.Z2;
                default: return false;
            }
        }

        /// <summary>
        /// Resolves a representative node for the basic-status read: prefers the
        /// X-axis mapping when authorized, otherwise the first whitelist node.
        /// </summary>
        public int? GetRepresentativeNode()
        {
            if (IsAxisWhitelisted(AxisRole.X))
            {
                return GetAxisMapping(AxisRole.X).NodeId;
            }

            return GetFirstWhitelistNode();
        }

        /// <summary>
        /// Validates every precondition <c>OpenConfiguredReadOnlySession</c>
        /// requires BEFORE an adapter may be constructed. Returns a safe error
        /// code (never a path/COM/NodeID) when any precondition fails, or null
        /// when all pass. Callers MUST consult this before constructing the
        /// adapter so <c>OpenPort</c> is never invoked with invalid parameters.
        ///
        /// Preconditions (all must hold):
        /// - ConnectionType == "CONN_USB" (the only allowed value).
        /// - PortNumber &gt; 0 and BaudRate &gt; 0.
        /// - Whitelist non-empty.
        /// - A representative node exists and is whitelisted.
        /// - Every configured axis mapping resolves to a physical axis (X/Y/Z)
        ///   and its NodeID is in the whitelist. Unmapped roles are permitted
        ///   (not every axis must be configured); only present mappings are
        ///   validated, and any invalid/outsider mapping fail-closes the open.
        /// </summary>
        public string ValidateSessionPreconditions()
        {
            if (!IsConnectionUsb2Can)
            {
                return "OpenConnectionTypeNotUsb";
            }

            if (PortNumber <= 0)
            {
                return "OpenPortNumberInvalid";
            }

            if (BaudRate <= 0)
            {
                return "OpenBaudRateInvalid";
            }

            if (!HasWhitelist)
            {
                return "OpenWhitelistEmpty";
            }

            if (!GetRepresentativeNode().HasValue)
            {
                return "OpenNoRepresentativeNode";
            }

            var roles = new[] { AxisRole.X, AxisRole.Y, AxisRole.Z1, AxisRole.Z2 };
            foreach (var role in roles)
            {
                var mapping = GetAxisMapping(role);
                if (mapping == null)
                {
                    continue;
                }

                if (!IsAxisWhitelisted(role))
                {
                    return "OpenAxisNotWhitelisted";
                }

                if (ResolvePhysicalAxis(role, mapping) == null)
                {
                    return "OpenAxisMappingInvalid";
                }
            }

            return null;
        }

        /// <summary>
        /// Builds session parameters for an axis role. Returns null if the
        /// mapping is missing or the NodeID is not whitelisted.
        /// </summary>
        public ReadOnlySessionParameters BuildParameters(AxisRole role)
        {
            var mapping = GetAxisMapping(role);
            if (mapping == null || !IsAxisWhitelisted(role))
            {
                return null;
            }

            var physicalAxis = ResolvePhysicalAxis(role, mapping);
            if (physicalAxis == null)
            {
                return null;
            }

            return new ReadOnlySessionParameters
            {
                ConnectionType = RequiredConnectionType,
                PortNumber = PortNumber,
                BaudRate = BaudRate,
                NodeId = mapping.NodeId,
                PhysicalAxis = physicalAxis
            };
        }

        /// <summary>
        /// Builds session parameters for the representative node (used by basic
        /// status and Open). Returns null when no representative node exists.
        /// PhysicalAxis defaults to "X" for the representative read because the
        /// basic-status calls (CheckIsInited/CheckHome) are node-level, not
        /// axis-level.
        /// </summary>
        public ReadOnlySessionParameters BuildRepresentativeParameters()
        {
            var node = GetRepresentativeNode();
            if (!node.HasValue)
            {
                return null;
            }

            return new ReadOnlySessionParameters
            {
                ConnectionType = RequiredConnectionType,
                PortNumber = PortNumber,
                BaudRate = BaudRate,
                NodeId = node.Value,
                PhysicalAxis = "X"
            };
        }

        private static string ResolvePhysicalAxis(AxisRole role, AxisMapping mapping)
        {
            // Prefer the explicit per-role mapping (case-insensitive X/Y/Z);
            // fall back to the role's canonical physical axis.
            var configured = mapping.Axis;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var upper = configured.Trim().ToUpperInvariant();
                if (upper == "X" || upper == "Y" || upper == "Z")
                {
                    return upper;
                }
            }

            switch (role)
            {
                case AxisRole.X: return "X";
                case AxisRole.Y: return "Y";
                case AxisRole.Z1:
                case AxisRole.Z2: return "Z";
                default: return null;
            }
        }

        /// <summary>
        /// Parses a role string from IPC (case-insensitive). Returns null for
        /// any value that is not exactly x|y|z1|z2.
        /// </summary>
        public static AxisRole? ParseAxisRole(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "x": return AxisRole.X;
                case "y": return AxisRole.Y;
                case "z1": return AxisRole.Z1;
                case "z2": return AxisRole.Z2;
                default: return null;
            }
        }

        /// <summary>
        /// Loads real-read-only config from <c>SoconBridge.config.local.json</c>
        /// in the bridge base directory. Returns safe defaults on any error.
        /// </summary>
        public static SoconReadOnlyConfig Load(string baseDirectory)
        {
            return Load(delegate
            {
                var directory = string.IsNullOrWhiteSpace(baseDirectory) ? string.Empty : baseDirectory;
                return Path.Combine(directory, LocalConfigFileName);
            });
        }

        /// <summary>
        /// Loads via an injectable path provider (for tests). Never throws.
        /// </summary>
        public static SoconReadOnlyConfig Load(Func<string> pathProvider)
        {
            if (pathProvider == null)
            {
                return Default();
            }

            string path = null;
            try
            {
                path = pathProvider();
            }
            catch (Exception)
            {
                return Default();
            }

            return LoadFromPath(path);
        }

        /// <summary>
        /// Loads from an explicit path. Never throws.
        /// </summary>
        public static SoconReadOnlyConfig LoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Default();
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(BridgeConfig));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var config = serializer.ReadObject(stream) as BridgeConfig;
                    return new SoconReadOnlyConfig(config);
                }
            }
            catch (Exception)
            {
                return Default();
            }
        }

        /// <summary>
        /// Builds a config view from an already-parsed <see cref="BridgeConfig"/>
        /// (for tests that construct the DTO in memory).
        /// </summary>
        public static SoconReadOnlyConfig FromBridgeConfig(BridgeConfig config)
        {
            return new SoconReadOnlyConfig(config);
        }

        public static SoconReadOnlyConfig Default()
        {
            return new SoconReadOnlyConfig(new BridgeConfig());
        }
    }
}
