﻿using System;
using System.ComponentModel.DataAnnotations;

namespace ServiceBase.IdentityServer.Models
{
    public class ExternalAccount
    {
        public Guid UserAccountId { get; set; }
        public string Provider { get; set; }
        public string Subject { get; set; }

        // Maximum length of a valid email address is 254 characters.
        // See Dominic Sayers answer at SO: http://stackoverflow.com/a/574698/99240
        [EmailAddress]
        [StringLength(254)]
        public string Email { get; set; }

        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public UserAccount UserAccount { get; set; }
    }
}
