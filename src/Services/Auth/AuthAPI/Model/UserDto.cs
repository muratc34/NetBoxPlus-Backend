namespace AuthAPI.Model
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
}
