using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpSingleClientChatClient
{
	class ChatClientSC
	{
		private readonly TcpClient _client;
		private NetworkStream? _stream;
		public readonly string ServerAddress;
		public readonly int Port;
		public readonly string Name;
		public bool Running { get; private set; }
		public const int BufferSize = 2 * 1024;

		public ChatClientSC(string serverAddress, int port, string name)
		{
			ArgumentNullException.ThrowIfNullOrEmpty(serverAddress);
			ArgumentNullException.ThrowIfNullOrEmpty(name);

			ServerAddress = serverAddress;
			Port = port;
			Name = name;
			_client = new TcpClient
			{
				SendBufferSize = BufferSize,
				ReceiveBufferSize = BufferSize
			};
			Running = false;
		}

		public async Task ConnectAsync()
		{
			try
			{
				await _client.ConnectAsync(ServerAddress, Port);
				if (_client.Connected)
				{
					_stream = _client.GetStream();
					byte[] nameBuffer = Encoding.UTF8.GetBytes($"name:{Name}");
					await _stream.WriteAsync(nameBuffer);

					// Check for welcome message
					byte[] buffer = new byte[BufferSize];
					int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
					if (bytesRead > 0)
					{
						Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, bytesRead));
						Running = true;
						Console.WriteLine("Type 'quit' to exit.");
					}
					else
					{
						Console.WriteLine("Server rejected the connection.");
						Cleanup();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Connection failed: {ex.Message}");
				Cleanup();
			}
		}

		public async Task RunAsync()
		{
			if (!Running) return;

			// Start receiving messages in the background
			var receiveTask = Task.Run(async () =>
			{
				while (Running)
				{
					try
					{
						int messageLength = _client.Available;
						if (messageLength > 0)
						{
							byte[] buffer = new byte[messageLength];
							await _stream!.ReadAsync(buffer, 0, messageLength);
							Console.WriteLine(Encoding.UTF8.GetString(buffer));
						}
						if (_isDisconnected())
						{
							Running = false;
							Console.WriteLine("Disconnected from server.");
						}
						await Task.Delay(10);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error receiving: {ex.Message}");
						Running = false;
					}
				}
			});

			// Handle sending messages
			while (Running)
			{
				Console.Write($"{Name}> ");
				string? message = Console.ReadLine();
				if (string.IsNullOrEmpty(message))
					continue;

				if (message.ToLower() is "quit" or "exit")
				{
					Running = false;
					break;
				}

				try
				{
					byte[] buffer = Encoding.UTF8.GetBytes(message);
					await _stream!.WriteAsync(buffer);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error sending: {ex.Message}");
					Running = false;
				}
				await Task.Delay(10);
			}

			await receiveTask;
			Cleanup();
			Console.WriteLine("Disconnected.");
		}

		private void Cleanup()
		{
			Running = false;
			try
			{
				_stream?.Dispose();
				_client.Dispose();
			}
			catch
			{
				// Ignore cleanup errors
			}
		}

		private bool _isDisconnected()
		{
			try
			{
				return _client.Client.Poll(1000, SelectMode.SelectRead) && _client.Available == 0;
			}
			catch
			{
				return true;
			}
		}

		public static async Task Main(string[] args)
		{
			Console.Write("Enter your name: ");
			string? name = Console.ReadLine();
			if (string.IsNullOrEmpty(name))
			{
				Console.WriteLine("Name cannot be empty.");
				return;
			}

			var client = new ChatClientSC("localhost", 8000, name);
			await client.ConnectAsync();
			await client.RunAsync();
		}
	}
}