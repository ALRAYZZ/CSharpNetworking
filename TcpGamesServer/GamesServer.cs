using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TcpGames.Common;
using TcpGamesServer.Classes;

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
		private List<TcpClient> _waitingLobby;
		private Dictionary<TcpClient, IGame> _gameClientIsIn;

		// Game management
		private IGame _nextGame;
		private List<IGame> _games;
		private List<Thread> _gameThreads;

		// Server state field
		private bool _isRunning;

		// Static reference for Ctrl+C handler
		private static GamesServer? _serverInstance;

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
			_waitingLobby = new List<TcpClient>();
			_gameClientIsIn = new Dictionary<TcpClient, IGame>();
			_games = new List<IGame>();
			_gameThreads = new List<Thread>();
			_nextGame = null; // NOT IMPLEMENTED YET
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
				await HandleNewConnectionAsync(client);

				// Check for disconnection in waiting lobby
				List<TcpClient> clientsToRemove = new List<TcpClient>();
				foreach (TcpClient waitingClient in _waitingLobby)
				{
					bool isDisconnected = await CheckForDisconnectionAsync(waitingClient);
					if (isDisconnected)
					{
						clientsToRemove.Add(waitingClient);
					}
				}

				// Clean up disconnected clients
				foreach (TcpClient clientToRemove in clientsToRemove)
				{
					string clientEndpoint = clientToRemove.Client.RemoteEndPoint.ToString();
					HandleDisconnectedClient(clientToRemove);
					Console.WriteLine($"Client {clientEndpoint} has disconnected from the server.");
				}

				// Start a game if enough players are waiting
				if (_waitingLobby.Count >= _nextGame.RequiredPlayers)
				{
					// Assign players to the game
					List<TcpClient> gamePlayers = new List<TcpClient>();
					int playersAdded = 0;
					while (playersAdded < _nextGame.RequiredPlayers && _waitingLobby.Count > 0)
					{
						TcpClient player = _waitingLobby[0];
						_waitingLobby.RemoveAt(0);

						bool addedSuccessfully = _nextGame.AddPlayer(player);
						if (addedSuccessfully)
						{
							gamePlayers.Add(player);
							_gameClientIsIn[player] = _nextGame;
							playersAdded++;
						}
						else
						{
							_waitingLobby.Add(player); // Re-add the player if not added to the game
						}
					}

					// Start the game in a new thread
					if (playersAdded == _nextGame.RequiredPlayers)
					{
						Console.WriteLine($"Starting a {_nextGame.Name} game.");
						Thread gameThread = new Thread(new ThreadStart(_nextGame.Run));
						gameThread.Start();
						_games.Add(_nextGame); // Add the game to the list of games
						_gameThreads.Add(gameThread); // Add the game thread to the list of threads

						// Prepare the next game
						_nextGame = null; // Reset the next game to null after starting it
					}
					else
					{
						// Rever if not enough players added
						foreach (TcpClient player in gamePlayers)
						{
							_waitingLobby.Add(player); // Re-add the player to the waiting lobby
							_gameClientIsIn.Remove(player); // Remove from the game dictionary
						}
					}
				}
			}

			// Stop listening when shutting down
			_listener.Stop();
			Console.WriteLine("The server has been shut down.");
		}


		public void Shutdown()
		{
			if (_isRunning)
			{
				_isRunning = false;
				Console.WriteLine("Shutting down the Game Server.");
			}
		}
		private async Task HandleNewConnectionAsync(TcpClient client)
		{
			// Log the new connection
			string clientEndpoint = client.Client.RemoteEndPoint.ToString();
			Console.WriteLine($"New connection from {clientEndpoint}");


			// Store the client in the list
			_clients.Add(client);
			_waitingLobby.Add(client);

			// Send welcome packet
			string welcomeMessage = $"Welcome to {_name} Game Server";
			Packet welcomePacket = new Packet("welcome", welcomeMessage);
			await SendPacketAsync(client, welcomePacket);
		}

		private async Task SendPacketAsync(TcpClient client, Packet packet)
		{
			// Convert the packet to JSON and encode it to bytes
			string jsonString = packet.ToJson();
			byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

			// Create length prefix (16-bit unsigned integer)
			ushort length = (ushort)jsonBytes.Length; // We need to cast to ushort s ince .Length returns an int
			byte[] lengthBytes = BitConverter.GetBytes(length);

			// Combine length and JSON bytes
			byte[] messageBytes = new byte[lengthBytes.Length + jsonBytes.Length]; // Create the array with as many positions as the sum of the two arrays
			lengthBytes.CopyTo(messageBytes, 0); // Now HERE we start populating the newly created array with the length bytes
			jsonBytes.CopyTo(messageBytes, lengthBytes.Length); // Adding, to the end of the array, the JSON bytes

			// Send the packet
			NetworkStream stream = client.GetStream();
			await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
		}
		private async Task<Packet> ReceivePacketAsync(TcpClient client)
		{
			try
			{
				NetworkStream stream = client.GetStream();

				// Check if data availabe
				if (client.Available == 0)
				{
					return null; // No data available
				}

				// Read the length prefix (2 bytes)
				byte[] lengthBytes = new byte[2];
				int bytesRead = await stream.ReadAsync(lengthBytes, 0, lengthBytes.Length);
				if (bytesRead !=2)
				{
					return null;
				}

				ushort packetLength = BitConverter.ToUInt16(lengthBytes, 0);

				// Read JSON bytes
				byte[] jsonBytes = new byte[packetLength];
				bytesRead = await stream.ReadAsync(jsonBytes, 0, jsonBytes.Length);
				if (bytesRead != packetLength)
				{
					return null;
				}

				// Decode and deserialize the JSON
				string jsonString = Encoding.UTF8.GetString(jsonBytes);
				Packet packet = Packet.FromJson(jsonString);
				return packet;
			}
			catch (Exception)
			{
				// Assume disconnection on error
				return null;
			}
		}
		private async Task<bool> CheckForDisconnectionAsync(TcpClient client)
		{
			// Check for graceful disconnection (bye packet)
			Packet packet = await ReceivePacketAsync(client);
			if (packet != null && packet.Command == "bye")
			{
				return true;
			}

			// Check for abrupt disconnection
			bool isDisconnected = IsDisconnected(client);
			if (isDisconnected)
			{
				return true;
			}
			return false;
		}

		private bool IsDisconnected(TcpClient client)
		{
			try
			{
				Socket socket = client.Client;
				bool isReadable = socket.Poll(10 * 1000, SelectMode.SelectRead); // Poll for 10 seconds
				bool noData = socket.Available == 0;
				return isReadable && noData; // If both are true, we return true using the &&, else we return false
			}
			catch (SocketException)
			{
				return true; // Assume disconnection on error
			}
		}
		public void DisconnectClient(TcpClient client, string message)
		{
			// Log the disconnection
			string clientEndpoint = client.Client.RemoteEndPoint.ToString();
			Console.WriteLine($"Disconnecting the client from {clientEndpoint}");

			// Send "bye" packet
			if (string.IsNullOrEmpty(message))
			{
				message = "Bye!";
			}
			Packet byePacket = new Packet("bye", message);
			SendPacketAsync(client, byePacket).GetAwaiter().GetResult();

			// Notify the game (if any)
			if (_gameClientIsIn.ContainsKey(client))
			{
				IGame game = _gameClientIsIn[client];
				game.DisconnectClient(client);
			}

			// Clean up
			HandleDisconnectedClient(client);
		}
		private void HandleDisconnectedClient(TcpClient client)
		{
			// Remove from collections
			_clients.Remove(client);
			_waitingLobby.Remove(client);
			_gameClientIsIn.Remove(client);

			// Clean up resources
			client.GetStream().Close();
			client.Close();

		}
		private static void HandleCtrlC(object sender, ConsoleCancelEventArgs args)
		{
			// Prevent default Ctrl+C behavior (immediate termination)
			args.Cancel = true;

			// Shutdown the server
			_serverInstance?.Shutdown();
		}

		public static void Main(string[] args)
		{
			// Default configuration
			string serverName = "DefaultGameServer";
			int serverPort = 6000;

			// Set up Ctrl+C handler
			Console.CancelKeyPress += HandleCtrlC!;

			// Create and run the server
			_serverInstance = new GamesServer(serverName, serverPort);
			_serverInstance.Run().GetAwaiter().GetResult();
		}
	}


}
