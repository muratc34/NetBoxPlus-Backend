﻿using Shared;

namespace SubscriptionAPI.Model
{
    public class Plan : IEntity
    {
        public Guid Id { get; set; }
        public string? PlanName { get; set; }
        public string? Quality { get; set; }
        public int ProfileCount { get; set; }
        public decimal Amount { get; set; }
    }
}
