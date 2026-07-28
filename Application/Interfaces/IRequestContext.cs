namespace VISA_RECON.API.Application.Interfaces
{
    public interface IRequestContext
    {
        string RequestId { get;  }
        string ClientIp { get;  }

    }
}
