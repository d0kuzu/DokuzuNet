using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DokuzuNet.Networking
{
    public class Packet
    {
        public string Type { get; set; } = string.Empty;
        public object? Data { get; set; }

        public Packet() { }

        public Packet(string type, object? data = null)
        {
            Type = type;
            Data = data;
        }

        // Сериализация в JSON
        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        // Десериализация из JSON
        public static Packet? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Packet>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
