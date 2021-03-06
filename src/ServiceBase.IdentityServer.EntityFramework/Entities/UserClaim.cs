﻿using System;

namespace ServiceBase.IdentityServer.EntityFramework.Entities
{
    public class UserClaim
    {
        public Guid UserAccountId { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string ValueType { get; set; }
        public UserAccount UserAccount { get; set; }
    }
}
