using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Integration
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute { }
}
