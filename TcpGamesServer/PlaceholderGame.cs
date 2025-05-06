using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TcpGames.Common;

namespace TcpGamesServer
{
	class PlaceholderGame : IGame
	{
		private readonly List<TcpClient> _players;
		private readonly GamesServer _server;

		public string Name => "Placeholder Game";
		public int RequiredPlayers => 2; // Arbitrary number of players required to start the game for testing


		public PlaceholderGame(GamesServer server)
		{
			_players = new List<TcpClient>();
			_server = server;
		}


		public bool AddPlayer(TcpClient player)
		{
			if (_players.Count < RequiredPlayers)
			{
				_players.Add(player);
				Console.WriteLine($"Player added to {Name}. Total players: {_players.Count}");
				return true;
			}
			return false;
		}

		public void DisconnectClient(TcpClient player)
		{
			_players.Remove(player);
			Console.WriteLine($"Player disconnected from {Name}. Remainig players: {_players.Count}");
		}

		public void Run(CancellationToken cancellationToken)
		{
			Console.WriteLine($"Running {Name} with {_players.Count} players.");

			try
			{
				// Simulate game loop logic
				for (int i = 0; i < 10; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					Console.WriteLine($"Game round {i + 1} in {Name}.");
					// Simulate some game logic here
					Thread.Sleep(1000); // Simulate time taken for a game round
				}
				Console.WriteLine($"{Name} completed.");
			}
			catch (OperationCanceledException)
			{
				Console.WriteLine($"{Name} cancelled.");
			}
		}
	}
}
