using Auth.Application.Profiles.CreateProfile;
using Auth.Application.Profiles.SetProfilePin;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ProfilesController : ControllerBase
    {
        private readonly ISender _sender;

        public ProfilesController(ISender sender)
        {
            _sender = sender;
        }

        [HttpPost]
        [Route("create")]
        [Authorize]
        public async Task<IActionResult> CreateProfile(CreateProfileCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }

        [HttpPost]
        [Route("setpin")]
        [Authorize]
        public async Task<IActionResult> SetProfilePin(SetProfilePinCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }
    }
}
