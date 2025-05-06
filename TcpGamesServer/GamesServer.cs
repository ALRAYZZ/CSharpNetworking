using System.Net;
using System.Net.Sockets;
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
		private Dictionary<string, List<TcpClient>> _gameLobbies;
		private Dictionary<TcpClient, IGame> _gameClientIsIn;

		// Game management
		private Dictionary<string, IGame> _nextGames;
		private List<IGame> _games;
		private List<Task> _gameTasks;
		private CancellationTokenSource _cancellationTokenSource;

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
			_gameLobbies = new Dictionary<string, List<TcpClient>>();
			_gameClientIsIn = new Dictionary<TcpClient, IGame>();
			_games = new List<IGame>();
			_gameTasks = new List<Task>();
			_nextGames = new Dictionary<string, IGame>(); // Instantiate a placeholder game to be used when the server starts
			_cancellationTokenSource = new CancellationTokenSource();
			_gameLobbies["Placeholder Game"] = new List<TcpClient>(); // Create a placeholder game to be used when the server starts
			_nextGames["Placeholder Game"] = new PlaceholderGame(this); // Assign the placeholder game to the next game to be used when the server starts
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

			// Track pending connection tasks
			List<Task> connectionTasks = new List<Task>();

			// Main loop to accept clients
			while (_isRunning)
			{
				// This new approach uses Tasks to handle multiple connections instead of blocking the main thread for each connection

				// Wait for a client connection
				Task<TcpClient> acceptTask = _listener.AcceptTcpClientAsync();
				connectionTasks.Add(acceptTask);

				// Wait for the connection to complete
				TcpClient client = await acceptTask;

				// Hanndle the new client
				await HandleNewConnectionAsync(client);

				// Check for disconnection in waiting lobby
				List<TcpClient> clientsToRemove = new List<TcpClient>();
				foreach (List<TcpClient> listClients in _gameLobbies.Values)
				{
					foreach (TcpClient waitingClient in listClients)
					{
						bool isDisconnected = await CheckForDisconnectionAsync(waitingClient);
						if (isDisconnected)
						{
							clientsToRemove.Add(waitingClient);
						}
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
				foreach (string gameName in _gameLobbies.Keys)
				{
					List<TcpClient> lobby = _gameLobbies[gameName];
					IGame nextGame = _nextGames[gameName];

					if (lobby.Count >= nextGame.RequiredPlayers)
					{
						List<TcpClient> gamePlayers = new List<TcpClient>();
						int playersAdded = 0;
						while (playersAdded < nextGame.RequiredPlayers && lobby.Count > 0)
						{
							TcpClient player = lobby[0];
							lobby.RemoveAt(0); // Remove the player from the lobby

							bool addedSuccessfully = nextGame.AddPlayer(player);
							if (addedSuccessfully)
							{
								gamePlayers.Add(player);
								_gameClientIsIn[player] = nextGame; // Track the game the client is in
								playersAdded++;
							}
							else
							{
								lobby.Add(player);
							}
						}

						if (playersAdded == nextGame.RequiredPlayers)
						{
							Console.WriteLine($"Starting a {nextGame.Name} game.");
							IGame gameToRun = nextGame;
							Task gameTask = Task.Run(() => gameToRun.Run(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
							_gameTasks.Add(gameTask); // Add the game task to the list of tasks
							_games.Add(gameToRun); // Add the game to the list of games

							_nextGames[gameName] = new PlaceholderGame(this); // Assign a new placeholder game for the next game to be used when the server starts
						}
						else
						{
							foreach (TcpClient player in gamePlayers)
							{
								lobby.Add(player); // Add the player back to the lobby if they were not added to the game
								_gameClientIsIn.Remove(player); // Remove the player from the game tracking dictionary
							}
						}
					}
				}
				
			}
			// Wait for pending connection to complete
			Task.WaitAll(connectionTasks.ToArray(), 1000); // Wait for 1 second to allow pending connections to complete


			// Stop listening when shutting down
			_listener.Stop();
			Console.WriteLine("The server has been shut down.");
		}


		public async Task ShutdownAsync()
		{
			if (_isRunning)
			{
				_isRunning = false;
				Console.WriteLine("Shutting down the Game Server.");

				// Stop all game threads
				await StopGameTasksAsync();

				// Disconnect all clients in parallel
				// Better approach than using a for loop since it will not block the main thread since each client will be disconnected in a separate thread
				Parallel.ForEach(_clients, (client) =>
				{
					DisconnectClient(client, "Server is shutting down.");
				});
		

				Console.WriteLine("Server shutdown completed.");

				// Clear collections
				_clients.Clear();
				_gameLobbies.Clear();
				_gameClientIsIn.Clear();
				_games.Clear();
				_gameTasks.Clear();

				_cancellationTokenSource.Dispose();
			}
		}
		private async Task StopGameTasksAsync()
		{
			Console.WriteLine("Stopping all game tasks ...");

			// Signal cancellation to all game threads
			_cancellationTokenSource.Cancel();

			try
			{
				await Task.WhenAll(_gameTasks).WaitAsync(TimeSpan.FromSeconds(5)); // Wait for all game tasks to complete
			}
			catch (OperationCanceledException)
			{

			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error stopping game tasks: {ex.Message}");
			}

			Console.WriteLine("All game tasks have stopped.");
		}
		private async Task HandleNewConnectionAsync(TcpClient client)
		{
			// Log the new connection
			string clientEndpoint = client.Client.RemoteEndPoint.ToString();
			Console.WriteLine($"New connection from {clientEndpoint}");


			// Store the client in the list
			_clients.Add(client);

			// Send welcome packet
			string welcomeMessage = $"Welcome to {_name} Game Server. Send a game selection packet (e.g., 'game:Placeholder Game').";
			Packet welcomePacket = new Packet("welcome", welcomeMessage);
			await SendPacketAsync(client, welcomePacket);

			Packet gamePacket = await ReceivePacketAsync(client); // Wait for the client to send a 'game' packet so we know which game to assign to the client
			if (gamePacket != null && gamePacket.Command == "game" && !string.IsNullOrEmpty(gamePacket.Message))
			{
				string gameName = gamePacket.Message;
				if (_gameLobbies.ContainsKey(gameName))
				{
					_gameLobbies[gameName].Add(client); // Add the client to the game lobby
					Console.WriteLine($"Client {clientEndpoint} joined {gameName} lobby.");
				}
				else
				{
					Console.WriteLine($"Client {clientEndpoint} selected unkown game: {gameName}");
					DisconnectClient(client, "Unknown game selected.");
				}
			}
			else
			{
				Console.WriteLine($"Client {clientEndpoint} failed to select a game.");
				DisconnectClient(client, "Invalid game selection.");
			}
		}
		private async Task SendPacketAsync(TcpClient client, Packet packet)
		{
			try
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
			catch (Exception ex)
			{
				string clientEndpoint = client.Client.RemoteEndPoint.ToString();
				Console.WriteLine($"Error sending packet to {clientEndpoint}: {ex.Message}");
				HandleDisconnectedClient(client); // Handle disconnection if an error occurs
			}
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
				if (bytesRead != 2)
				{
					return null;
				}

				ushort packetLength = BitConverter.ToUInt16(lengthBytes, 0); // This contains the length as a ushort of the JSON packet

				// Read JSON bytes
				byte[] jsonBytes = new byte[packetLength]; // We use here the length prefix received to create the array with the right size
				bytesRead = await stream.ReadAsync(jsonBytes, 0, jsonBytes.Length); // We reuse the same variable to read the JSON bytes and confirm all bytes are read
				if (bytesRead != packetLength) // If sizes don't match we return null since something went wrong
				{
					return null;
				}

				// Decode and deserialize the JSON
				string jsonString = Encoding.UTF8.GetString(jsonBytes);
				Packet packet = Packet.FromJson(jsonString);
				return packet; // The object deserialized where we can access the command and message properties
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
			
			foreach (List<TcpClient> lobby in _gameLobbies.Values)
			{
				lobby.Remove(client);
			}
			_gameClientIsIn.Remove(client); // Remove the client from the game tracking dictionary

			// Clean up resources
			client.GetStream().Close();
			client.Close();

		}
		private static async void HandleCtrlC(object sender, ConsoleCancelEventArgs args)
		{
			// Prevent default Ctrl+C behavior (immediate termination)
			args.Cancel = true;

			if (_serverInstance != null)
			{
				await _serverInstance.ShutdownAsync();
			}
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
