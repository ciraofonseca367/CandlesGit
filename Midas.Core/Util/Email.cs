using System;
using System.Net;
using System.Net.Mail;

namespace Midas.Util
{
    public class Email
    {
        public static string SmtpServer = "smtp.gmail.com";
        public static string SmtpUserName = "ciro.nola@gmail.com";
        public static string SmtpPassword = "EuGostoDeBuceta";

        public static void SendEmail(string mailTo, string subject, string body) {
            var smtp = new SmtpClient(SmtpServer, 587);

            var fromAddress = new MailAddress("ciro.nola@gmail.com", "Ciro Nola");

            NetworkCredential SMTPUserInfo = new System.Net.NetworkCredential(SmtpUserName, SmtpPassword);
            smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
            smtp.UseDefaultCredentials = false;
            smtp.Credentials = new NetworkCredential(fromAddress.Address, SmtpPassword);
            smtp.EnableSsl = true;
            smtp.Timeout = 20000;

            var mailMessage = new MailMessage
            {
                From = fromAddress,
                Subject = subject,
                Body = body,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(mailTo);
            mailMessage.To.Add("mairalongo5@gmail.com");
            mailMessage.To.Add("leandro.capitani@gmail.com");
            mailMessage.To.Add("thyagoliberalli@gmail.com");

            smtp.Send(mailMessage);
        }
    }
}