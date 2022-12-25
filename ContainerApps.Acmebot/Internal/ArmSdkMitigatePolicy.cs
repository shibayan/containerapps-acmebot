using System.IO;
using System.Text;

using Azure.Core;
using Azure.Core.Pipeline;

namespace ContainerApps.Acmebot.Internal;

internal class ArmSdkMitigatePolicy : HttpPipelineSynchronousPolicy
{
    public override void OnReceivedResponse(HttpMessage message)
    {
        if (message.Response.ContentStream is null)
        {
            return;
        }

        using var reader = new StreamReader(message.Response.ContentStream, leaveOpen: true);
        var content = reader.ReadToEnd().Replace("outboundIpAddresses", "outboundIpAddresses_");
        var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes(content));
        message.Response.ContentStream = stream;
        message.Response.ContentStream.Position = 0;
    }
}
