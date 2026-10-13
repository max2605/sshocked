using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IForwardingProcessFactory
{
    IForwardingProcess Start(ProcessStartInfo info, ForwardingProfile profile);
}
