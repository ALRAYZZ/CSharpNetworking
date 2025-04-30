using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpGames.Common
{
    public interface IGame
    {
		#region Properties
		// Name of the game
		string Name { get; }
        int RequiredPlayers { get; }
		#endregion Properties

		#region Functions
		bool AddPlayer(TcpClient player);
        void DiconnectClient(TcpClient client);
        void Run();
		#endregion Functions
	}
}
