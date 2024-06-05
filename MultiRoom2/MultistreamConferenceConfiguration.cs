using System.Net.Mail;

namespace WebApplication1
{
    public class MultistreamConferenceConfiguration
    {
        // public string DbName { get; set; }
        public string DbFileName { get; set; }

        public string ServiceHostUrl { get; set; }

        public TimeSpan SessionTimeout { get; set; }

        public TimeSpan TokenTimeout { get; set; }
        public TimeSpan DeliveryTimeout { get; set; }

        public string SmtpServerHost { get; set; }
        public ushort SmtpServerPort { get; set; }
        public string SmtpLogin { get; set; }
        public string SmtpPassword { get; set; }
        public bool SmtpUseSsl { get; set; }
        public bool SmtpUseDefaultCredentials { get; set; }
        public SmtpDeliveryMethod SmtpDeliveryMethod { get; set; }
        public string SmtpPickupDirectoryLocation { get; set; }

        public string LogsDirPath { get; set; }

        public MultistreamConferenceLinkTemplatesType LinkTemplates { get; set; }

        public MultistreamConferenceConfiguration()
        {
        }
    }
}
