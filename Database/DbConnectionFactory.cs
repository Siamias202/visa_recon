using Npgsql;
using System.Data;
using VISA_RECON.API.Application.Interfaces;

namespace VISA_RECON.API.Database
{
    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public DbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection CreateConnection()
        {
            return new NpgsqlConnection(
                _configuration.GetConnectionString("Default")
            );
        }
    }
}
