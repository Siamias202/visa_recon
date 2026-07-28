using VISA_RECON.API.Application.Interfaces;

namespace Ekyc.Onboarding.API.Application.Common
{
    public sealed class RequestContext : IRequestContext
    {
        public string RequestId { get; internal set; } = string.Empty;
        public string ClientIp { get; internal set; } = string.Empty;
    }
}
