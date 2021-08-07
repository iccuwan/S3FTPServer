using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RestSharp;
using ScalewaySpaces;


namespace S3FTPServer
{
	class Server
	{
		private TcpListener server;
		
		public IPAddress serverIP;
		public int serverPort = 21;
		//public IRestClient api = new RestClient("https://localhost:44343/api/ftp/");
		public static ScalewaySpace Space;
		public static FTPDatabase DB { get; set; }
		
		public Server()
		{
			serverIP = GetLocalIPAddress();
			Console.WriteLine("Starting Server {0}:{1}", serverIP, serverPort);
			Space = new ScalewaySpace();
			DB = new FTPDatabase("135.181.201.237", "gms", "6dIbkgyVe6zw6Kpr", "gms");
			server = new TcpListener(serverIP, serverPort);
			server.Start();
			StartListener();
		}

		private void StartListener()
		{
			try
			{
				while (true)
				{
					Console.WriteLine("Waiting for a connection...");
					TcpClient client = server.AcceptTcpClient();
					Console.WriteLine("Client connected!");

					Thread t = new Thread(new ParameterizedThreadStart(HandleDevice));
					t.Start(client);
				}
			}
			catch (SocketException e)
			{
				Console.WriteLine("Server Exception: {0}", e);
				server.Stop();
			}
		}

		private void HandleDevice(Object obj)
		{
			ClientConnection _connection = new ClientConnection((TcpClient)obj, Space);
			/*
			TcpClient client = (TcpClient)obj;
			NetworkStream stream = client.GetStream();
			string imei = String.Empty;

			string data;
			byte[] bytes = new byte[256];
			int i;
			try
			{
				while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
				{
					string hex = BitConverter.ToString(bytes);
					data = Encoding.ASCII.GetString(bytes, 0, i);
					Console.WriteLine("{1}: Received: {0}", data, Thread.CurrentThread.ManagedThreadId);

					string str = "Hey Device!";
					byte[] reply = System.Text.Encoding.ASCII.GetBytes(str);
					stream.Write(reply, 0, reply.Length);
					Console.WriteLine("{1}: Sent: {0}", str, Thread.CurrentThread.ManagedThreadId);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Client Exception: {0}", e.ToString());
				client.Close();
			}
			*/
		}

		private IPAddress GetLocalIPAddress()
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
			{
				socket.Connect("8.8.8.8", 65530);
				IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
				return endPoint.Address;
			}
		}
	}
}
