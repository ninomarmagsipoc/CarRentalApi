using CarRental.IRepository;
using CarRental.Model.Response;
using System.Data.SqlClient;

namespace CarRental.Server
{
    public class LoginClass : ILoginRepository
    {
        private readonly IConfiguration configuration;
        private readonly SqlConnection conn;
        public LoginClass(IConfiguration config)
        {
            configuration = config;
            conn = new SqlConnection(config["ConnectionString:CarRental"]);
        }
        public Task<ServiceResponse<object>> GetLogin(string username, string password)
        {
            throw new NotImplementedException();
        }
    }
}
