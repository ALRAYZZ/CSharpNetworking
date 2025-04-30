using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TcpGames.Common;

namespace TcpGamesServer
{
    class GamesServer
    {
		// Server config
		private readonly string _name;
		private readonly int _port;
		private readonly TcpListener _listener;

		// Client management
		private List<TcpClient> _clients;

		// Server state field
		private bool _isRunning;

		// Property to check if the server is running
		public bool IsRunning
		{
			get
			{
				return _isRunning;
			}
			private set
			{
				_isRunning = value;
			}
		}
		public GamesServer(string serverName, int serverPort)
		{
			if (serverName == null)
			{
				throw new ArgumentNullException("serverName", "Server name cannot be null.");
			}
			if (serverPort < 0 || serverPort > 65535)
			{
				throw new ArgumentOutOfRangeException("serverPort", "Server port must be between 0 and 65535.");
			}

			// Initialize server properties
			_name = serverName;
			_port = serverPort;
			_listener = new TcpListener(IPAddress.Any, _port);
			_clients = new List<TcpClient>();
			_isRunning = false;
		}

		public async Task Run()
		{
			// Log server startup
			Console.WriteLine($"Starting the {_name} Game Server on port {_port}.");
			Console.WriteLine($"Press Ctrl+C to shutdown the server.");

			// Start listening for incoming connections
			_listener.Start();
			_isRunning = true;
			Console.WriteLine("Waiting for incoming connections...");

			// Main loop to accept clients
			while (_isRunning)
			{
				// Wait for a client connection
				TcpClient client = await _listener.AcceptTcpClientAsync();

				// Handle the new client
				HandleNewConnectionAsync(client);
			}

			// Stop listening when shutting down
			_listener.Stop();
			Console.WriteLine("The server has been shut down.");
		}

		private async Task HandleNewConnectionAsync(TcpClient client)
		{
			// Log the new connection
			string clientEndpoint = client.Client.RemoteEndPoint.ToString();
			Console.WriteLine($"New connection from {clientEndpoint}");
			// Store the client in the list
			_clients.Add(client);
		}
	}


}
