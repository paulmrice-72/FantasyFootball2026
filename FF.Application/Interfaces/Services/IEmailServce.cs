namespace FF.Application.Interfaces.Services
{
    public interface IEmailService
    {
        Task SendWarRoomBriefAsync(
            string toEmail,
            string subject,
            string htmlBody,
            CancellationToken ct = default);

        Task SendPasswordResetAsync(
            string toEmail,
            string firstName,
            string resetLink,
            CancellationToken ct = default);
    }
}
