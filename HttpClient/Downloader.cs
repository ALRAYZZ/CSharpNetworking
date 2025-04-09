using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HttpClientProject
{
    class Downloader
    {
        public static string urlToDownload = "https://16bpp.net";
        public static string fileName = "index.html";


        public static async Task DownloadWebPage()
        {
			Console.WriteLine("Starting Download...");

            // Setup the HttpClient
            using (HttpClient httpClient = new HttpClient())
            {
				// Get the webpage asynchronously
				HttpResponseMessage response = await httpClient.GetAsync(urlToDownload);

				// If we get a 200 response, then save it
				if (response.IsSuccessStatusCode)
				{
					Console.WriteLine("Got it ...");

					// Get the data
					byte[] data = await response.Content.ReadAsByteArrayAsync();

					// Save the data to a file
					using (FileStream fileStream = File.Create(fileName))
					{
						await fileStream.WriteAsync(data, 0, data.Length);
					}

					Console.WriteLine("Done!");
				}
			}
		}

		public static async Task Main(string[] args)
		{
			await DownloadWebPage();
			Console.WriteLine("Download complete!");
		}
	}
}
