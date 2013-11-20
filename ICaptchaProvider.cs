using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public interface ICaptchaProvider
    {
        Task<string> GetCaptchaResponse(string captchaIden);
    }
}
