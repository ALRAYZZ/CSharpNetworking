using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpChatServer
{
    class ChatServer
    {
		// What listens incoming connections
		private TcpListener _listener;
		// Types of clients connected
		private List<TcpClient> _viewers = new List<TcpClient>();
		private List<TcpClient> _messengers = new List<TcpClient>();
		// Names that are taken by other messengers
		private Dictionary<TcpClient, string> _names = new Dictionary<TcpClient, string>();
		// Messages that need to be sent
		private Queue<string> _messageQueue = new Queue<string>();
		// Extra fun data
		public readonly string ChatName;
		public readonly int Port;
		public bool Running { get; private set; }
		// Buffer
		public readonly int BufferSize = 2 * 1024; // 2KB

		// Constructor, make a new TCP chat server with our provided name and port
		public ChatServer(string chatName, int port)
		{
			// Set basic data
			ChatName = chatName ?? throw new ArgumentNullException(nameof(chatName));
			Port = port;
			Running = false;

			// Make the listener listen for connection on any network device
			_listener = new TcpListener(IPAddress.Any, port);
		}

		// If the server is running, this will shut down it
		public void Shutdown()
		{
			Running = false;
			Console.WriteLine("Shutting down server");
		}

		// Start the server
		public void Run()
		{
			// Some info
			Console.WriteLine($"Starting the \"{ChatName}\" TCP Chat Server on port {Port}.");
			Console.WriteLine("Press Ctrl-C to shut down the server at any time.");

			_listener.Start();
			Running = true;

			while (Running)
			{
				// Check for new clients
				if (_listener.Pending())
				{
					_handleNewConnection();
				}

				// Lively check for disconnects and messages
				_checkForDisconnects();
				_checkForNewMessages();
				_sendMessages();

				Thread.Sleep(10);
			}

			// Stop the server, and clean up any connected clients
			foreach (TcpClient v in _viewers.ToArray()) 
				_cleanupClient(v);
			foreach (TcpClient m in _messengers.ToArray())
				_cleanupClient(m);
			_listener.Stop();

			Console.WriteLine("Server is shutting down");
		}
		private void _handleNewConnection()
		{
			TcpClient newClient = _listener.AcceptTcpClient(); // Block until a new client connects
			NetworkStream netStream = newClient.GetStream(); // Get the stream to read/write data to that client

			// Modifying default buffer sizes
			newClient.SendBufferSize = BufferSize;
			newClient.ReceiveBufferSize = BufferSize;

			// Get the end point of the client connected
			EndPoint? endPoint = newClient.Client.RemoteEndPoint;
			Console.WriteLine($"Handling a new client from {endPoint}");

			byte[] msgBuffer = new byte[BufferSize]; // This holds the message we will receive from the client
			// Wait for the client to send us a "viewer" or a "messenger" signal.
			int bytesRead = netStream.Read(msgBuffer, 0, msgBuffer.Length); // .Read is blocking, so it will wait until the client sends something
			if (bytesRead <= 0) // If nothing is received we assume client is disconnected
			{
				return;
			}

			// Convert the byte array to a string, we need the bytesRead to know how many bytes to convert
			string msg = Encoding.UTF8.GetString(msgBuffer, 0, bytesRead); 

			bool good = false;
			if (msg == "viewer") // Our own defines rules, where client has to send "viewer"
			{
				good = true;
				_viewers.Add(newClient);
				Console.WriteLine($"{endPoint} is a Viewer");

				msg = $"Welcome to the \"{ChatName}\" chat server! You are a viewer. You can only see messages, not send them."; // Create a message for the new client
				msgBuffer = Encoding.UTF8.GetBytes(msg); // Encode the message to bytes
				netStream.Write(msgBuffer, 0, msgBuffer.Length); // Send the message to the client
			}
			else if (msg.StartsWith("name")) // Own rule, where client has to send "name" and then their name
			{
				string name = msg["name:".Length..]; // We get the name by removing the "name:" part of the string
				if (!string.IsNullOrEmpty(name) && !_names.ContainsValue(name)) // Checking if its not empty or if is not already been used.
				{
					good = true;
					_names.Add(newClient, name); // Map the client to their name
					_messengers.Add(newClient);
					Console.WriteLine($"{endPoint} is a Messenger with the name {name}.");
					_messageQueue.Enqueue($"[{name}] has joined the chat.");
				}
			}
			else
			{
				Console.WriteLine($"Wasn't able to identify {endPoint} as a Viewer or Messenger.");
				_cleanupClient(newClient);
			}
			if (!good)
			{
				newClient.Close();
			}
		}
		// Sees if any of the clients have disconnected
		private void _checkForDisconnects()
		{
			// Check the viewers first
			foreach (TcpClient v in _viewers.ToArray()) // We use ToArray() to avoid modifying the collection while iterating or we would get an exception
			{
				if (_isDisconnected(v))
				{
					Console.WriteLine($"Viewer {v.Client.RemoteEndPoint} has left.");
					_viewers.Remove(v);  // Since we are modifying the collection, we create a "snapshot" using the .ToArray() method so we can remove items.
					_cleanupClient(v);
				}
			}

			// Check the messengers
			foreach (TcpClient m in _messengers.ToArray())
			{
				if (_isDisconnected(m))
				{
					string name = _names[m];
					// Tell the viewers someone has left
					Console.WriteLine($"Messenger {m.Client.RemoteEndPoint} has left.");
					_messageQueue.Enqueue($"{name} has left the chat.");

					// Clean up on our end
					_messengers.Remove(m);
					_names.Remove(m); // Remove taken name
					_cleanupClient(m);
				}
			}
		}

		// See if any of our messengers have sent us a message, put it in the queue
		private void _checkForNewMessages()
		{
			foreach (TcpClient m in _messengers.ToArray())
			{
				int messageLength = m.Available; // Check if there is any data available to read
				if (messageLength > 0)
				{
					// There is one! Get it!
					byte[] msgBuffer = new byte[messageLength]; // Create a buffer to hold the message
					m.GetStream().Read(msgBuffer, 0, messageLength); // Read the message from the client. Blocks until the message is received

					// Attach the name to the message and add it to the queue
					string msg = $"{_names[m]}: {Encoding.UTF8.GetString(msgBuffer)}"; // Convert the message to a string
					_messageQueue.Enqueue(msg); // Add the message to the queue
				}
			}
		}
		// Clears out the message queue and sends the messages to all viewers
		private void _sendMessages()
		{
			if (_messageQueue.Count == 0) return; // If there are no messages to send, we don't need to do anything

			foreach (string msg in _messageQueue)
			{
				byte[] msgBuffer = Encoding.UTF8.GetBytes(msg); // Encode the message to bytes
				foreach (TcpClient v in _viewers.ToArray()) // For each viewer, we send the message
				{
					v.GetStream().Write(msgBuffer, 0, msgBuffer.Length); // Send the message to the viewers only
				}
			}

			_messageQueue.Clear(); // Clear the queue after sending all messages
		}
		// Check if the socket is disconnected
		private static bool _isDisconnected(TcpClient client)
		{
			try
			{
				Socket s = client.Client;
				return s.Poll(10 * 1000, SelectMode.SelectRead) && s.Available == 0;
			}
			catch (SocketException)
			{
				// If we get a SocketException, asume the client is disconnected
				return true;
			}
		}
		private static void _cleanupClient(TcpClient client)
		{
			client.GetStream().Close();
			client.Close();
		}

		public static ChatServer? Chat;

		private static void InterruptHandler(object? sender, ConsoleCancelEventArgs args)
		{
			Chat?.Shutdown(); // Gracefully shut down the server following our own rules
			args.Cancel = true; // Cancel the default behavior of the interrupt
		}

		public static void Main(string[] args)
		{
			// Create the server
			string name = "Bad IRC";
			int port = 8000;
			Chat = new ChatServer(name, port);

			// Add a handler for a Ctrl-C interrupt
			Console.CancelKeyPress += InterruptHandler; // Register the interrupt handler
			Chat.Run(); // Start the server
		}
	}
}
