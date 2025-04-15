using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpSingleClientChatServer
{
    class ChatServerSC
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<TcpClient, string> _clients = new Dictionary<TcpClient, string>();
        private readonly Queue<string> _messageQueue = new Queue<string>();
        public readonly string ChatName;
        public readonly int Port;
        public bool Running { get; private set; }
		public readonly int BufferSize = 2 * 1024; // 2KB

        public ChatServerSC(string chatName, int port)
		{
			ChatName = chatName ?? throw new ArgumentNullException(nameof(chatName));
			Port = port;
			Running = false;
			_listener = new TcpListener(System.Net.IPAddress.Any, port);
		}

        public void Shutdown()
        {
			Running = false;
			Console.WriteLine("Shutting down server...");
		}

		public async Task RunAsync()
		{
			Console.WriteLine($"Starting \"{ChatName}\" TCP Chat Server on port {Port}");
			_listener.Start();
			Running = true;

			try
			{
				while (Running)
				{
					// Accept new clients asynchronously
					if (_listener.Pending())
					{
						await _handleNewConnectionAsync();
					}

					// Process messages and check for disconnects	
					await _checkForNewMessagesAsync();
					await _sendMessagesAsync();
					await _checkForDisconnectsAsync();

					await Task.Delay(10); // Avoid busy waiting
				}
			}
			finally
			{
				// Clean up on shutdown
				foreach (var client in _clients.Keys.ToArray())
				{
					_cleanupClient(client);
				}
				_listener.Stop();
				Console.WriteLine("Server shut down.");
			}
		}

		private async Task _handleNewConnectionAsync()
		{
			TcpClient client = await _listener.AcceptTcpClientAsync();
			NetworkStream stream = client.GetStream();
			client.SendBufferSize = BufferSize;
			client.ReceiveBufferSize = BufferSize;

			try
			{
				byte[] buffer = new byte[BufferSize];
				int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
				if (bytesRead <= 0)
				{
					_cleanupClient(client);
					return;
				}

				string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
				if (msg.StartsWith("name:") && msg.Length > "name:".Length)
				{
					string name = msg["name:".Length..].Trim();
					if (!string.IsNullOrEmpty(name) && !_clients.ContainsValue(name))
					{
						_clients[client] = name;
						Console.WriteLine($"{client.Client.RemoteEndPoint} joined as {name}");

						// Send welcome message
						string welcomeMessage = $"Welcome to the \"{ChatName}\" chat server, {name}!";
						await stream.WriteAsync(Encoding.UTF8.GetBytes(welcomeMessage));

						// Broadcast join
						_messageQueue.Enqueue($"[{name}] has joined the chat.");
						return;
					}
				}
				Console.WriteLine($"Rejected {client.Client.RemoteEndPoint}: Invalid or taken name.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error handling new connection: {ex.Message}");
			}
			_cleanupClient(client);
		}

		private async Task _checkForDisconnectsAsync()
		{
			foreach (var client in _clients.Keys.ToArray())
			{
				if (_isDisconnected(client))
				{
					string name = _clients[client];
					_messageQueue.Enqueue($"[{name}] has left the chat.");
					Console.WriteLine($"{name} at {client.Client.RemoteEndPoint} disconnected.");
					_clients.Remove(client);
					_cleanupClient(client);
				}
			}
		}

		private void _cleanupClient(TcpClient client)
		{
			try
			{
				client.GetStream()?.Dispose();
				client.Dispose();
			}
			catch
			{

			}
		}

		private bool _isDisconnected(TcpClient client)
		{
			try
			{
				return client.Client.Poll(1000, SelectMode.SelectRead) && client.Available == 0;
			}
			catch
			{
				return true;
			}
		}

		private async Task _checkForNewMessagesAsync()
		{
			foreach (var client in _clients.Keys.ToArray())
			{
				try
				{
					int messageLength = client.Available;
					if (messageLength > 0)
					{
						byte[] buffer = new byte[messageLength];
						await client.GetStream().ReadAsync(buffer, 0, messageLength);
						string message = Encoding.UTF8.GetString(buffer).Trim();
						if (!string.IsNullOrEmpty(message))
						{
							string formatted = $"{_clients[client]}: {message}";
							_messageQueue.Enqueue(formatted);
							Console.WriteLine($"Received: {formatted}");
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error reading from {client.Client.RemoteEndPoint}: {ex.Message}");
				}
			}
		}
		private async Task _sendMessagesAsync()
		{
			if (_messageQueue.Count == 0) return; // Nothing to send

			var messages = _messageQueue.ToArray();
			_messageQueue.Clear(); // Clear the queue after copying

			foreach (string msg in messages)
			{
				byte[] buffer = Encoding.UTF8.GetBytes(msg + "\n");
				foreach (var client in _clients.Keys.ToArray())
				{
					try
					{
						if (!_isDisconnected(client))
						{
							await client.GetStream().WriteAsync(buffer);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error sending to {client.Client.RemoteEndPoint}: {ex.Message}");
					}
				}
			}
		}
		public static async Task Main(string[] args)
		{
			var server = new ChatServerSC("Simple Chat", 8000);
			Console.CancelKeyPress += (sender, e) =>
			{
				e.Cancel = true; // Prevent the process from terminating immediately
				server.Shutdown();
			};
			await server.RunAsync();
		}
	}
}
