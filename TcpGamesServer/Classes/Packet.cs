using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TcpGamesServer.Classes
{
    class Packet
    {
		// This decorators ensure correct serialization and deserialization of the properties with known Key names
		[JsonProperty("command")]
		public string Command { get; set; } = string.Empty;

		[JsonProperty("message")]
        public string Message { get; set; } = string.Empty; // Can be empty but not null

		// Making a packet in the constructor
		public Packet(string command, string message)
		{
			Command = command;
			Message = message;
		}

		public override string ToString()
		{
			return $"[Packet:\n Command='{Command}'\n Message='{Message}']";
		}

		public string ToJson()
		{
			return JsonConvert.SerializeObject(this);
		}

		public static Packet FromJson(string jsonData)
		{
			if (string.IsNullOrEmpty(jsonData))
			{
				throw new ArgumentNullException(nameof(jsonData));
			}
			try
			{
				var packet = JsonConvert.DeserializeObject<Packet>(jsonData);
				return packet ?? throw new InvalidOperationException("Deserialization returned null.");
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("Failed to deserialize JSON.", ex);
			}
		}
	}
}
