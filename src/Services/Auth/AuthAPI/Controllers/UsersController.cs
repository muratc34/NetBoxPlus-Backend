using Auth.Application.Users.CreateUser;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shared.Domain.Result;
using System.Reflection;

namespace AuthAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public sealed class UsersController : ControllerBase
    {
        private readonly ISender _sender;
        public UsersController(ISender sender)
        {
            _sender = sender;
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser(CreateUserCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }
    }
}
