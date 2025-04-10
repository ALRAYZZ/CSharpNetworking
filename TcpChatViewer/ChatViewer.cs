using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpChatViewer
{
    class ChatViewer
    {
        public readonly string ServerAddress;
        public readonly int Port;
        private readonly TcpClient _client;
        public bool Running { get; private set; }
        private bool _disconnectRequested;

        // Buffer & messaging
        public const int BufferSize = 2 * 1024; // 2KB
        private NetworkStream? _msgStream;

        public ChatViewer(string serverAddress, int port)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(serverAddress, nameof(serverAddress));

            // Create a non-connected TcpClient
            _client = new TcpClient()
            {
                SendBufferSize = BufferSize,
                ReceiveBufferSize = BufferSize,
            };
            Running = false;
            _disconnectRequested = false;

            ServerAddress = serverAddress;
            Port = port;
        }

        public async Task ConnectAsync()
        {
            try
            {
                await _client.ConnectAsync(ServerAddress, Port).ConfigureAwait(false);
                var endPoint = _client.Client.RemoteEndPoint;

                if (_client.Connected)
                {
					Console.WriteLine($"Connected to the server at {endPoint}");

					// Tell server we are a "viewer"
					_msgStream = _client.GetStream(); // Getting the stream to the server
					byte[] msgBuffer = Encoding.UTF8.GetBytes("viewer"); // This is the format the server expects to decide we are a viewer
                    await _msgStream.WriteAsync(msgBuffer).ConfigureAwait(false); // Send the message to the server

                    // Check that we are still connected
                    if (!IsDisconnected(_client))
                    {
                        Running = true;
						Console.WriteLine("Press Ctrl-C to exit the Viewer at any time.");
					}
                    else
                    {
                        CleanupNetworkResource();
						Console.WriteLine("The server didn't recognise us as Viewer.");
                    }
                }
                else
                {
                    CleanupNetworkResource();
					Console.WriteLine($"Wasn't able to connect to {endPoint}");
				}
			}
            catch (SocketException ex)
            {
				Console.WriteLine($"Connection failed: {ex.Message}");
                CleanupNetworkResource();
            }
        }
        // Requests a disconnect
        public void Disonnect()
        {
            Running = false;
			_disconnectRequested = true;
			Console.WriteLine("Disconnecting from the chat ...");
		}
		// Main loop, listens for messages from the server and prints them to the console
		public async Task ListenForMessagesAsync()
        {
            bool wasRunning = Running;
            if (!Running) return;

            while (Running)
            {
                int messageLength = _client.Available; // Sets the message length to the number of bytes available received
                if (messageLength > 0)
                {
                    byte[] msgBuffer = new byte[messageLength]; // Create a buffer to hold the message
                    await _msgStream!.ReadAsync(msgBuffer).ConfigureAwait(false); // Read the message from the client. Blocks until the message is received

                    string msg = Encoding.UTF8.GetString(msgBuffer); // Convert the message to a string
                    Console.WriteLine(msg); // Write on console the message received
                }

                if (IsDisconnected(_client))
                {
                    Running = false;
                    Console.WriteLine("Server has disconnected from us.");
                }

                Running &= !_disconnectRequested; // Keep running until we are disconnected

                await Task.Delay(10).ConfigureAwait(false);
			}

            CleanupNetworkResource();
            if (wasRunning) Console.WriteLine("Disonnected.");
		}
		private void CleanupNetworkResource()
		{
            _msgStream?.Dispose();
            _msgStream = null;
            _client.Dispose();
		}

		private bool IsDisconnected(TcpClient client)
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

        public static ChatViewer? Viewer;
        private static void InterruptHandler(object? sender, ConsoleCancelEventArgs args)
        {
            Viewer?.Disonnect();
            args.Cancel = true; // Cancel the default behavior of the interrupt
		}

        public static async Task Main(string[] args)
        {
            string host = "localhost"; // Default hosts
            int port = 8000;
            Viewer = new ChatViewer(host, port); // Create the viewer

			Console.CancelKeyPress += InterruptHandler;

            await Viewer.ConnectAsync(); // Connect to the server
            await Viewer.ListenForMessagesAsync(); // Listen for messages from the server
		}
	}
}
