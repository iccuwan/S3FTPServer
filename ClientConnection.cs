using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Globalization;
using RestSharp;
using static S3FTPServer.Api;
using ScalewaySpaces;
using Amazon.S3.Model;
using System.Text.RegularExpressions;
using System.Linq;

namespace S3FTPServer
{
	class ClientConnection
	{
		private bool loggedIn = false;

		private string login;
		private string password;
		private string root;
		private string path = "/";
		private string transferType;

		private CultureInfo enCulture = CultureInfo.CreateSpecificCulture("en-US");

		private TcpClient client;
		private NetworkStream stream;
		private StreamReader streamReader;
		private StreamWriter streamWriter;

		private TcpListener passiveListener;

		private Api api;
		private ScalewaySpace Space;

		public ClientConnection(TcpClient _client, Api _api, ScalewaySpace _space)
		{
			client = _client;
			api = _api;
			Space = _space;

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
							case "PWD":
								response = string.Format("257 \"{0}\" is current directory", path);
								break;
							case "TYPE":
								string[] splitArgs = arguments.Split(' ');
								response = Type(splitArgs[0], splitArgs.Length > 1 ? splitArgs[1] : null);
								break;
							case "PASV":
								response = Passive();
								break;
							case "LIST":
								response = List(arguments);
								break;
							case "CWD":
								response = ChangeDirectory(arguments);
								break;
							case "CDUP":
								response = ChangeDirectory("..");
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
				//client.Close();
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

		private string Type(string typeCode, string formatControl)
		{
			string response;
			switch (typeCode)
			{
				case "I":
					response = "200 OK";
					break;
				default:
					response = "504 Command not implemented for that parameter.";
					break;
			}
			transferType = typeCode;
			return response;
		}

		private string Passive()
		{
			IPAddress localAddress = ((IPEndPoint)client.Client.LocalEndPoint).Address;
			passiveListener = new TcpListener(localAddress, 0);
			passiveListener.Start();

			IPEndPoint localEndpoint = ((IPEndPoint)passiveListener.LocalEndpoint);
			byte[] address = localEndpoint.Address.GetAddressBytes();
			short port = (short)localEndpoint.Port;
			byte[] portArray = BitConverter.GetBytes(port);
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(portArray);
			}
			return string.Format("227 Entering Passive Mode ({0},{1},{2},{3},{4},{5})",
				address[0], address[1], address[2], address[3], portArray[0], portArray[1]);
		}

		private string List(string pathname)
		{
			if (pathname == null)
			{
				pathname = "/";
			}
			passiveListener.BeginAcceptTcpClient(DoList, pathname);
			return string.Format("150 Opening passive mode data transfer for LIST");
		}

		private void DoList(IAsyncResult result)
		{
			TcpClient dataClient = passiveListener.EndAcceptTcpClient(result);
			string pathname = (string)result.AsyncState;
			using (NetworkStream dataStream = dataClient.GetStream())
			{
				StreamReader dataReader = new StreamReader(dataStream, Encoding.ASCII);
				StreamWriter dataWriter = new StreamWriter(dataStream, Encoding.ASCII);

				List<S3Object> objects = Space.GetObjects(GetValidPath(pathname));
				foreach (S3Object obj in objects)
				{
					string filename = DeletePathFromObjectKey(obj.Key);
					
					string date = obj.LastModified < DateTime.Now - TimeSpan.FromDays(180) ?
						obj.LastModified.ToString("MMM dd yyyy", enCulture) :
						obj.LastModified.ToString("MMM dd HH:mm", enCulture);
					string line;
					if (obj.Key.EndsWith("/"))
					{
						line = string.Format("drwxr-xr-x    2 {3}     {3}     {0,8} {1} {2}",
							"4096", date, filename, login);
					}
					else
					{
						line = string.Format("-rw-r--r-- 2 {3} {3}           {0,8} {1} {2}",
							obj.Size, date, filename, login);
					}
					dataWriter.WriteLine(line);
					dataWriter.Flush();
				}
				dataClient.Close();
				SendMessage("226 Transfer complete.");
			}
		}

		private string ChangeDirectory(string pathname)
		{
			if (pathname.EndsWith(".."))
			{
				int index = path.LastIndexOf("/");
				if (index > 0)
				{
					path = path.Substring(0, index);
				}
			}
			else
			{
				path += pathname;
			}
			return string.Format("200 Changing to {0} directory", path);
		}

		private string GetValidPath(string pathname)
		{
			return string.Format("{0}{1}", root, pathname);
		}

		private void SendMessage(string text)
		{
			streamWriter.WriteLine(text);
			streamWriter.Flush();
		}

		private string DeletePathFromObjectKey(string key)
		{
			string name = Regex.Replace(key, root + path, "");
			return name;
		}
	}
}
