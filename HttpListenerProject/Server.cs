using System.Net;
using System.Text;

namespace HttpListenerProject
{
    class Server
    {
        public static HttpListener listener = new HttpListener();
		public static string url = "http://localhost:8000/";
        public static string pageData =
			 "<!DOCTYPE>" +
			"<html>" +
			"  <head>" +
			"    <title>HttpListener Example</title>" +
			"  </head>" +
			"  <body>" +
			"    <p>Page Views: {0}</p>" +
			"    <form method=\"post\" action=\"shutdown\">" +
			"      <input type=\"submit\" value=\"Shutdown\" {1}>" +
			"    </form>" +
			"  </body>" +
			"</html>";

		public static async Task HandleIncomingConnections(HttpListener listener, CancellationToken cancellationToken)
        {
			int requestCount = 0, pageViews = 0;
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					HttpListenerContext context = await listener.GetContextAsync();
					requestCount++;

					try
					{
						HttpListenerRequest request = context.Request;
						using HttpListenerResponse response = context.Response;

						Console.WriteLine($"Request #{requestCount}");
						Console.WriteLine(request.Url.ToString());
						Console.WriteLine(request.HttpMethod);
						Console.WriteLine(request.UserHostName);
						Console.WriteLine(request.UserAgent);
						Console.WriteLine();

						// Shutdown logic
						bool shutdown = request.HttpMethod == "POST" && request.Url.AbsolutePath == "/shutdown";
						if (shutdown)
						{
							Console.WriteLine("Shutdown requested");
						}

						// Increment page views
						if (request.Url.AbsolutePath != "/favicon.ico")
						{
							pageViews++;
						}

						// Prepare the response and send
						string disableSubmit = shutdown ? "disabled" : "";
						byte[] data = Encoding.UTF8.GetBytes(string.Format(pageData, pageViews, disableSubmit));

						response.ContentType = "text/html";
						response.ContentLength64 = data.Length;
						response.ContentEncoding = Encoding.UTF8;

						await response.OutputStream.WriteAsync(data, 0, data.Length, cancellationToken);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error processing request: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Server error: {ex.Message}");
			}
			
		}
		public static async Task Main(string[] args)
		{
			listener.Prefixes.Add(url);
			listener.Start();
			Console.WriteLine("Server started...");
			await HandleIncomingConnections(listener, CancellationToken.None);
			listener.Stop();
		}
	}
}
