namespace Bloxstrap.Utility
{
    internal static class Http
    {
        public static async Task<T> GetJson<T>(Uri url)
        {
            using HttpResponseMessage response = await App.HttpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await ReadJson<T>(response);
        }

        public static async Task<T> SendJson<T>(HttpRequestMessage requestMessage)
        {
            using HttpResponseMessage response = await App.HttpClient.SendAsync(requestMessage);

            response.EnsureSuccessStatusCode();

            return await ReadJson<T>(response);
        }

        public static async Task<T> AuthGetJson<T>(Uri url)
        {
            using HttpResponseMessage response = await App.Cookies.AuthGet(url);

            response.EnsureSuccessStatusCode();

            return await ReadJson<T>(response);
        }

        public static async Task<T> AuthSendJson<T>(HttpRequestMessage requestMessage)
        {
            HttpContent content = requestMessage.Content!;

            using HttpResponseMessage response = await App.Cookies.AuthPost(requestMessage.RequestUri, content);

            response.EnsureSuccessStatusCode();

            return await ReadJson<T>(response);
        }

        private static async Task<T> ReadJson<T>(HttpResponseMessage response)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync();

            return (await JsonSerializer.DeserializeAsync<T>(stream))!;
        }
    }
}
