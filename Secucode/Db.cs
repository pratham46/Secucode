using MySql.Data.MySqlClient;

namespace Secucode
{
    public static class Db
    {
        // Override with the SECUCODE_DB environment variable on other machines.
        public static readonly string ConnectionString =
            Environment.GetEnvironmentVariable("SECUCODE_DB")
            ?? "Server=localhost;Database=secucode;User Id=root;Password=root;";

        public static MySqlConnection Open()
        {
            var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            return conn;
        }
    }
}
