using Shared;

namespace AuthAPI.Model
{
    public class Profile : IEntity
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? ProfileName { get; set; }
        public byte[]? PinHash { get; set; }
        public byte[]? PinSalt { get; set; }
    }
}
