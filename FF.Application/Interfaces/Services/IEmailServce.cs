using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF.Application.Interfaces.Services
{
    public interface IEmailService
    {
        Task SendWarRoomBriefAsync(
            string toEmail,
            string subject,
            string htmlBody,
            CancellationToken ct = default);
    }
}
