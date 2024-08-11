using Auth.Domain.Profiles;

namespace Auth.Application.Users.GetUserById;

public sealed class GetUserByIdResponse
{
    public Guid Id { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Guid? PaymentId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string FullName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public ICollection<GetProfileByUserIdResponse> Profiles{ get; set; }
}

public sealed class GetProfileByUserIdResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string ProfileName { get; set; }
}
