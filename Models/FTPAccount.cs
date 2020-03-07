using System;
using System.Collections.Generic;
using System.Text;

namespace S3FTPServer.Models
{
	class FTPAccount
	{
		public int id;
		public string login;
		public string password;
		public int serverId;

		public FTPAccount(string _login, string _password)
		{
			login = _login;
			password = _password;
		}
	}
}
