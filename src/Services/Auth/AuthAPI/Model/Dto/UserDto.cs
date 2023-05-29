namespace AuthAPI.Model.Dto
{
    public class UserDto
    {
    }

    public class UserForLoginDto
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }

    public class UserForRegisterDto
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    public class UserForChangePasswordDto
    {
        public Guid Id { get; set; }
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
    }
}
