using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using MySql.Data;
using MySql.Data.MySqlClient;

namespace S3FTPServer
{
	class FTPDatabase
	{
		private MySqlConnection connection;
		public FTPDatabase(string host, string user, string password, string database, int port = 3306)
		{
			string connString = $"server={host};user={user};database={database};port={port};password={password};SslMode = none";
			connection = new MySqlConnection(connString);
			try
			{
				Console.WriteLine("Connection to MYSQL...");
				connection.Open();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
		}

		private object ExecuteSql(string sql)
		{
			MySqlCommand cmd = new MySqlCommand(sql, connection);
			object result = cmd.ExecuteScalar();
			return result;
		}

		public bool LoginExists(string login)
		{
			int result = (int)(long)ExecuteSql($"SELECT EXISTS (SELECT `Login` FROM `FTPAccounts` WHERE `Login` = '{login}');");
			return Convert.ToBoolean(result);
		}

		public AccInfo Auth(string login, string password)
		{
			string sql = $"SELECT `Login`, `Password`, `ServerId` FROM `FTPAccounts` WHERE `Login` = '{login}';";
			MySqlCommand cmd = new MySqlCommand(sql, connection);
			MySqlDataReader rdr = cmd.ExecuteReader();

			AccInfo info = new AccInfo();
			while (rdr.Read())
			{
				info.Login = (string)rdr["Login"];
				info.Password = (string)rdr["Password"];
				info.ServerId = (int)rdr["ServerId"];
			}
			rdr.Close();
			return info;
		}
	}

	public struct AccInfo
	{
		public string Login { get; set; }
		public string Password { get; set; }
		public int ServerId { get; set; }
	}
}
