using System.Data;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
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
            var provider = _configuration["DatabaseProvider"];

            return provider?.ToLower() switch
            {
                "postgres" => new NpgsqlConnection(
                    _configuration.GetConnectionString("Postgres")),

                "sqlserver" => new SqlConnection(
                    _configuration.GetConnectionString("SqlServer")),

                "oracle" => new OracleConnection(
                    _configuration.GetConnectionString("Oracle")),

                "mysql" => new MySqlConnection(
                    _configuration.GetConnectionString("mysql")),

                _ => throw new NotSupportedException(
                    $"Database provider '{provider}' is not supported.")
            };
        }
    }
}
