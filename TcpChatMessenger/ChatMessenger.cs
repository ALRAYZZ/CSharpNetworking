using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace TcpChatMessenger
{
    class ChatMessenger
    {
        // Connection objects
        public readonly string ServerAddress;
        public readonly int Port;
        private readonly TcpClient _client;
        public bool Running { get; private set; }

        // Buffer & messaging
        public const int BufferSize = 2 * 1024; // 2KB
        private NetworkStream? _msgStream;

        // Personal data
        public readonly string Name;

        public ChatMessenger(string serverAddress, int port, string name)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(serverAddress, nameof(serverAddress));
			ArgumentNullException.ThrowIfNullOrEmpty(name, nameof(name));

			// Create a non-connected TcpClient
			_client = new TcpClient()
            {
				SendBufferSize = BufferSize,
				ReceiveBufferSize = BufferSize
			};
			Running = false;

			ServerAddress = serverAddress;
			Port = port;
			Name = name;
		}

        public async Task ConnectAsync()
        {
			try
			{
				// Try to connect to the server
				await _client.ConnectAsync(ServerAddress, Port).ConfigureAwait(false); // Block until connected
				var endPoint = _client.Client.RemoteEndPoint;

				// Make sure we connected
				if (_client.Connected)
				{
					// Got in!
					Console.WriteLine($"Connected to the server at {endPoint}");

					// Tell server we are a "messenger"
					_msgStream = _client.GetStream(); // Getting the stream to the server
					byte[] msgBuffer = Encoding.UTF8.GetBytes($"name:{Name}"); // This is the format the server expects to decide we are a messenger
					await _msgStream.WriteAsync(msgBuffer).ConfigureAwait(false); // Block until sent

					// If we are still connected after sending our name, we are good to go
					if (!IsDisconnected(_client))
					{
						Running = true;
					}
					else
					{
						// Name was probably taken
						CleanupNetworkResource();
						Console.WriteLine($"The server rejected us; {Name} is probably in use.");
					}
				}
				else
				{
					CleanupNetworkResource();
					Console.WriteLine($"Wasn't able to connect to the server at {endPoint}.");
				}
			}
			catch (SocketException ex)
			{
				Console.WriteLine($"Connection failed: {ex.Message}");
				CleanupNetworkResource();
			}
        }
		public async Task SendMessagesAsync()
		{
			bool wasRunning = Running;
			if (!Running) return;

			// Main loop for sending messages
			while (Running)
			{
				// Poll for user input
				Console.Write($"{Name}>");
				string msg = Console.ReadLine() ?? string.Empty;

				if (string.IsNullOrEmpty(msg))
				{
					continue; // Skip empty messages
				}

				if (msg.ToLower() is "quit" or "exit")
				{
					Console.WriteLine("Disconnecting...");
					Running = false;
				}
				else
				{
					// Send the mesage after an empty check and a "quit" check
					byte[] msgBuffer = Encoding.UTF8.GetBytes(msg);
					await _msgStream.WriteAsync(msgBuffer).ConfigureAwait(false); // Block until sent
				}

				if (IsDisconnected(_client))
				{
					Running = false;
					Console.WriteLine("Server has disconnected from us.");
				}

				await Task.Delay(10).ConfigureAwait(false); // Give the CPU a break less CPU usage than Thread.Sleep(10)
			}

			CleanupNetworkResource();
			if (wasRunning)
			{
				Console.WriteLine("Disconnected.");
			}
		}
		// Cleans any leftover network resources
		private void CleanupNetworkResource()
		{
			_msgStream?.Dispose(); // Dispose is the modern way to close a stream and free up resources
			_msgStream = null;
			_client.Dispose();
		}
		private static bool IsDisconnected(TcpClient client)
		{
			try
			{
				Socket s = client.Client;
				return s.Poll(10 * 1000, SelectMode.SelectRead) && s.Available == 0;
			}
			catch (SocketException)
			{
				return true;
			}
		}

		public static async Task Main(string[] args)
		{
			// Get a name
			Console.Write("Enter a name to use: ");
			string? name = Console.ReadLine();

			if (string.IsNullOrEmpty(name))
			{
				Console.WriteLine("Name cannot be empty.");
				return;
			}


			// Setup the messenger
			string host = "localhost"; // Default host
			int port = 8000; // Default port
			ChatMessenger messenger = new ChatMessenger(host, port, name);

			// Connect and send messages
			await messenger.ConnectAsync();
			await messenger.SendMessagesAsync();
		}
	}
}
