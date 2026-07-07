using System;
using System.Collections.Generic;

namespace Stainer.SoconBridge
{
    internal sealed class BridgeRequestProcessor
    {
        private const string PingCommand = "Ping";
        private const string GetBridgeStatusCommand = "GetBridgeStatus";
        private const string ValidateSdkDeploymentCommand = "ValidateSdkDeployment";

        private readonly ISdkDeploymentValidator validator;
        private BridgeStatus currentStatus;

        public BridgeRequestProcessor(ISdkDeploymentValidator validator, BridgeStatus initialStatus)
        {
            if (validator == null)
            {
                throw new ArgumentNullException("validator");
            }

            this.validator = validator;
            currentStatus = initialStatus;
        }

        public BridgeStatus CurrentStatus
        {
            get { return currentStatus; }
        }

        public static BridgeRequestProcessor CreateDefault(string baseDirectory)
        {
            var options = new SdkDeploymentValidatorOptions(baseDirectory);
            var deploymentValidator = new SdkDeploymentValidator(
                options,
                new DefaultProcessArchitectureProbe(),
                new PeArchitectureInspector());

            return new BridgeRequestProcessor(deploymentValidator, BridgeStatus.Offline);
        }

        public BridgeResponse Process(BridgeRequest request)
        {
            if (request == null)
            {
                request = new BridgeRequest();
            }

            var requestId = request.RequestId ?? string.Empty;
            var command = request.Command ?? string.Empty;

            Console.WriteLine(
                "IPC request command={0} requestId={1}",
                SanitizeForLog(command),
                SanitizeForLog(requestId));

            if (string.Equals(command, PingCommand, StringComparison.Ordinal))
            {
                return CreateResponse(requestId, command, true, currentStatus, "Pong", new BridgeResponseDetails(), new List<string>());
            }

            if (string.Equals(command, GetBridgeStatusCommand, StringComparison.Ordinal))
            {
                return CreateResponse(requestId, command, true, currentStatus, "OK", new BridgeResponseDetails(), new List<string>());
            }

            if (string.Equals(command, ValidateSdkDeploymentCommand, StringComparison.Ordinal))
            {
                var result = validator.Validate();
                currentStatus = result.Status;

                Console.WriteLine(
                    "SDK deployment validation status={0} warnings={1}",
                    result.Status,
                    result.Warnings.Count);

                return CreateResponse(
                    requestId,
                    command,
                    result.Success,
                    result.Status,
                    result.Success ? "DeploymentValidated" : "DeploymentNotReady",
                    result.Details,
                    result.Warnings);
            }

            return CreateResponse(requestId, command, false, currentStatus, "NotSupported", new BridgeResponseDetails(), new List<string>());
        }

        private static BridgeResponse CreateResponse(
            string requestId,
            string command,
            bool success,
            BridgeStatus status,
            string message,
            BridgeResponseDetails details,
            List<string> warnings)
        {
            return new BridgeResponse
            {
                RequestId = requestId ?? string.Empty,
                Command = command ?? string.Empty,
                Success = success,
                BridgeStatus = status.ToString(),
                Message = message ?? string.Empty,
                Details = details ?? new BridgeResponseDetails(),
                Warnings = warnings ?? new List<string>()
            };
        }

        private static string SanitizeForLog(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            var chars = value.ToCharArray();
            var limit = Math.Min(chars.Length, 80);
            var sanitized = new char[limit];
            for (var i = 0; i < limit; i++)
            {
                var c = chars[i];
                sanitized[i] = char.IsControl(c) ? '_' : c;
            }

            return new string(sanitized);
        }
    }
}
