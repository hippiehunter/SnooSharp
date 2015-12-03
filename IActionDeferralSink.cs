using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public interface IActionDeferralSink
    {
        void Defer(Dictionary<string, string> arguments, string action, string verb);
        Tuple<Dictionary<string, string>, string, string> DequeDeferral();
    }
}
