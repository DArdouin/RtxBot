using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Html.Dom;
using Newtonsoft.Json;

namespace RtxBot
{
    class Program
    {
        const string PBAccessToken = "<YourPushBulletAccessTokenHere>";

        enum State
        {
            Unknown,
            OutOfStock,
            InStock
        }

        private static FileLogger _fileLogger = new FileLogger();

        static async Task Main(string[] args)
        {
            using var pbClient = new PushBulletClient();
            var histories = new Dictionary<Uri, State>();
            const int loopDelayInSeconds = 10;

            await _fileLogger.LogAsync("Starting");

            while (true)
            {
                var config = Configuration.Default.WithDefaultLoader();
                var context = BrowsingContext.New(config);
                var document = await context.OpenAsync("https://www.newegg.ca/p/pl?d=3080&N=100007708&isdeptsrh=1");
                var itemContainers = document.QuerySelectorAll("div.item-cell div.item-container");

                foreach (var itemContainer in itemContainers)
                {
                    try
                    {
                        var itemInfo = itemContainer.Children.First(x => x.ClassName == "item-info");
                        var itemPromo = (IHtmlParagraphElement)itemInfo.Children.First(x => x.ClassName == "item-promo");
                        var itemTitle = (IHtmlAnchorElement)itemInfo.Children.First(x => x.ClassName == "item-title");

                        var newState = string.Equals(itemPromo.TextContent, "OUT OF STOCK", StringComparison.InvariantCultureIgnoreCase) ? State.OutOfStock : State.InStock;
                        var uri = new UriBuilder(itemTitle.HostName, itemTitle.PathName).Uri;

                        if (histories.ContainsKey(uri) && histories[uri] != newState && newState != State.OutOfStock)
                        {
                            await _fileLogger.LogAsync($"New state for {itemTitle.InnerHtml} : {newState}");
                            await pbClient.NotifyAsync(uri.ToString());
                        }

                        histories[uri] = newState;
                    }
                    catch (Exception e)
                    {
                        await _fileLogger.LogAsync(e);
                    }
                }

                await Task.Delay(loopDelayInSeconds * 1000);

                var outOfStock = histories.Count(x => x.Value == State.OutOfStock);
                await _fileLogger.LogAsync($"{DateTime.Now} - Out of stock : {outOfStock} - In stock {histories.Count - outOfStock}");
            }
        }

        ~Program()
        { 
            _fileLogger.LogAsync("Stopped").Wait();
        }

        private class PushBulletClient : IDisposable
        {
            private readonly HttpClient _client;

            public PushBulletClient()
            {
                _client = new HttpClient();
            }

            public async Task NotifyAsync(string gpuUri)
            {
                var json = JsonConvert.SerializeObject(new Push(gpuUri));

                using var req = new StringContent(json, Encoding.UTF8, "application/json");
                req.Headers.Add("Access-Token", PBAccessToken);

                var resp = await _client.PostAsync("https://api.pushbullet.com/v2/pushes", req);
            }

            public void Dispose()
            {
                _client.Dispose();
            }

            private class Push
            {
                [JsonProperty("url")]
                public string Url { get; }

                [JsonProperty("title")]
                public string Title => "New rtx available";

                [JsonProperty("type")]
                public string Type => "link";

                [JsonProperty("device_iden")]
                public string DeviceId => "ujx7FvHZyoKsjAsoeMFET6";

                public Push(string uri)
                {
                    Url = uri;
                }
            }
        }

        private class FileLogger
        {
            public async Task LogAsync(string text)
            {
                await LogTextAsync(text);
            }

            public async Task LogAsync(Exception e)
            {
                await LogTextAsync($"!!!! Exception : {e.Message}");
            }

            private static async Task LogTextAsync(string text)
            {
                var fi = new FileInfo($"/home/nodral/rtx-bot/logs");
                await using var fileWriter = fi.AppendText();

                await fileWriter.WriteLineAsync(text);

                fileWriter.Close();
            }
        }
    }
}
