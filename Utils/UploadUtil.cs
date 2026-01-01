// Jarfix/Utils/UploadUtil.cs
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jarfix.Utils
{
    public static class UploadUtil
    {
        private static readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        public static async Task<JsonElement?> UploadLogAsync(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Log file not found", path);
            var contentText = await File.ReadAllTextAsync(path, Encoding.UTF8);

            var values = new[]
            {
                new KeyValuePair<string, string>("content", contentText)
            };

            using var form = new FormUrlEncodedContent(values);
            using var resp = await http.PostAsync("https://api.mclo.gs/1/log", form);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();

            var doc = JsonDocument.Parse(body);
            return doc.RootElement;
        }
    }
}
