namespace КР_Ханников.Core
{
                public class NotificationSettings
    {
        public int Id { get; set; }

                public int UserId { get; set; }
        public User User { get; set; } = null!;

                public bool NotifyOnNewTickets { get; set; } = true;

                public bool NotifyOnStatusChanged { get; set; } = true;

                public bool NotifyOnComments { get; set; } = true;

                public bool NotifyOnDueSoon { get; set; } = true;

                                        public int DueSoonThresholdMinutes { get; set; } = 60;

        public bool PlaySound { get; set; } = true;

        public bool ShowToast { get; set; } = true;

        public bool EmailEnabled { get; set; } = false;
    }
}
