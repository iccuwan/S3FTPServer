using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using RestSharp;
using Newtonsoft.Json;
using S3FTPServer.Models;
using ScalewaySpaces;


namespace S3FTPServer
{
	class Api
	{
		private static IRestClient client;

		public Api()
		{
			client = new RestClient("https://localhost:44343/api/ftp");
		}

		public static bool LoginExists(string login)
		{
			var request = new RestRequest("auth/{login}", Method.GET);
			request.AddUrlSegment("login", login);
			var response = client.Execute(request);
			if (response.StatusCode == HttpStatusCode.NoContent)
			{
				return true;
			}
			return false;
		}

		public static AuthInfo Auth(string login, string password)
		{
			var request = new RestRequest("auth", Method.POST);
			FTPAccount ftpAcc = new FTPAccount(login, password);
			request.AddJsonBody(ftpAcc);
			var response = client.Execute(request);
			if (response.StatusCode == HttpStatusCode.OK)
			{
				AuthInfo info = JsonConvert.DeserializeObject<AuthInfo>(response.Content);
				return info;
			}
			AuthInfo emptyInfo = new AuthInfo
			{
				id = 0
			};
			return emptyInfo;
		}

		public class AuthInfo
		{
			public int id;
			public int serverId;
			public string login;
			public string password;
		}
	}
}
