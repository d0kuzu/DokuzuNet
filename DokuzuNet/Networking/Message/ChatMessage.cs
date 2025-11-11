using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Networking.Message
{
    public record ChatMessage(string Text) : IMessage;
}
