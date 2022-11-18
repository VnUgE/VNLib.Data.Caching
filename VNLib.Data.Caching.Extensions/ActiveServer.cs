using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.Extensions
{
    public class ActiveServer
    {
        [JsonPropertyName("address")]
        public string? HostName { get; set; }
        [JsonPropertyName("server_id")]
        public string? ServerId { get; set; }
        [JsonPropertyName("ip_address")]
        public string? Ip { get; set; }
    }
}
