﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Globalization;
using RestSharp;
using ScalewaySpaces;
using Amazon.S3.Model;
using System.Text.RegularExpressions;
using System.Linq;
using System.Timers;
using S3FTPServer.Models;

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

		private DirectoryObject renameObject;

		private const long MB = 1024 * 1024;

		private CultureInfo enCulture = CultureInfo.CreateSpecificCulture("en-US");

		private TcpClient client;
		private NetworkStream stream;
		private StreamReader streamReader;
		private StreamWriter streamWriter;

		private TcpListener passiveListener;

		//private Api api;
		private ScalewaySpace Space;

		public List<DirectoryObject> ObjectsInDirectory = new List<DirectoryObject>();

		public ClientConnection(TcpClient _client, ScalewaySpace _space)
		{
			client = _client;
			Space = _space;

			stream = client.GetStream();
			streamReader = new StreamReader(stream, Encoding.ASCII);
			streamWriter = new StreamWriter(stream, Encoding.ASCII);

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
								//errorTimer = new Timer(10000);
								//errorTimer.Elapsed += Timeout;
								//errorTimer.AutoReset = false;
								//errorTimer.Enabled = true;
								response = ChangeDirectory(arguments);
								break;
							case "CDUP":
								response = ChangeDirectory("..");
								break;
							case "RETR":
								response = Retrieve(arguments);
								break;
							case "STOR":
								//string test = CreateDirectory(path);
								response = Store(arguments);
								break;
							case "DELE":
								response = Delete(arguments, ObjectType.File);
								break;
							case "MKD":
								response = CreateDirectory(arguments);
								break;
							case "RMD":
								response = Delete(arguments, ObjectType.Directory);
								break;
							case "RNFR":
								response = RenameObject(arguments);
								break;
							/*case "RNTO":
								response = RenameObjectTo(arguments);
								break;
							*/
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

		private string RenameObject(string pathname)
		{
			UpdateObjectsList(path);
			ObjectType type;
			if (pathname.EndsWith('/'))
			{
				type = ObjectType.Directory;
			}
			else
			{
				type = ObjectType.File;
			}
			DirectoryObject obj = FindObject(pathname, type);
			if (obj != null)
			{
				renameObject = obj;
				return "350 Waiting new file name";
			}
			else
			{
				if (type == ObjectType.Directory)
				{
					type = ObjectType.File;
				}
				else
				{
					type = ObjectType.Directory;
				}
				DirectoryObject obj2 = FindObject(pathname, type);
				if (obj2 != null)
				{
					renameObject = obj2;
					return "350 Waiting new file name";
				}
			}
			return "550 File not found";
		}

		private string RenameObjectTo(string pathname)
		{
			if (pathname.StartsWith('/'))
			{
				pathname = root + pathname;
			}
			else
			{
				pathname = string.Format("{0}{2}{1}", root, pathname, path);
			}
			if (renameObject.Type == ObjectType.Directory)
			{
				pathname += "/";
			}
			bool response = Space.RenameObject(renameObject.Path, pathname);
			if (response)
			{
				return "250 OK";
			}
			return "501 Error";
		}

		private string CreateDirectory(string pathname)
		{
			string fullPath;
			if (!pathname.StartsWith('/'))
			{
				fullPath = root + path + pathname + "/";
			}
			else
			{
				fullPath = root + pathname;
			}
			Space.CreateDirectory(fullPath);
			return string.Format("250 {0} created", '"' + pathname + '"');
		}

		private string Delete(string pathname, ObjectType type)
		{
			DirectoryObject obj = FindObject(pathname, type);
			if (obj != null)
			{
				Space.DeleteObject(obj.Path);
			}
			return string.Format("250 {0} deleted", type.ToString());
		}

		private string User(string _username)
		{
			/*
			if (Api.LoginExists(_username))
			{
				login = _username;
				return "331 Login ok, need password";
			}
			*/
			if (Server.DB.LoginExists(_username))
			{
				login = _username;
				return "331 Login ok, need password";
			}
			return "430 Bad login";
		}

		private string Password(string _password)
		{
			AccInfo info = Server.DB.Auth(login, _password);
			if (info.Password == _password)
			{
				password = _password;
				root = info.ServerId.ToString();
				loggedIn = true;
				UpdateObjectsList(path);
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
				ObjectsInDirectory.Clear();
				Console.WriteLine("Objects list cleared");
				foreach (S3Object obj in objects)
				{
					string filename = DeletePathFromObjectKey(obj.Key);
					int count = filename.Split('/').Length - 1;
					if (count == 1 && filename.EndsWith('/') || count == 0 && !string.IsNullOrEmpty(filename))
					{

						string date = obj.LastModified < DateTime.Now - TimeSpan.FromDays(180) ?
							obj.LastModified.ToString("MMM dd yyyy", enCulture) :
							obj.LastModified.ToString("MMM dd HH:mm", enCulture);
						string line;
						DirectoryObject dirObj;
						if (obj.Key.EndsWith("/"))
						{
							filename = filename.Remove(filename.Length - 1);
							line = string.Format("drwxr-xr-x    2 {3}     {3}     {0,8} {1} {2}",
								"4096", date, filename, login);
							dirObj = new DirectoryObject(filename, obj.Key, ObjectType.Directory);
						}
						else
						{
							line = string.Format("-rw-r--r-- 2 {3} {3}           {0,8} {1} {2}",
								obj.Size, date, filename, login);
							dirObj = new DirectoryObject(filename, obj.Key, ObjectType.File);
						}
						ObjectsInDirectory.Add(dirObj);
						Console.WriteLine("{0} {1} added to objects list", dirObj.Type.ToString(), filename);
						dataWriter.WriteLine(line);
						dataWriter.Flush();
					}
				}
				dataClient.Close();
				SendMessage("226 Transfer complete.");
			}
		}

		private void UpdateObjectsList(string pathname)
		{
			List<S3Object> objects = Space.GetObjects(GetValidPath(pathname));
			ObjectsInDirectory.Clear();
			Console.WriteLine("Objects list cleared");
			foreach (S3Object obj in objects)
			{
				string filename = DeletePathFromObjectKey(obj.Key);
				MetadataCollection meta = Space.GetObjectMeta(obj);
				//filename = meta["name"];
				int count = filename.Split('/').Length - 1;
				if (count == 1 && filename.EndsWith('/') || count == 0 && !string.IsNullOrEmpty(filename))
				{

					string date = obj.LastModified < DateTime.Now - TimeSpan.FromDays(180) ?
						obj.LastModified.ToString("MMM dd yyyy", enCulture) :
						obj.LastModified.ToString("MMM dd HH:mm", enCulture);
					string line;
					DirectoryObject dirObj;
					if (obj.Key.EndsWith("/"))
					{
						filename = filename.Remove(filename.Length - 1);
						line = string.Format("drwxr-xr-x    2 {3}     {3}     {0,8} {1} {2}",
							"4096", date, filename, login);
						dirObj = new DirectoryObject(filename, obj.Key, ObjectType.Directory);
					}
					else
					{
						line = string.Format("-rw-r--r-- 2 {3} {3}           {0,8} {1} {2}",
							obj.Size, date, filename, login);
						dirObj = new DirectoryObject(filename, obj.Key, ObjectType.File);
					}
					ObjectsInDirectory.Add(dirObj);
					Console.WriteLine("{0} {1} added to objects list", dirObj.Type.ToString(), filename);
				}
			}
		}

		private string Retrieve(string pathname)
		{
			//DirectoryObject file = ObjectsInDirectory.Find(x => x.Name == pathname && x.Type == ObjectType.File);
			string filepath = root + path + pathname;
			if (!string.IsNullOrEmpty(pathname) && Space.ObjectExists(filepath))
			{
				passiveListener.BeginAcceptTcpClient(DoRetrieve, filepath);
				return string.Format("150 Opening passive mode data transfer for RETR");
			}
			return "550 File Not Found";
		}

		private void DoRetrieve(IAsyncResult result)
		{
			TcpClient dataClient = passiveListener.EndAcceptTcpClient(result);
			string pathname = (string)result.AsyncState;

			using (NetworkStream dataStream = dataClient.GetStream())
			{
				using (Stream s = Space.StreamFileFromS3(pathname))
				{
					CopyStream(s, dataStream, 4096);
				}
			}
			dataClient.Close();
			SendMessage("226 Closing data connection, file transfer successful");
		}

		private long CopyStream(Stream input, Stream output, int bufferSize)
		{
			byte[] buffer = new byte[bufferSize];
			int count = 0;
			long total = 0;

			while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, count);
				total += count;
			}
			return total;
		}

		private bool CopyStreamUpload(NetworkStream input, MemoryStream output, long bufferSize, string path, string name)
		{
			byte[] buffer = new byte[bufferSize];
			int count;
			long totalPart = 0;
			int partNumber = 1;
			long total = 0;
			long totalUpload = 0;

			bool multipart = false;
			List<UploadPartResponse> uploadParts = new List<UploadPartResponse>();
			InitiateMultipartUploadResponse uploadInfo = new InitiateMultipartUploadResponse();
			
			while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer , 0, count);
				total += count;
				totalPart += count;
				if (totalPart > 5 * MB)
				{
					if (!multipart)
					{
						uploadInfo = Space.InitMultipartUpload(path, name);
						multipart = true;
					}
					UploadPartResponse uploadedPart = Space.UploadPart(path, uploadInfo.UploadId, partNumber, totalPart, output, false);
					uploadParts.Add(uploadedPart);
					partNumber++;
					totalUpload += totalPart;
					totalPart = 0;
					output.Position = 0;
					output.SetLength(0);
				}
			}
			
			if (multipart)
			{
				UploadPartResponse lastPart = Space.UploadPart(path, uploadInfo.UploadId, partNumber, totalPart, output, true);
				uploadParts.Add(lastPart);
				CompleteMultipartUploadResponse complete = Space.CompleteMultipartUpload(path, uploadInfo.UploadId, uploadParts);
			}
			
			return multipart;
		}

		private string Store(string pathname)
		{
			string filepath = root + path + pathname;
			filepath = Regex.Replace(filepath, @"\s+", string.Empty);
			if (!string.IsNullOrEmpty(pathname) && !string.IsNullOrWhiteSpace(pathname))
			{
				string dir = path;
				var strAr = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
				while (strAr.Length != 0)
				{
					string dirPath = string.Join('/', strAr);
					dirPath += '/';
					if (!Space.Exists(root + '/' + dirPath))
					{
						Space.CreateDirectory(root + '/' + dirPath);
					}
					Array.Resize(ref strAr, strAr.Length - 1);
				}
				string[] data = new string[2];
				data[0] = filepath;
				data[1] = pathname;
				passiveListener.BeginAcceptTcpClient(DoStore, data);
				return "150 Opening passive mode data transfer for STOR";
			}
			return "450 Bad file name";
		}

		private void DoStore(IAsyncResult result)
		{
			TcpClient dataClient = passiveListener.EndAcceptTcpClient(result);
			string[] data = (string[])result.AsyncState;
			MemoryStream output = new MemoryStream();

			using (NetworkStream dataStream = dataClient.GetStream())
			{
				bool multipart = CopyStreamUpload(dataStream, output, 2048, data[0], data[1]);
				if (!multipart)
				{
					Space.UploadObject(data[0], output, data[1]);
				}
			}
			output.Close();
			dataClient.Close();
			SendMessage("226 Closing data connection, file transfered successful");
		}

		private string ChangeDirectory(string pathname)
		{
			if (pathname == "/")
			{
				path = "/";
			}
			else
			{
				if (pathname.StartsWith("/"))
				{
					//pathname = pathname.Remove(0, 1);
					path = pathname + "/";
					return string.Format("200 Changing to {0} directory", path);
				}
				if (pathname.EndsWith(".."))
				{
					int index = path.LastIndexOf("/");
					if (index > 0)
					{
						path = path.Substring(0, index);
					}
					index = path.LastIndexOf("/");
					if (index >= 0)
					{
						path = path.Substring(0, index + 1);
					}
				}
				else
				{
					DirectoryObject newDir = ObjectsInDirectory.Find(x => x.Name == pathname && x.Type == ObjectType.Directory);
					path = DeleteRootFromPath(newDir.Path);
					//path += pathname;
				}
			}
			UpdateObjectsList(path);
			return string.Format("200 Changing to {0} directory", path);
		}

		private string GetValidPath(string pathname)
		{
			return string.Format("{0}{1}", root, pathname);
		}

		private string DeleteRootFromPath(string pathname)
		{
			string newString = pathname.Remove(0, root.Length);
			return newString;
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

		private DirectoryObject FindObject(string name, ObjectType type)
		{
			DirectoryObject obj = ObjectsInDirectory.Find(x => x.Name == name && x.Type == type);
			return obj;
		}
	}
}
