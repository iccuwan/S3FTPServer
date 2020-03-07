using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using RestSharp;
using static S3FTPServer.Api;

namespace S3FTPServer
{
	class ClientConnection
	{
		private bool loggedIn = false;

		private string login;
		private string password;
		private string root;
		private string path;

		private TcpClient client;
		private NetworkStream stream;
		private StreamReader streamReader;
		private StreamWriter streamWriter;

		private Api api;

		public ClientConnection(TcpClient _client, Api _api)
		{
			client = _client;
			api = _api;

			stream = client.GetStream();
			streamReader = new StreamReader(stream);
			streamWriter = new StreamWriter(stream);

			HandleConnection();
		}

		private void HandleConnection()
		{
			SendMessage("220 Ready.");

			string text;

			try
			{
				while (!string.IsNullOrEmpty(text = streamReader.ReadLine()))
				{
					string response = null;

					string[] command = text.Split(' ');

					string cmd = command[0].ToUpperInvariant();
					string arguments = command.Length > 1 ? text.Substring(command[0].Length + 1) : null;

					if (string.IsNullOrWhiteSpace(arguments))
					{
						arguments = null;
					}
					if (response == null)
					{
						switch (cmd)
						{
							case "USER":
								response = User(arguments);
								break;
							case "PASS":
								response = Password(arguments);
								break;
							default:
								response = "502 Command not implemented";
								break;
						}
					}
					if (client == null || !client.Connected)
					{
						break;
					}
					else
					{
						SendMessage(response);
						if (response.StartsWith("221"))
						{
							break;
						}
					}
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Client connection exception: {0}", e.ToString());
				client.Close();
			}
		}

		private string User(string _username)
		{
			if (Api.LoginExists(_username))
			{
				login = _username;
				return "331 Login ok, need password";
			}
			return "430 Bad login";
		}

		private string Password(string _password)
		{
			AuthInfo info = Api.Auth(login, _password);
			if (info.password == _password)
			{
				password = _password;
				root = info.serverId.ToString();
				loggedIn = true;
				return "230 Logged in";
			}
			return "502";
		}

		private void SendMessage(string text)
		{
			streamWriter.WriteLine(text);
			streamWriter.Flush();
		}
	}
}
