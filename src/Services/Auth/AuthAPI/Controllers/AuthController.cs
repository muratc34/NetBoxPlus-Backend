using Auth.Application.Authentication.ChangePassword;
using Auth.Application.Authentication.Login;
using Auth.Application.Authentication.Register;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AuthController : Controller
    {
        private readonly ISender _sender;
        public AuthController(ISender sender)
        {
            _sender = sender;
        }
        [HttpPost]
        [Route("login")]
        public async Task<IActionResult> Login(LoginCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }

        [HttpPost]
        [Route("register")]
        public async Task<IActionResult> CreateUser(RegisterCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }

        [HttpPost]
        [Route("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }
    }
}
