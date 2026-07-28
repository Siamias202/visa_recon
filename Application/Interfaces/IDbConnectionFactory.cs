using System.Data;

namespace VISA_RECON.API.Application.Interfaces
{
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}
