using Auth.Application.Profiles.CreateProfile;
using MediatR;
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
        public async Task<IActionResult> CreateProfile(CreateProfileCommand command)
        {
            var result = await _sender.Send(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result.Error);
        }
    }
}
